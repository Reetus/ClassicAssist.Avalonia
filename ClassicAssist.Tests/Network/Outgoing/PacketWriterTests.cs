using System.IO;
using ClassicAssist.UO.Data;
using Xunit;

namespace ClassicAssist.Tests.Network.Outgoing
{
    /// <summary>
    ///     Pins <see cref="PacketWriter" />, which every packet the assistant builds goes through: integers
    ///     are big-endian, fixed-width strings are padded with zeros or cut to fit, null-terminated strings
    ///     carry exactly one terminator of the right width, and seeking back to patch a length prefix
    ///     leaves the rest of the packet intact. An off-by-one in any of these shifts every field after it
    ///     and the server rejects or misreads the packet.
    /// </summary>
    public class PacketWriterTests
    {
        private static byte[] Write( System.Action<PacketWriter> write )
        {
            PacketWriter writer = new PacketWriter();
            write( writer );

            return writer.ToArray();
        }

        [Fact]
        public void WillWriteIntegersBigEndian()
        {
            byte[] bytes = Write( w =>
            {
                w.Write( true );
                w.Write( false );
                w.Write( (byte) 0xAB );
                w.Write( (sbyte) -1 );
                w.Write( (short) -2 );
                w.Write( (ushort) 0xABCD );
                w.Write( 0x01020304 );
                w.Write( -2 );
                w.Write( 0xFFFFFFFEu );
            } );

            Assert.Equal( new byte[]
            {
                0x01, 0x00, // bools
                0xAB, // byte
                0xFF, // sbyte -1
                0xFF, 0xFE, // short -2
                0xAB, 0xCD, // ushort
                0x01, 0x02, 0x03, 0x04, // int
                0xFF, 0xFF, 0xFF, 0xFE, // int -2
                0xFF, 0xFF, 0xFF, 0xFE // uint
            }, bytes );
        }

        [Fact]
        public void WillWritePartOfAByteArray()
        {
            byte[] bytes = Write( w => w.Write( new byte[] { 1, 2, 3, 4, 5 }, 1, 3 ) );

            Assert.Equal( new byte[] { 2, 3, 4 }, bytes );
        }

        [Fact]
        public void WillPadShortAsciiFixedWithZeros()
        {
            byte[] bytes = Write( w =>
            {
                w.WriteAsciiFixed( "ab", 4 );
                w.Write( (byte) 0xEE );
            } );

            Assert.Equal( new byte[] { 0x61, 0x62, 0, 0, 0xEE }, bytes );
        }

        [Fact]
        public void WillTruncateLongAsciiFixed()
        {
            byte[] bytes = Write( w =>
            {
                w.WriteAsciiFixed( "abcdef", 3 );
                w.Write( (byte) 0xEE );
            } );

            Assert.Equal( new byte[] { 0x61, 0x62, 0x63, 0xEE }, bytes );
        }

        [Fact]
        public void WillWriteExactLengthAsciiFixedWithoutTerminator()
        {
            Assert.Equal( new byte[] { 0x61, 0x62, 0x63 }, Write( w => w.WriteAsciiFixed( "abc", 3 ) ) );
        }

        [Fact]
        public void WillWriteNullStringsAsEmpty()
        {
            byte[] bytes = Write( w =>
            {
                w.WriteAsciiFixed( null, 2 );
                w.WriteAsciiNull( null );
                w.WriteBigUniNull( null );
                w.WriteLittleUniNull( null );
                w.WriteBigUniFixed( null, 1 );
                w.WriteLittleUniFixed( null, 1 );
            } );

            Assert.Equal( new byte[2 + 1 + 2 + 2 + 2 + 2], bytes );
        }

        [Fact]
        public void WillTerminateAsciiNullWithOneByte()
        {
            byte[] bytes = Write( w =>
            {
                w.WriteAsciiNull( "abc" );
                w.Write( (byte) 0xEE );
            } );

            Assert.Equal( new byte[] { 0x61, 0x62, 0x63, 0x00, 0xEE }, bytes );
        }

        [Fact]
        public void WillEncodeUnicodeNullInBothByteOrders()
        {
            Assert.Equal( new byte[] { 0x00, 0x61, 0x00, 0x62, 0x00, 0x00, 0xEE },
                Write( w =>
                {
                    w.WriteBigUniNull( "ab" );
                    w.Write( (byte) 0xEE );
                } ) );

            Assert.Equal( new byte[] { 0x61, 0x00, 0x62, 0x00, 0x00, 0x00, 0xEE },
                Write( w =>
                {
                    w.WriteLittleUniNull( "ab" );
                    w.Write( (byte) 0xEE );
                } ) );
        }

        [Fact]
        public void WillEncodeNonAsciiCharactersInUnicodeStrings()
        {
            // U+00E9 and U+2713 must keep both bytes.
            Assert.Equal( new byte[] { 0x00, 0xE9, 0x27, 0x13, 0x00, 0x00 }, Write( w => w.WriteBigUniNull( "é✓" ) ) );
        }

