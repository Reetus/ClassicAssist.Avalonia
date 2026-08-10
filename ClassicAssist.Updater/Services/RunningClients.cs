using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ClassicAssist.Updater.Services;

/// <summary>
///     Finds processes that have the install loaded, so they can be closed before anything is
///     overwritten.
///     <para>
///         The WPF build walked <see cref="Process.Modules" /> looking for the install's
///         ClassicAssist.dll. That only works on Windows: on Linux the property throws for any process
///         but the caller's, and on macOS there is no way to enumerate another process's modules
///         without entitlements. Each platform therefore gets the best answer it can give - loaded
///         modules on Windows, mapped files on Linux, and executable path on macOS.
///     </para>
/// </summary>
internal static class RunningClients
{
    /// <summary>
    ///     Processes with something from <paramref name="installPath" /> loaded, never including this
    ///     process.
    /// </summary>
    public static Process[] Find( string installPath )
    {
        if ( string.IsNullOrEmpty( installPath ) || !Directory.Exists( installPath ) )
        {
            return [];
        }

        string fullPath = Path.GetFullPath( installPath );
        int self = Environment.ProcessId;

        List<Process> result = [];

        foreach ( Process process in Process.GetProcesses() )
        {
            try
            {
                if ( process.Id == self )
                {
                    continue;
                }

                if ( HasInstallLoaded( process, fullPath ) )
                {
                    result.Add( process );
                }
            }
            catch ( Exception )
            {
                // Access denied on a process this user does not own, or one that exited between the
                // enumeration and the check. Neither is ours to update.
            }
        }

        return [.. result];
    }

    private static bool HasInstallLoaded( Process process, string installPath )
    {
        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            return process.Modules.Cast<ProcessModule>()
                .Any( module => IsUnder( module.FileName, installPath ) );
        }

        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Linux ) )
        {
            return HasMappedFileUnder( process.Id, installPath );
        }

        // macOS: MainModule is the executable itself, which catches the launcher and the UI app
        // running out of the install but not a client that merely loaded the plugin.
        return IsUnder( process.MainModule?.FileName, installPath );
    }

    /// <summary>
    ///     Reads /proc/&lt;pid&gt;/maps, which lists every file the process has mapped - including the
    ///     managed assemblies of a client that loaded the plugin, which is exactly what
    ///     Process.Modules would have reported on Windows.
    /// </summary>
    private static bool HasMappedFileUnder( int pid, string installPath )
    {
        string maps = $"/proc/{pid}/maps";

        if ( !File.Exists( maps ) )
        {
            return false;
        }

        try
        {
            foreach ( string line in File.ReadLines( maps ) )
            {
                // Path is the last column and is the only one that can contain a slash.
                int pathStart = line.IndexOf( '/' );

                if ( pathStart < 0 )
                {
                    continue;
                }

                if ( IsUnder( line[pathStart..], installPath ) )
                {
                    return true;
                }
            }
        }
        catch ( Exception )
        {
            // Short-lived process, or one this user cannot read.
        }

        return false;
    }

    private static bool IsUnder( string path, string installPath )
    {
        if ( string.IsNullOrEmpty( path ) )
        {
            return false;
        }

        StringComparison comparison = RuntimeInformation.IsOSPlatform( OSPlatform.Linux )
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return path.StartsWith( installPath + Path.DirectorySeparatorChar, comparison );
    }

    /// <summary>
    ///     Something to show in the "these will be closed" list. MainWindowTitle is empty for a
    ///     process with no window, which on Linux is most of them.
    /// </summary>
    public static string Describe( Process process )
    {
        try
        {
            string title = process.MainWindowTitle;

            return string.IsNullOrWhiteSpace( title )
                ? $"{process.ProcessName} ({process.Id})"
                : $"{title} ({process.Id})";
        }
        catch ( Exception )
        {
            return process.Id.ToString();
        }
    }
}
