using System;
using System.Buffers;
using LibDeflate;

namespace ClassicAssist.UO
{
    public static class Compression
    {
        private static readonly Compressor _compressor = new ZlibCompressor( 1 );
        private static readonly Decompressor _decompressor = new ZlibDecompressor();

        // libdeflate's compressor and decompressor objects each own scratch state and must not be used
        // concurrently. Packet callbacks arrive on thread pool threads from the plugin, so send and
        // receive really can overlap here.
        private static readonly object _compressLock = new object();
        private static readonly object _decompressLock = new object();

        public static int Compress( byte[] sourceBuffer, ref byte[] destBuffer )
        {
            lock ( _compressLock )
            {
                return _compressor.Compress( sourceBuffer, destBuffer );
            }
        }

        public static bool Uncompress( ref byte[] destBuffer, ref int destLength, byte[] sourceBuffer, int sourceLen )
        {
            lock ( _decompressLock )
            {
                OperationStatus status = _decompressor.Decompress( sourceBuffer.AsSpan( 0, sourceLen ),
                    destBuffer.AsSpan(), out destLength );

                return status == OperationStatus.Done;
            }
        }
    }
}
