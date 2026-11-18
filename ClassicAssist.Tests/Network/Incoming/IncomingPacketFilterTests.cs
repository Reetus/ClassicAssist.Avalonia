using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using ClassicAssist.Data.Filters;
using ClassicAssist.Misc;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests.Network.Incoming
{
    /// <summary>
    ///     <see cref="IncomingPacketFilters.CheckPacket" /> for the rehue paths (0x20, 0x77, 0x78, 0xF3), the
    ///     light level watch (0x4F) and the dynamic filters. A rehued mobile packet is swallowed, a recoloured
    ///     copy goes to the client, and the original must still reach the handlers - through the incoming
    ///     queue, or for 0xF3 directly - so the world model keeps the real hue. Losing that last step leaves
    ///     the rehued mobile frozen where it was first seen.
    /// </summary>
    public class IncomingPacketFilterTests : IncomingPacketTestBase
    {
        private const int REHUED = 0x00000E01;
        private const int OTHER = 0x00000E02;
        private const int REHUE_HUE = 0x0021;

        private static readonly FieldInfo _lightLevelField =
            typeof( LightLevelFilter ).GetField( "_serverLightLevel", BindingFlags.NonPublic | BindingFlags.Static );

        private readonly ThreadQueue<Packet> _originalQueue;
        private readonly List<byte[]> _queued = new List<byte[]>();
        private readonly ThreadQueue<Packet> _queue;
        private readonly SemaphoreSlim _queuedSignal = new SemaphoreSlim( 0 );

        public IncomingPacketFilterTests()
        {
            _originalQueue = Engine.IncomingQueue;
            _queue = new ThreadQueue<Packet>( p =>
            {
                lock ( _queued )
                {
                    _queued.Add( p.GetPacket().Take( p.GetLength() ).ToArray() );
                }

                _queuedSignal.Release();
            } );
            Engine.IncomingQueue = _queue;

            Engine.RehueList.Add( REHUED, REHUE_HUE );
        }

        public override void Dispose()
        {
            Engine.RehueList.Remove( REHUED );
            Engine.IncomingQueue = _originalQueue;
            _queue.Dispose();
            _queuedSignal.Dispose();

            base.Dispose();
        }

        [Fact]
        public void WillPassUnrehuedMobileUpdate()
        {
            using PacketRecorder recorder = new PacketRecorder();

            Assert.False( Check( MobileUpdate( OTHER, 0x0455 ) ) );
            Assert.Empty( recorder.Received );
        }

        [Fact]
        public void WillRehueAndRequeueMobileUpdate()
        {
            using PacketRecorder recorder = new PacketRecorder();

            byte[] packet = MobileUpdate( REHUED, 0x0455 );

            Assert.True( Check( packet ) );

            byte[] rehued = Assert.Single( recorder.ReceivedWithId( 0x20 ) );

            Assert.Equal( REHUE_HUE, ( rehued[8] << 8 ) | rehued[9] );
            Assert.Equal( packet, WaitForQueued() );
        }

        /// <summary>
        ///     The filter keeps the running flag in the direction (the handler masks it later), so the copy
        ///     sent to the client still shows the mobile running.
        /// </summary>
        [Fact]
        public void WillRehueKeepingRunningFlagAndRequeueMobileMoving()
        {
            using PacketRecorder recorder = new PacketRecorder();

            byte[] packet = Fixed( 0x77, w =>
            {
                w.Write( REHUED );
                w.Write( (short) 0x0190 );
                w.Write( (short) 100 );
                w.Write( (short) 200 );
                w.Write( (sbyte) 0 );
                w.Write( (byte) 0x82 ); // running, east
                w.Write( (short) 0x0455 );
                w.Write( (byte) 0 );
                w.Write( (byte) Notoriety.Innocent );
            } );

            Assert.True( Check( packet ) );

            byte[] rehued = Assert.Single( recorder.ReceivedWithId( 0x77 ) );

            Assert.Equal( 0x82, rehued[12] );
            Assert.Equal( REHUE_HUE, ( rehued[13] << 8 ) | rehued[14] );
            Assert.Equal( packet, WaitForQueued() );
        }

        [Fact]
        public void WillRehueBodyAndEquipmentAndRequeueMobileIncoming()
        {
            using PacketRecorder recorder = new PacketRecorder();

            byte[] packet = MobileIncoming( REHUED, 0x0190, 100, 200, 0, 0, 0x0455, MobileStatus.None,
                Notoriety.Innocent, false, ( 0x40000E01, 0x1415, Layer.InnerTorso, 0x0123 ) );

            Assert.True( Check( packet ) );

            byte[] rehued = Assert.Single( recorder.ReceivedWithId( 0x78 ) );

            Assert.Equal( REHUE_HUE, ( rehued[15] << 8 ) | rehued[16] ); // body
            Assert.Equal( REHUE_HUE, ( rehued[26] << 8 ) | rehued[27] ); // the torso
            Assert.Equal( packet, WaitForQueued() );
        }

        [Fact]
        public void WillPassUnrehuedMobileIncoming()
        {
            byte[] packet = MobileIncoming( OTHER, 0x0190, 100, 200, 0, 0, 0x0455, MobileStatus.None,
                Notoriety.Innocent, false );

            Assert.False( Check( packet ) );
        }

        /// <summary>
        ///     0xF3 is not re-queued: the filter hands the original to the handler itself, so the client
        ///     shows the rehued copy and the world model keeps the real hue.
        /// </summary>
        [Fact]
        public void WillRehueAndHandleSAWorldItemDirectly()
        {
            const int item = 0x00000E01; // the rehue list is keyed by serial; reuse it for an item

            using PacketRecorder recorder = new PacketRecorder();

            byte[] packet = Fixed( 0xF3, w =>
            {
                w.Write( (short) 1 );
                w.Write( (byte) 0 );
                w.Write( item );
                w.Write( (short) 0x0EED );
                w.Write( (byte) 0 );
                w.Write( (short) 5 );
                w.Write( (short) 5 );
                w.Write( (short) 1001 );
                w.Write( (short) 1002 );
                w.Write( (sbyte) 0 );
                w.Write( (byte) 0 );
                w.Write( (short) 0x0455 );
                w.Write( (byte) 0 );
                w.Write( (short) 0 );
            } );

            Assert.True( Check( packet ) );

            byte[] rehued = Assert.Single( recorder.ReceivedWithId( 0xF3 ) );

            Assert.Equal( REHUE_HUE, ( rehued[21] << 8 ) | rehued[22] );
            Assert.Equal( 0x0455, Engine.Items.GetItem( item ).Hue );
        }

        [Fact]
        public void WillRecordServerLightLevelAndPassGlobalLightLevel()
        {
            object original = _lightLevelField.GetValue( null );

            try
            {
                Assert.False( Check( new byte[] { 0x4F, 0x12 } ) );
                Assert.Equal( 0x12, _lightLevelField.GetValue( null ) );
            }
            finally
            {
                _lightLevelField.SetValue( null, original );
            }
        }

        /// <summary>
        ///     Dynamic filters see every incoming packet, including ids that have a registered handler
        ///     which let the packet through - and only while enabled.
        /// </summary>
        [Fact]
        public void WillRunEnabledDynamicFiltersAfterRegisteredHandler()
        {
            BlockingFilter filter = new BlockingFilter( 0x20 );

            try
            {
                byte[] packet = MobileUpdate( OTHER, 0x0455 );

                Assert.False( Check( packet ) );
                Assert.Equal( 0, filter.Calls );

                filter.Enabled = true;

                Assert.True( Check( packet ) );
                Assert.Equal( 1, filter.Calls );
            }
            finally
            {
                DynamicFilterEntry.Filters.Remove( filter );
            }
        }

        [Fact]
        public void WillPassPacketWithNoFilter()
        {
            // 0x65 is only claimed by the weather filter, which is on by default and stays on once any
            // earlier test has built the filter list - so turn it off for the duration.
            bool weather = WeatherFilter.IsEnabled;
            WeatherFilter.IsEnabled = false;

            try
            {
                Assert.False( Check( new byte[] { 0x65, 0x01, 0x46, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 } ) );
            }
            finally
            {
                WeatherFilter.IsEnabled = weather;
            }
        }

        private static bool Check( byte[] packet )
        {
            byte[] data = packet;
            int length = packet.Length;

            return IncomingPacketFilters.CheckPacket( ref data, ref length );
        }

        private byte[] WaitForQueued()
        {
            Assert.True( _queuedSignal.Wait( TimeSpan.FromSeconds( 5 ), TestContext.Current.CancellationToken ) );

            lock ( _queued )
            {
                return Assert.Single( _queued );
            }
        }

        private static byte[] MobileUpdate( int serial, int hue )
        {
            return Fixed( 0x20, w =>
            {
                w.Write( serial );
                w.Write( (short) 0x0190 );
                w.Write( (byte) 0 );
                w.Write( (short) hue );
                w.Write( (byte) 0 );
                w.Write( (short) 100 );
                w.Write( (short) 200 );
                w.Write( (short) 0 );
                w.Write( (byte) 0 );
                w.Write( (sbyte) 0 );
            } );
        }

        private sealed class BlockingFilter : DynamicFilterEntry
        {
            private readonly byte _packetId;

            public BlockingFilter( byte packetId )
            {
                _packetId = packetId;
            }

            public int Calls { get; private set; }

            public override bool CheckPacket( ref byte[] packet, ref int length, PacketDirection direction )
            {
                if ( packet[0] != _packetId )
                {
                    return false;
                }

                Calls++;

                return true;
            }
        }
    }
}
