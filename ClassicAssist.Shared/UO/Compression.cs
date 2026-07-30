using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using LibDeflate;

namespace ClassicAssist.UO
{
    public static class Compression
    {
        private const string LIBRARY = "libdeflate";

        private static readonly Compressor _compressor;
        private static readonly Decompressor _decompressor;

        // libdeflate's compressor and decompressor objects each own scratch state and must not be used
        // concurrently. Packet callbacks arrive on thread pool threads from the plugin, so send and
        // receive really can overlap here.
        private static readonly object _compressLock = new object();
        private static readonly object _decompressLock = new object();

        static Compression()
        {
            NativeLibrary.SetDllImportResolver( typeof( Compressor ).Assembly, Resolve );

            _compressor = new ZlibCompressor( 1 );
            _decompressor = new ZlibDecompressor();
        }

        /// <summary>
        ///     Finds libdeflate when the runtime's own probing cannot.
        ///     <para>
        ///         LibDeflate.Native publishes its macOS binary under the RID <c>osx.11.0</c>. Since .NET 8
        ///         the default RID graph is portable, so version-specific RIDs like that are never matched
        ///         from <c>osx-arm64</c> or <c>osx-x64</c> (the SDK says as much via NETSDK1206). The file is
        ///         copied into the output, just into a directory nothing looks in, and the first call fails
        ///         with DllNotFoundException. Probing the RID directories directly sidesteps the graph.
        ///     </para>
        /// </summary>
        private static IntPtr Resolve( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
        {
            if ( libraryName != LIBRARY )
            {
                return IntPtr.Zero;
            }

            foreach ( string rid in CandidateRuntimeIdentifiers() )
            {
                string path = Path.Combine( AppContext.BaseDirectory, "runtimes", rid, "native", NativeFileName() );

                if ( File.Exists( path ) && NativeLibrary.TryLoad( path, out IntPtr handle ) )
                {
                    return handle;
                }
            }

            // Nothing found; let the runtime fall back to its normal probing.
            return IntPtr.Zero;
        }

        private static string NativeFileName()
        {
            if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
            {
                return LIBRARY + ".dll";
            }

            return RuntimeInformation.IsOSPlatform( OSPlatform.OSX ) ? LIBRARY + ".dylib" : LIBRARY + ".so";
        }

        /// <summary>
        ///     The portable RID for this machine first, then any version-specific directory for the same OS
        ///     that carries no architecture suffix - those are either universal or single-architecture
        ///     builds, so they are safe to try. Directories pinned to a different architecture are skipped.
        /// </summary>
        private static IEnumerable<string> CandidateRuntimeIdentifiers()
        {
            string os = RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) ? "win" :
                RuntimeInformation.IsOSPlatform( OSPlatform.OSX ) ? "osx" : "linux";

            string architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

            yield return $"{os}-{architecture}";

            string runtimes = Path.Combine( AppContext.BaseDirectory, "runtimes" );

            if ( !Directory.Exists( runtimes ) )
            {
                yield break;
            }

            foreach ( string directory in Directory.GetDirectories( runtimes ) )
            {
                string name = Path.GetFileName( directory );

                if ( !name.StartsWith( os + ".", StringComparison.OrdinalIgnoreCase ) )
                {
                    continue;
                }

                int dash = name.IndexOf( '-' );

                if ( dash < 0 || name.Substring( dash + 1 ).Equals( architecture, StringComparison.OrdinalIgnoreCase ) )
                {
                    yield return name;
                }
            }
        }

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
