using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace ClassicAssist.Updater.Services;

/// <summary>
///     Picks the release asset built for the running platform.
///     <para>
///         The WPF updater had no such notion - it was Windows only, so a release carried one zip and
///         the updater took it. Here a release is expected to carry one package per platform, named
///         with its runtime identifier:
///     </para>
///     <code>
///         ClassicAssist-0.5.1234.0-win-x64.zip
///         ClassicAssist-0.5.1234.0-linux-x64.zip
///         ClassicAssist-0.5.1234.0-osx-arm64.zip
///     </code>
///     <para>
///         Matching is by token rather than by exact file name, so the surrounding convention can
///         change without breaking older updaters. A release that carries exactly one zip and nothing
///         platform-specific is taken as-is, which is what makes a single-package repository work
///         before per-platform builds exist.
///     </para>
/// </summary>
internal static class PlatformPackage
{
    /// <summary>Runtime identifier of the running platform, e.g. "linux-x64".</summary>
    public static string RuntimeIdentifier => $"{OSToken()}-{ArchitectureToken()}";

    private static string OSToken()
    {
        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            return "win";
        }

        if ( RuntimeInformation.IsOSPlatform( OSPlatform.OSX ) )
        {
            return "osx";
        }

        return "linux";
    }

    private static string ArchitectureToken()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
        };
    }

    /// <summary>
    ///     Spellings of the running OS that a package might reasonably use, most specific first. The
    ///     architecture-qualified rid wins over a bare os name so that osx-arm64 is never handed an
    ///     osx-x64 build when both are published.
    /// </summary>
    private static IEnumerable<string> Candidates()
    {
        yield return RuntimeIdentifier;

        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            yield return "windows";
            yield return "win";
        }
        else if ( RuntimeInformation.IsOSPlatform( OSPlatform.OSX ) )
        {
            yield return "macos";
            yield return "osx";
            yield return "mac";
        }
        else
        {
            yield return "linux";
        }
    }

    /// <summary>
    ///     Chooses the asset for this platform, or null when the release carries nothing usable.
    /// </summary>
    /// <param name="assetNames">Asset file names, in the order the release lists them.</param>
    public static string Select( IReadOnlyCollection<string> assetNames )
    {
        if ( assetNames == null || assetNames.Count == 0 )
        {
            return null;
        }

        string[] archives = [.. assetNames.Where( IsArchive )];

        if ( archives.Length == 0 )
        {
            return null;
        }

        foreach ( string candidate in Candidates() )
        {
            string match = archives.FirstOrDefault( name => ContainsToken( name, candidate ) );

            if ( match != null )
            {
                return match;
            }
        }

        // Nothing names a platform. One archive is unambiguous, so take it; several are not, and
        // guessing would install a Windows build on Linux.
        return archives.Length == 1 ? archives[0] : null;
    }

    private static bool IsArchive( string name )
    {
        return name != null && ( name.EndsWith( ".zip", StringComparison.OrdinalIgnoreCase ) ||
                                 name.EndsWith( ".tar.gz", StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    ///     Token match rather than plain Contains: "win" must not match "darwin", and "linux" must
    ///     not match a name that merely mentions it mid-word. Tokens are delimited by the separators
    ///     package names actually use.
    /// </summary>
    private static bool ContainsToken( string name, string token )
    {
        int index = name.IndexOf( token, StringComparison.OrdinalIgnoreCase );

        while ( index >= 0 )
        {
            bool startOk = index == 0 || IsSeparator( name[index - 1] );
            int after = index + token.Length;
            bool endOk = after >= name.Length || IsSeparator( name[after] );

            if ( startOk && endOk )
            {
                return true;
            }

            index = name.IndexOf( token, index + 1, StringComparison.OrdinalIgnoreCase );
        }

        return false;
    }

    private static bool IsSeparator( char c )
    {
        return c is '-' or '_' or '.' or '+';
    }
}
