using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests
{
    public class IncomingPacketHandlerTests
    {
        public IncomingPacketHandlerTests()
        {
            Engine.Items = new ItemCollection( 0 );
            Engine.Mobiles = new MobileCollection( Engine.Items );
            Engine.Player = new PlayerMobile( 1 );

            IncomingPacketHandlers.Initialize();
        }

        [Fact]
        public void ContainerContentsWillAddToPlayerBackpack()
        {
            const int backpackSerial = 0x40000001;
            const int itemSerial = 0x40000002;

            Item backpack = new Item( backpackSerial, 1 ) { Layer = Layer.Backpack };
            Engine.Player.SetLayer( Layer.Backpack, backpack.Serial );
            Engine.Items.Add( backpack );

            Assert.True( Engine.Player.Backpack.Serial > 0 );

            ContainerContentsPacket cc =
                new ContainerContentsPacket( new ItemCollection( backpackSerial ) { new Item( itemSerial ) } );

            PacketHandler handler = IncomingPacketHandlers.GetHandler( 0x3C );

            Assert.NotNull( handler );

            byte[] packet = cc.ToArray();

            handler.OnReceive( new PacketReader( packet, packet.Length, false ) );

            Item item = Engine.Items.GetItem( backpackSerial );

            Assert.NotNull( item.Container );
            Assert.Equal( 1, item.Container.GetTotalItemCount() );
            Assert.NotNull( Engine.Items.GetItem( itemSerial ) );
            Assert.NotNull( item.Container.GetItem( itemSerial ) );

            Engine.Items.Clear();
            Engine.Player.SetLayer( Layer.Backpack, 0 );
        }

        [Fact]
        public void WillSetPoisonedOnHealthbarColour()
        {
            Engine.Player = new PlayerMobile( 0x01 );

            byte[] packet = { 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x01, 0x01 };

            PacketHandler handler = IncomingPacketHandlers.GetHandler( 0x17 );

            Assert.NotNull( handler );

            handler.OnReceive( new PacketReader( packet, packet.Length, false ) );

            Assert.True( Engine.Player.IsPoisoned );

            packet = new byte[] { 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x01, 0x00 };

            handler.OnReceive( new PacketReader( packet, packet.Length, false ) );

            Assert.False( Engine.Player.IsPoisoned );

            Engine.Player = null;
        }

        [Fact]
        public void WillSetYellowHitsOnHealthbarColour()
        {
            Engine.Player = new PlayerMobile( 0x01 );

            byte[] packet = { 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x02, 0x01 };

            PacketHandler handler = IncomingPacketHandlers.GetHandler( 0x17 );

            Assert.NotNull( handler );

            handler.OnReceive( new PacketReader( packet, packet.Length, false ) );

            Assert.True( Engine.Player.IsYellowHits );

            packet = new byte[] { 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x02, 0x00 };

            handler.OnReceive( new PacketReader( packet, packet.Length, false ) );

            Assert.False( Engine.Player.IsYellowHits );

            Engine.Player = null;
        }

        [Fact]
        public void WillSetFrozenOnMobileStatus()
        {
            Engine.Player = new PlayerMobile( 0x01 );

            Assert.False( Engine.Player.IsFrozen );

            byte[] packet =
            {
                0x20, 0x00, 0x00, 0x00, 0x01, 0x01, 0x90, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00
            };

            PacketHandler handler = IncomingPacketHandlers.GetHandler( 0x20 );

            Assert.NotNull( handler );

            handler.OnReceive( new PacketReader( packet, packet.Length, true ) );

            Assert.True( Engine.Player.IsFrozen );

            Engine.Player = null;
        }

        [Fact]
        public void MapChangeToSameMapKeepsMultis()
        {
            Engine.Player.Map = Map.Trammel;
            Engine.Items.Add( new Item( 0x40000010 ) { ArtDataID = 2, X = 100, Y = 100 } );

            SendMapChange( Map.Trammel );

            Assert.NotNull( Engine.Items.GetItem( 0x40000010 ) ); // "the same map again must not drop multis"
            Assert.Equal( Map.Trammel, Engine.Player.Map );

            Engine.Items.Clear();
        }

        [Fact]
        public void MapChangeToOtherMapClearsMultisOnly()
        {
            Engine.Player.Map = Map.Trammel;
            Engine.Items.Add( new Item( 0x40000010 ) { ArtDataID = 2, X = 100, Y = 100 } );
            Engine.Items.Add( new Item( 0x40000011 ) { X = 100, Y = 100 } );

            SendMapChange( Map.Felucca );

            Assert.Null( Engine.Items.GetItem( 0x40000010 ) ); // "a facet change drops the previous facet's multis"
            Assert.NotNull( Engine.Items.GetItem( 0x40000011 ) ); // "non-multis are left alone"
            Assert.Equal( Map.Felucca, Engine.Player.Map );

            Engine.Items.Clear();
        }

        private static void SendMapChange( Map map )
        {
            byte[] packet = { 0xBF, 0x00, 0x06, 0x00, 0x08, (byte) map };

            PacketHandler handler = IncomingPacketHandlers.GetHandler( 0xBF );

            Assert.NotNull( handler );

            handler.OnReceive( new PacketReader( packet, packet.Length, false ) );
        }
    }
}