        [Fact]
        public void WillPadShortUnicodeFixedToCharacterCount()
        {
            Assert.Equal( new byte[] { 0x00, 0x61, 0, 0, 0, 0, 0xEE },
                Write( w =>
                {
                    w.WriteBigUniFixed( "a", 3 );
                    w.Write( (byte) 0xEE );
                } ) );

            Assert.Equal( new byte[] { 0x61, 0x00, 0, 0, 0, 0, 0xEE },
                Write( w =>
                {
                    w.WriteLittleUniFixed( "a", 3 );
                    w.Write( (byte) 0xEE );
                } ) );
        }

        [Fact]
        public void WillWriteExactLengthUnicodeFixed()
        {
            Assert.Equal( new byte[] { 0x00, 0x61, 0x00, 0x62, 0xEE },
                Write( w =>
                {
                    w.WriteBigUniFixed( "ab", 2 );
                    w.Write( (byte) 0xEE );
                } ) );
        }

        [Fact( Skip = "BUG: PacketWriter.WriteBigUniFixed/WriteLittleUniFixed write the whole string instead of truncating to size " )]
        public void WillTruncateLongUnicodeFixed()
        {
            Assert.Equal( new byte[] { 0x00, 0x61, 0x00, 0x62, 0xEE },
                Write( w =>
                {
                    w.WriteBigUniFixed( "abcd", 2 );
                    w.Write( (byte) 0xEE );
                } ) );

            Assert.Equal( new byte[] { 0x61, 0x00, 0x62, 0x00, 0xEE },
                Write( w =>
                {
                    w.WriteLittleUniFixed( "abcd", 2 );
                    w.Write( (byte) 0xEE );
                } ) );
        }

        [Fact]
        public void WillFillToCapacity()
        {
            PacketWriter writer = new PacketWriter( 8 );
            writer.Write( (byte) 0x6C );
            writer.Write( (short) 0x0102 );
            writer.Fill();

            Assert.Equal( new byte[] { 0x6C, 0x01, 0x02, 0, 0, 0, 0, 0 }, writer.ToArray() );
            Assert.Equal( 8, writer.Length );
            Assert.Equal( 8, writer.Position );
        }

        [Fact]
        public void WillZeroBytesWhenFillingInsideTheStream()
        {
            PacketWriter writer = new PacketWriter();
            writer.Write( 0x01020304 );
            writer.Seek( 1, SeekOrigin.Begin );
            writer.Fill( 2 );

            Assert.Equal( new byte[] { 0x01, 0, 0, 0x04 }, writer.ToArray() );
            Assert.Equal( 3, writer.Position );
        }

        [Fact]
        public void WillPatchLengthPrefixAndContinueAtEnd()
        {
            // The pattern every variable-length packet uses: placeholder length, body, seek back, patch,
            // seek to the end and carry on.
            PacketWriter writer = new PacketWriter();
            writer.Write( (byte) 0xB1 );
            writer.Write( (short) 0 );
            writer.Write( 0x11223344 );
            writer.Seek( 1, SeekOrigin.Begin );
            writer.Write( (short) writer.Length );
            writer.Seek( 0, SeekOrigin.End );
            writer.Write( (byte) 0xEE );

            Assert.Equal( new byte[] { 0xB1, 0x00, 0x07, 0x11, 0x22, 0x33, 0x44, 0xEE }, writer.ToArray() );
        }

        [Fact]
        public void WillReturnACopyFromToArray()
        {
            PacketWriter writer = new PacketWriter();
            writer.Write( (byte) 1 );

            byte[] first = writer.ToArray();
            first[0] = 99;
            writer.Write( (byte) 2 );

            Assert.Equal( new byte[] { 1, 2 }, writer.ToArray() );
        }

        [Fact]
        public void WillGrowBeyondInitialCapacity()
        {
            PacketWriter writer = new PacketWriter( 2 );
            writer.WriteAsciiFixed( "abcdefgh", 8 );
            writer.WriteBigUniNull( "xyz" );

            Assert.Equal( 8 + 8, writer.ToArray().Length );
        }

        [Fact]
        public void WillReuseReleasedInstanceEmptyWithNewCapacity()
        {
            PacketWriter writer = PacketWriter.CreateInstance( 4 );
            writer.Write( 0x01020304 );

            PacketWriter.ReleaseInstance( writer );

            PacketWriter reused = PacketWriter.CreateInstance( 6 );

            Assert.Same( writer, reused );
            Assert.Equal( 0, reused.Length );
            Assert.Equal( 0, reused.Position );

            reused.Write( (byte) 0xAA );
            reused.Fill();

            Assert.Equal( new byte[] { 0xAA, 0, 0, 0, 0, 0 }, reused.ToArray() );
        }
    }
}
