using System;
using System.Collections.Generic;
using System.IO;

namespace ClassicAssist.Updater.Services;

/// <summary>
///     The pre-flight that stops a half-applied update.
///     <para>
///         Every file about to be overwritten is probed for write access before a single one is copied.
///         On Windows a file mapped into a running process cannot be opened for writing, so a client
///         still holding ClassicAssist.dll fails the probe and the whole update is refused with the
///         list of offending files - rather than copying half the package and leaving an install made
///         of two different versions.
///     </para>
///     <para>
///         What the probe catches differs by platform, so it is not the whole story on its own.
///         Windows locks a file that any process has open or mapped, so a client holding a loaded
///         assembly is caught. Unix has no mandatory locking, but .NET emulates FileShare with
///         advisory locks, so another .NET process holding a FileStream is caught there too; a merely
///         mapped assembly is not. What covers that case is <see cref="RunningClients" />, which
///         closes the clients before the copy starts. Everywhere, the probe also catches the dull
///         reasons a write would fail: permissions, and read-only mounts.
///     </para>
/// </summary>
internal static class InstallGuard
{
    /// <summary>
    ///     Adds to <paramref name="failList" /> every file under <paramref name="destination" /> that
    ///     <paramref name="source" /> would overwrite but cannot be written to.
    /// </summary>
    public static void VerifyWriteAccess( DirectoryInfo source, DirectoryInfo destination,
        List<string> failList, string basePath = null )
    {
        if ( string.IsNullOrEmpty( basePath ) )
        {
            basePath = source.FullName;
        }

        if ( !Directory.Exists( destination.FullName ) )
        {
            return;
        }

        foreach ( FileInfo fileInfo in source.GetFiles() )
        {
            string relativePath =
                fileInfo.FullName.Replace( basePath + Path.DirectorySeparatorChar, string.Empty );

            string destinationPath = Path.Combine( destination.FullName, relativePath );

            if ( File.Exists( destinationPath ) && !CheckFileAccess( destinationPath ) )
            {
                failList.Add( destinationPath );
            }
        }

        foreach ( DirectoryInfo sourceDirectory in source.GetDirectories() )
        {
            // Mirrors the source tree onto the destination root, so nested files are checked against
            // the matching nested destination rather than against the top level.
            VerifyWriteAccess( sourceDirectory, destination, failList, basePath );
        }
    }

    public static bool CheckFileAccess( string path )
    {
        try
        {
            File.OpenWrite( path ).Dispose();

            return true;
        }
        catch ( Exception )
        {
            return false;
        }
    }
}
