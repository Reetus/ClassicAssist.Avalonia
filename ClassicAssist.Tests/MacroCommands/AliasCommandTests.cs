using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class AliasCommandTests
    {
        private PlayerMobile _player;

        public AliasCommandTests()
        {
            _player = new PlayerMobile( 0x01 );
            Engine.Player = _player;
        }

        [Fact]
        public void WillSetDefaultAliases()
        {
            AliasCommands.SetDefaultAliases();

            Assert.Equal( _player.Serial, AliasCommands.GetAlias( "self" ) );
        }

        [Fact]
        public void WillChangeLastAliasOnTargetSent()
        {
            _player.LastTargetSerial = 0x02;

            AliasCommands.SetDefaultAliases();

            Assert.Equal( 0x02, AliasCommands.GetAlias( "last" ) );

            OutgoingPacketHandlers.Initialize();
            PacketHandler handler = OutgoingPacketHandlers.GetHandler( 0x6C );

            byte[] packet =
            {
                0x6C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xAA, 0xBB, 0xCC, 0xDD, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };

            handler?.OnReceive( new PacketReader( packet, packet.Length, true ) );

            Assert.Equal( 0xAABBCCDD, (uint) AliasCommands.GetAlias( "last" ) );
        }
    }
}