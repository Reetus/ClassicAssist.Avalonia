using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ClassicAssist.UO
{
    public static class Compression
    {
        /// <summary>
        ///     Logical name resolved to a real zlib by <see cref="Resolve" />. The platform is decided at run
        ///     time rather than by a build configuration, because the previous <c>#if LINUX</c> approach only
        ///     worked in the "Linux" configuration and left Debug builds P/Invoking zlib64.dll on Linux.
        /// </summary>
        private const string ZLIB = "zlib";

        static Compression()
        {
            NativeLibrary.SetDllImportResolver( typeof( Compression ).Assembly, Resolve );
        }

        [DllImport( ZLIB, EntryPoint = "uncompress" )]
        private static extern int UncompressNative( byte[] dest, ref int destLen, byte[] source, int sourceLen );

        [DllImport( ZLIB, EntryPoint = "compress" )]
        private static extern int CompressNative( byte[] dest, ref int destLen, byte[] source, int sourceLen );

        private static IntPtr Resolve( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
        {
            if ( libraryName != ZLIB )
            {
                return IntPtr.Zero;
            }

            string[] candidates;

            if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
            {
                candidates = Environment.Is64BitProcess
                    ? new[] { "zlib64.dll", "zlib1.dll" }
                    : new[] { "zlib32.dll", "zlib1.dll" };
            }
            else if ( RuntimeInformation.IsOSPlatform( OSPlatform.OSX ) )
            {
                candidates = new[] { "libz.dylib", "libz.1.dylib" };
            }
            else
            {
                candidates = new[] { "libz.so.1", "libz.so" };
            }

            foreach ( string candidate in candidates )
            {
                if ( NativeLibrary.TryLoad( candidate, assembly, searchPath, out IntPtr handle ) )
                {
                    return handle;
                }
            }

            return IntPtr.Zero;
        }

        public static bool Uncompress( ref byte[] destBuffer, ref int destLength, byte[] sourceBuffer, int sourceLen )
        {
            return UncompressNative( destBuffer, ref destLength, sourceBuffer, sourceLen ) == 0;
        }

        public static byte[] Compress( byte[] bytes )
        {
            byte[] compressBytes = new byte[(int) ( bytes.Length * 1.001 ) + 12];

            int length = compressBytes.Length;

            CompressNative( compressBytes, ref length, bytes, bytes.Length );

            Array.Resize( ref compressBytes, length );

            return compressBytes;
        }
    }
}
