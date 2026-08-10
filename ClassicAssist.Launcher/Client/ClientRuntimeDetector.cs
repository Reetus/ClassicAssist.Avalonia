using System;
using System.IO;
using System.Reflection.PortableExecutable;

namespace ClassicAssist.Launcher.Client;

public enum ClientRuntimeFormat
{
    Unknown,
    Framework,
    Managed,
    NativeAot
}

/// <summary>
///     Works out which of the three ClassicAssist plugin builds (see
///     <see cref="PluginPathResolver" />) a client executable can load, by inspecting the file
///     itself rather than trusting a file extension or user choice.
///     <para>
///         There is no single flag for this - it takes two signals read together:
///     </para>
///     <list type="bullet">
///         <item>
///             Whether the file has a CLI/COR20 header. Every managed assembly has one, including
///             legacy .NET Framework/Mono executables - but NOT a modern .NET apphost, which is a
///             thin native stub with the actual managed payload in a separate DLL.
///         </item>
///         <item>
///             Whether a sibling "&lt;name&gt;.runtimeconfig.json" exists next to the executable.
///             Only hostfxr-based apphosts (framework-dependent or self-contained CoreCLR) write
///             one; NativeAOT publishes a fully native binary with no CLR to host, so it never has
///             one.
///         </item>
///     </list>
///     A CLI header present means Framework/Mono - it's a native PE with no CLR to host at all. No
///     header plus a runtimeconfig.json sibling means a modern managed apphost. No header and no
///     sibling means NativeAOT.
///     <para>
///         Caveat: this assumes the input is the client's own launchable executable, not a bare
///         managed DLL - every managed assembly (including a modern one) has a CLI header, so a
///         bare DLL would be misclassified as Framework. Modern .NET never fuses the apphost and
///         the managed payload into one COR20 file, so this only matters if the wrong kind of file
///         is picked.
///     </para>
/// </summary>
public static class ClientRuntimeDetector
{
    public static ClientRuntimeFormat Detect( string exePath )
    {
        if ( string.IsNullOrEmpty( exePath ) || !File.Exists( exePath ) )
        {
            return ClientRuntimeFormat.Unknown;
        }

        byte[] header = ReadHeader( exePath, 4 );

        if ( header.Length < 4 )
        {
            return ClientRuntimeFormat.Unknown;
        }

        if ( header[0] == 0x4D && header[1] == 0x5A ) // "MZ"
        {
            return DetectPe( exePath );
        }

        if ( IsElfOrMachO( header ) )
        {
            return HasSiblingRuntimeConfig( exePath ) ? ClientRuntimeFormat.Managed : ClientRuntimeFormat.NativeAot;
        }

        return ClientRuntimeFormat.Unknown;
    }

    private static ClientRuntimeFormat DetectPe( string exePath )
    {
        try
        {
            using FileStream stream = File.OpenRead( exePath );
            using PEReader peReader = new( stream );

            if ( peReader.HasMetadata && peReader.PEHeaders.CorHeader != null )
            {
                return ClientRuntimeFormat.Framework;
            }
        }
        catch ( BadImageFormatException )
        {
            return ClientRuntimeFormat.Unknown;
        }

        // Native PE stub: either a modern apphost (has a runtimeconfig.json) or a NativeAOT exe.
        return HasSiblingRuntimeConfig( exePath ) ? ClientRuntimeFormat.Managed : ClientRuntimeFormat.NativeAot;
    }

    private static bool HasSiblingRuntimeConfig( string exePath )
    {
        string directory = Path.GetDirectoryName( exePath );
        string baseName = Path.GetFileNameWithoutExtension( exePath );

        if ( string.IsNullOrEmpty( directory ) )
        {
            return false;
        }

        return File.Exists( Path.Combine( directory, $"{baseName}.runtimeconfig.json" ) );
    }

    private static bool IsElfOrMachO( byte[] header )
    {
        uint magic = BitConverter.ToUInt32( header, 0 );

        // ELF: 0x7F 'E' 'L' 'F', read little-endian.
        if ( magic == 0x464C457Fu )
        {
            return true;
        }

        // Mach-O 32/64-bit and FAT/universal binaries, both byte orders.
        return magic is 0xFEEDFACFu or 0xCFFAEDFEu or 0xFEEDFACEu or 0xCEFAEDFEu or 0xCAFEBABEu or 0xBEBAFECAu;
    }

    private static byte[] ReadHeader( string path, int count )
    {
        try
        {
            using FileStream stream = File.OpenRead( path );
            byte[] buffer = new byte[count];
            int read = stream.Read( buffer, 0, count );

            return read == count ? buffer : [];
        }
        catch ( IOException )
        {
            return [];
        }
    }
}
