using System.Linq;
using ClassicAssist.Data;
using ClassicAssist.Data.Targeting;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using Xunit;

namespace ClassicAssist.Tests.Network.Incoming
{
    /// <summary>
    ///     0x6C / 0x99 target cursors and 0x6F secure trade. The target state (exists, type, flags, the
    ///     cursor id to echo back) is what every WaitForTarget / Target command answers against, a cancel
    ///     must not count as a cursor, and with "queue last target" on an incoming cursor is answered from the
    ///     queue on the spot. Trade values mean different things per action, so they must land in the right
    ///     fields.
    /// </summary>
    public class TargetAndTradePacketTests : IncomingPacketTestBase
    {
        private const int CURSOR = 0x00ABCDEF;
        private const int MOBILE = 0x00000901;

        [Fact]
        public void WillSetTargetStateOnTargetCursor()
        {
            Engine.WaitingForTarget = true;
            Engine.InternalTarget = true;

            Receive( TargetCursor( TargetType.Tile, CURSOR, TargetFlags.Harmful ) );

            Assert.True( Engine.TargetExists );
            Assert.Equal( TargetType.Tile, Engine.TargetType );
            Assert.Equal( CURSOR, Engine.TargetSerial );
            Assert.Equal( TargetFlags.Harmful, Engine.TargetFlags );
            Assert.False( Engine.WaitingForTarget );
            Assert.False( Engine.InternalTarget );
        }

        /// <summary>A server cancel (flags 3) clears the cursor, and is not what a waiting macro waits for.</summary>
        [Fact]
        public void WillClearTargetOnCancelTargetCursor()
        {
            Receive( TargetCursor( TargetType.Object, CURSOR, TargetFlags.None ) );

            Engine.WaitingForTarget = true;

            Receive( TargetCursor( TargetType.Object, CURSOR, TargetFlags.Cancel ) );

            Assert.False( Engine.TargetExists );
            Assert.True( Engine.WaitingForTarget );
        }

        /// <summary>0x99 (multi placement) shares the 0x6C handler: the first bytes have the same layout.</summary>
        [Fact]
        public void WillSetTargetStateOnMultiTargetCursor()
        {
            Receive( Fixed( 0x99, w =>
            {
                w.Write( (byte) 1 ); // allow ground
                w.Write( CURSOR ); // deed serial
                w.Write( (byte) 0 );
                w.Fill( 30 - 7 );
            } ) );

            Assert.True( Engine.TargetExists );
            Assert.Equal( CURSOR, Engine.TargetSerial );
            Assert.Equal( TargetType.Tile, Engine.TargetType );
        }

        [Fact]
        public void WillAnswerCursorFromQueueWhenQueueLastTargetEnabled()
        {
            AddMobile( MOBILE );
            Options.CurrentOptions.QueueLastTarget = true;
            Options.CurrentOptions.RangeCheckLastTarget = false;
            Engine.LastTargetQueue.Enqueue( new TargetQueueObject { Object = MOBILE } );

            using PacketRecorder recorder = new PacketRecorder();

            Receive( TargetCursor( TargetType.Object, CURSOR, TargetFlags.Beneficial ) );

            byte[] response = Assert.Single( recorder.SentWithId( 0x6C ) );

            Assert.Equal( MOBILE, ( response[7] << 24 ) | ( response[8] << 16 ) | ( response[9] << 8 ) | response[10] );
            Assert.Equal( 0, Engine.LastTargetQueue.Count );
        }

        [Fact]
        public void WillKeepQueueOnCancelWhenQueueLastTargetEnabled()
        {
            Options.CurrentOptions.QueueLastTarget = true;
            Engine.LastTargetQueue.Enqueue( new TargetQueueObject { Object = MOBILE } );

            using PacketRecorder recorder = new PacketRecorder();

            Receive( TargetCursor( TargetType.Object, CURSOR, TargetFlags.Cancel ) );

            Assert.Empty( recorder.Sent );
            Assert.Equal( 1, Engine.LastTargetQueue.Count );
            Assert.False( Engine.TargetExists );
        }

        [Fact]
        public void WontAnswerFromQueueWhenQueueLastTargetDisabled()
        {
            Options.CurrentOptions.QueueLastTarget = false;
            Engine.LastTargetQueue.Enqueue( new TargetQueueObject { Object = MOBILE } );

            using PacketRecorder recorder = new PacketRecorder();

            Receive( TargetCursor( TargetType.Object, CURSOR, TargetFlags.None ) );

            Assert.Empty( recorder.Sent );
            Assert.Equal( 1, Engine.LastTargetQueue.Count );
        }

        [Fact]
        public void WillSetContainersOnTradeStart()
        {
            Receive( SecureTrade( TradeAction.Start, MOBILE, 0x40000A01, 0x40000A02 ) );

            Assert.Equal( TradeAction.Start, Engine.Trade.Action );
            Assert.Equal( MOBILE, Engine.Trade.Serial );
            Assert.Equal( 0x40000A01, Engine.Trade.ContainerLocal );
            Assert.Equal( 0x40000A02, Engine.Trade.ContainerRemote );
        }

        [Fact]
        public void WillSetAcceptStateOnTradeUpdate()
        {
            Receive( SecureTrade( TradeAction.Start, MOBILE, 0x40000A01, 0x40000A02 ) );
            Receive( SecureTrade( TradeAction.Update, MOBILE, 1, 0 ) );

            Assert.Equal( TradeAction.Update, Engine.Trade.Action );
            Assert.Equal( 1, Engine.Trade.AcceptLocal );
            Assert.Equal( 0, Engine.Trade.AcceptRemote );
            Assert.Equal( 0x40000A01, Engine.Trade.ContainerLocal ); // an update leaves the containers alone
        }

        [Fact]
        public void WillSetRemoteGoldOnTradeGold()
        {
            Receive( SecureTrade( TradeAction.Gold, MOBILE, 50000, 3 ) );

            Assert.Equal( 50000, Engine.Trade.GoldRemote );
            Assert.Equal( 3, Engine.Trade.PlatinumRemote );
            Assert.Equal( 0, Engine.Trade.GoldLocal );
        }

        [Fact]
        public void WillOnlyRecordActionOnTradeCancel()
        {
            Receive( SecureTrade( TradeAction.Start, MOBILE, 0x40000A01, 0x40000A02 ) );
            Receive( SecureTrade( TradeAction.Cancel, 0x40000A01, 0, 0 ) );

            Assert.Equal( TradeAction.Cancel, Engine.Trade.Action );
            Assert.Equal( 0x40000A02, Engine.Trade.ContainerRemote );
            Assert.Equal( 0, Engine.Trade.AcceptLocal );
        }

        private static byte[] TargetCursor( TargetType type, int cursor, TargetFlags flags )
        {
            return Fixed( 0x6C, w =>
            {
                w.Write( (byte) type );
                w.Write( cursor );
                w.Write( (byte) flags );
                w.Fill( 19 - 7 );
            } );
        }

        private static byte[] SecureTrade( TradeAction action, int serial, int value1, int value2 )
        {
            return Dynamic( 0x6F, w =>
            {
                w.Write( (byte) action );
                w.Write( serial );
                w.Write( value1 );
                w.Write( value2 );
                w.Write( (byte) 0 ); // no name
            } );
        }
    }
}
