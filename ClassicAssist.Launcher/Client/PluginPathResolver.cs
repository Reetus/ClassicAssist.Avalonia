using System;
using System.IO;

namespace ClassicAssist.Launcher.Client;

/// <summary>
///     Maps a detected <see cref="ClientRuntimeFormat" /> onto one of the three ClassicAssist
///     plugin builds that <c>ClassicAssist.Plugin.csproj</c> produces, all of which land as
///     siblings of this launcher under Output/ClassicAssist/ - see the solution README's
///     "Which file to point the client at" table.
/// </summary>
public static class PluginPathResolver
{
    /// <param name="format">The client's detected runtime format.</param>
    /// <param name="forceManaged">
    ///     For a NativeAOT client, skip the DNNE shim and use the managed net10.0 build even when the
    ///     shim exists. Has no effect on the other formats, which already load a managed build.
    /// </param>
    public static string Resolve( ClientRuntimeFormat format, bool forceManaged = false )
    {
        return Resolve( format, AppContext.BaseDirectory, forceManaged );
    }

    internal static string Resolve( ClientRuntimeFormat format, string baseDirectory, bool forceManaged = false )
    {
        return format switch
        {
            ClientRuntimeFormat.Framework => FrameworkPath( baseDirectory ),
            ClientRuntimeFormat.Managed => ManagedPath( baseDirectory ),
            ClientRuntimeFormat.NativeAot => forceManaged ? ManagedPath( baseDirectory ) : ResolveNativeAot( baseDirectory ),
            _ => throw new InvalidOperationException( "Could not determine the client's runtime format." )
        };
    }

    /// <summary>
    ///     The DNNE shim when there is one, and the net472 build when there is not.
    ///     <para>
    ///         A NativeAOT client hosts no CLR of its own, so the shim - which starts one through
    ///         hostfxr - is the direct route. It is not the only one: such a client loads plugins
    ///         through ClassicUO.Bootstrap, which targets net472 and loads them with
    ///         Assembly.LoadFile plus a reflected call to Assistant.Engine.Install. That is an
    ///         ordinary managed load, and framework/ClassicAssist.dll is the build it can take.
    ///     </para>
    ///     <para>
    ///         Which matters because the shim needs a C toolchain to build and is simply absent
    ///         otherwise - on Windows most of all, where ClassicAssist.Plugin.csproj probes for
    ///         clang at a Unix path and so never enables DNNE at all. Without this fallback the
    ///         launcher pointed those builds at a file that was never produced.
    ///     </para>
    /// </summary>
    private static string ResolveNativeAot( string baseDirectory )
    {
        string shim = Path.Combine( baseDirectory, "ClassicAssistNE" + NativeShimExtension() );

        return File.Exists( shim ) ? shim : FrameworkPath( baseDirectory );
    }

    private static string ManagedPath( string baseDirectory )
    {
        return Path.Combine( baseDirectory, "ClassicAssist.dll" );
    }

    private static string FrameworkPath( string baseDirectory )
    {
        return Path.Combine( baseDirectory, "framework", "ClassicAssist.dll" );
    }

    // The host OS running the Launcher, not the detected client format - you cannot run a Linux
    // ELF client on Windows in the first place, so these are always the same platform.
    private static string NativeShimExtension()
    {
        if ( OperatingSystem.IsWindows() )
        {
            return ".dll";
        }

        return OperatingSystem.IsMacOS() ? ".dylib" : ".so";
    }
}
