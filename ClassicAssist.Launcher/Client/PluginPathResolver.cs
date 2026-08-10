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
    public static string Resolve( ClientRuntimeFormat format )
    {
        string baseDirectory = AppContext.BaseDirectory;

        return format switch
        {
            ClientRuntimeFormat.Framework => Path.Combine( baseDirectory, "framework", "ClassicAssist.dll" ),
            ClientRuntimeFormat.Managed => Path.Combine( baseDirectory, "ClassicAssist.dll" ),
            ClientRuntimeFormat.NativeAot => Path.Combine( baseDirectory, "ClassicAssistNE" + NativeShimExtension() ),
            _ => throw new InvalidOperationException( "Could not determine the client's runtime format." )
        };
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
