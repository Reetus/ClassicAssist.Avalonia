using System;
using System.Linq;
using System.Text;
using ClassicAssist.UO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class CompressionTests
    {
        [TestMethod]
        public void WillRoundTrip()
        {
            byte[] original = Encoding.ASCII.GetBytes(
                string.Concat( Enumerable.Repeat( "{ page 0 }{ resizepic 0 0 2600 300 200 }", 32 ) ) );

            byte[] compressed = new byte[original.Length + 64];
            int compressedLength = Compression.Compress( original, ref compressed );

            Assert.IsTrue( compressedLength > 0, "compressor reported nothing written" );
            Assert.IsTrue( compressedLength < original.Length, "highly repetitive input should shrink" );

            byte[] decompressed = new byte[original.Length];
            int decompressedLength = 0;

            Assert.IsTrue( Compression.Uncompress( ref decompressed, ref decompressedLength,
                compressed.Take( compressedLength ).ToArray(), compressedLength ) );

            Assert.AreEqual( original.Length, decompressedLength );
            CollectionAssert.AreEqual( original, decompressed );
        }

        /// <summary>
        ///     Gump packets size the destination as the server-reported length plus one, so the buffer is
        ///     always larger than the payload. Decompressing has to succeed anyway and report the real length.
        /// </summary>
        [TestMethod]
        public void WillUncompressIntoOversizedBuffer()
        {
            byte[] payload = Encoding.ASCII.GetBytes( "{ button 10 10 4005 4007 1 0 1 }" );

            byte[] compressed = new byte[payload.Length + 64];
            int compressedLength = Compression.Compress( payload, ref compressed );

            byte[] decompressed = new byte[payload.Length + 1];
            int decompressedLength = 0;

            Assert.IsTrue( Compression.Uncompress( ref decompressed, ref decompressedLength,
                compressed.Take( compressedLength ).ToArray(), compressedLength ) );

            Assert.AreEqual( payload.Length, decompressedLength );
            Assert.AreEqual( "{ button 10 10 4005 4007 1 0 1 }",
                Encoding.ASCII.GetString( decompressed ).TrimEnd( '\0' ) );
        }

        [TestMethod]
        public void WillUncompressKnownZlibStream()
        {
            // The compressed gump layout carried by GumpParserTests.WontThrowExceptionEmptyLayout.
            byte[] compressed = { 0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01 };

            byte[] decompressed = new byte[1];
            int decompressedLength = 0;

            Assert.IsTrue( Compression.Uncompress( ref decompressed, ref decompressedLength, compressed,
                compressed.Length ) );

            Assert.AreEqual( 1, decompressedLength );
            Assert.AreEqual( 0, decompressed[0] );
        }

        [TestMethod]
        public void WontUncompressGarbage()
        {
            byte[] garbage = { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11, 0x22, 0x33 };

            byte[] decompressed = new byte[64];
            int decompressedLength = 0;

            Assert.IsFalse( Compression.Uncompress( ref decompressed, ref decompressedLength, garbage,
                garbage.Length ) );
        }
    }
}
