using System;
using System.Diagnostics;
using System.IO;

namespace ClassicAssist.Updater.Models;

/// <summary>
///     Reads the version of an installed ClassicAssist, for when the caller did not pass --version.
/// </summary>
internal static class InstallVersion
{
    /// <summary>
    ///     Files worth reading a version off, in order. The install root holds the launcher, the
    ///     updater and the plugin; the assistant itself is one level down in ui/, which is why the
    ///     root alone is not enough.
    /// </summary>
    internal static string[] Candidates( string installPath )
    {
        return
        [
            Path.Combine( installPath, "ui", "ClassicAssist.Shared.dll" ),
            Path.Combine( installPath, "ClassicAssist.Shared.dll" ),
            Path.Combine( installPath, "ClassicAssist.dll" )
        ];
    }

    /// <summary>
    ///     The installed version, or null when nothing readable is present - a broken or absent
    ///     install, which the caller treats as grounds to update rather than to give up.
    /// </summary>
    public static string Resolve( string installPath )
    {
        if ( string.IsNullOrEmpty( installPath ) )
        {
            return null;
        }

        foreach ( string candidate in Candidates( installPath ) )
        {
            if ( !File.Exists( candidate ) )
            {
                continue;
            }

            try
            {
                string version = FileVersionInfo.GetVersionInfo( candidate ).ProductVersion;

                if ( !string.IsNullOrEmpty( version ) )
                {
                    return version;
                }
            }
            catch ( Exception )
            {
                // Not a managed assembly, or unreadable; try the next candidate.
            }
        }

        return null;
    }
}
