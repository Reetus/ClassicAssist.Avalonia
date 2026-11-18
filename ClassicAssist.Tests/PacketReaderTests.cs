using System.IO;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using Xunit;

namespace ClassicAssist.Tests
{
    public class PacketReaderTests
    {
        [Fact]
        public void PacketReaderWillReadCorrect()
        {
            byte[] packet = { 0x1B, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x0A };

            PacketReader reader = new PacketReader( packet, packet.Length, true );

            int int32 = reader.ReadInt32();
            int int16 = reader.ReadInt16();
            int int8 = reader.ReadByte();

            Assert.Equal( unchecked( (int) 0xAABBCCDD ), int32 );
            Assert.Equal( unchecked( (short) 0xEEFF ), int16 );
            Assert.Equal( 0x0A, int8 );

            reader.Seek( 1, SeekOrigin.Begin );

            uint uint32 = reader.ReadUInt32();
            uint uint16 = reader.ReadUInt16();
            sbyte uint8 = reader.ReadSByte();

            Assert.Equal( 0xAABBCCDD, uint32 );
            Assert.Equal( (ushort) 0xEEFF, uint16 );
            Assert.Equal( 0x0A, uint8 );
        }
    }
}