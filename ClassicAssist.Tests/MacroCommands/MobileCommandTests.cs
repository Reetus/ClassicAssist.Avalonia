using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.SpecialMoves;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class MobileCommandTests
    {
        private PlayerMobile _player;

        public MobileCommandTests()
        {
            _player = new PlayerMobile( 0x01 ) { Name = "Shmoo", Notoriety = Notoriety.Murderer };
            Engine.Player = _player;
            AliasCommands.SetAlias( "self", 0x01 );
        }

        [Fact]
        public void WillGetName()
        {
            string name = EntityCommands.Name( "self" );

            Assert.Equal( "Shmoo", name );
        }

        [Fact]
        public void WillTrimNameItem()
        {
            Engine.Items.Add( new Item( 0x40000000 ) { Name = " need trim " } );

            Assert.Equal( "need trim", EntityCommands.Name( 0x40000000 ) );

            Assert.Equal( string.Empty, EntityCommands.Name( 0x40000001 ) );

            Engine.Items.Remove( 0x40000000 );
        }

        [Fact]
        public void WillGetNotoriety()
        {
            Assert.True( NotorietyCommands.Murderer( "self" ) );
        }

        [Fact]
        public void WillGetHitsNoParam()
        {
            _player.Hits = 100;

            int val = MobileCommands.Hits();

            Assert.Equal( 100, val );

            _player.Hits = 0;
        }

        [Fact]
        public void WillGetHitsSerialParam()
        {
            Mobile mobile = new Mobile( 2 ) { Hits = 200 };

            Engine.Mobiles.Add( mobile );

            int val = MobileCommands.Hits( mobile.Serial );

            Assert.Equal( 200, val );

            Engine.Mobiles.Remove( mobile );
        }

        [Fact]
        public void WillGetHitsStringParam()
        {
            Mobile mobile = new Mobile( 2 ) { Hits = 250 };

            Engine.Mobiles.Add( mobile );

            AliasCommands.SetAlias( "mobile", mobile.Serial );

            int val = MobileCommands.Hits( "mobile" );

            Assert.Equal( 250, val );

            Engine.Mobiles.Remove( mobile );
        }

        [Fact]
        public void WillGetHiddenNoParam()
        {
            _player.Status |= MobileStatus.Hidden;
            _player.Status |= MobileStatus.Female;

            bool hidden = MobileCommands.Hidden();

            Assert.True( hidden );
        }

        [Fact]
        public void WillGetDead()
        {
            Assert.False( MobileCommands.Dead() );

            _player.ID = 0x192;

            Assert.True( MobileCommands.Dead() );

            _player.ID = 0x190;
        }

        [Fact]
        public void WillGetDeadParam()
        {
            Mobile m = new Mobile( 2 );

            Engine.Mobiles.Add( m );

            Assert.False( MobileCommands.Dead( 2 ) );

            m.ID = 0x192;

            Assert.True( MobileCommands.Dead( 2 ) );

            Engine.Mobiles.Remove( m );
        }

        [Fact]
        public void WillGetMountedParam()
        {
            Item mount = new Item( 2 );

            Engine.Items.Add( mount );

            AliasCommands.SetAlias( "self", 1 );
            AliasCommands.SetAlias( "mount", 2 );

            Assert.False( MobileCommands.Mounted( "self" ) );

            _player.SetLayer( Layer.Mount, 2 );

            Assert.True( MobileCommands.Mounted( "self" ) );

            Engine.Items.Remove( mount );
            _player.SetLayer( Layer.Mount, 0 );
        }

        [Fact]
        public void WillGetMaxHits()
        {
            _player.HitsMax = 100;

            Assert.Equal( 100, MobileCommands.MaxHits( _player.Serial ) );
        }

        [Fact]
        public void WillGetHits()
        {
            _player.Hits = 100;

            Assert.Equal( 100, MobileCommands.Hits( _player.Serial ) );
        }

        [Fact]
        public void WillGetDiffHits()
        {
            _player.Hits = 90;
            _player.HitsMax = 100;

            Assert.Equal( 10, MobileCommands.DiffHits( _player.Serial ) );
        }

        [Fact]
        public void WillGetStats()
        {
            _player.Strength = 100;
            _player.Int = 90;
            _player.Dex = 25;

            Assert.Equal( 100, MobileCommands.Str() );
            Assert.Equal( 90, MobileCommands.Int() );
            Assert.Equal( 25, MobileCommands.Dex() );
        }

        [Fact]
        public void WillEnableDisableSpecialMove()
        {
            SpecialMovesManager manager = SpecialMovesManager.GetInstance();

            byte[] enablePacket = { 0xBF, 0x00, 0x08, 0x00, 0x25, 0x01, 0xF6, 0x01 };
            byte[] disablePacket = { 0xBF, 0x00, 0x08, 0x00, 0x25, 0x01, 0xF6, 0x00 };

            IncomingPacketHandlers.Initialize();

            PacketHandler handler = IncomingPacketHandlers.GetHandler( 0xBF );

            handler?.OnReceive( new PacketReader( enablePacket, enablePacket.Length, false ) );

            bool result = EntityCommands.SpecialMoveExists( "Death Strike" );

            Assert.True( result );

            handler?.OnReceive( new PacketReader( disablePacket, disablePacket.Length, false ) );

            result = EntityCommands.SpecialMoveExists( "Death Strike" );

            Assert.False( result );
        }
    }
}