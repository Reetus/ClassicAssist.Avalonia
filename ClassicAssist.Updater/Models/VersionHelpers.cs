using System;

namespace ClassicAssist.Updater.Models;

/// <summary>
///     The WPF build compares versions with the Semver package. Not carried over: this tree stamps its
///     assemblies <c>0.5.&lt;builddate&gt;.0</c>, a plain four-part System.Version that semver refuses
///     outright, while GitHub tags in the old repo look like <c>0.4.424-prerelease+1</c>. Both shapes
///     are handled here, and neither needs a package.
/// </summary>
public static class VersionHelpers
{
    /// <summary>
    ///     True when <paramref name="newVersion" /> should be installed over
    ///     <paramref name="currentVersion" />. Anything unparseable returns true, matching WPF: a build
    ///     with no readable version is assumed stale rather than silently left alone.
    /// </summary>
    public static bool IsVersionNewer( string currentVersion, string newVersion )
    {
        try
        {
            ( Version currentNumber, string currentPrerelease ) = Parse( currentVersion );
            ( Version newNumber, string newPrerelease ) = Parse( newVersion );

            // A develop build is ahead of whatever has been released; updating would move it
            // backwards. Same carve-out as WPF.
            if ( currentPrerelease.Equals( "develop", StringComparison.OrdinalIgnoreCase ) )
            {
                return false;
            }

            int numbers = currentNumber.CompareTo( newNumber );

            if ( numbers != 0 )
            {
                return numbers < 0;
            }

            // Same numbers: a release beats a prerelease, and build metadata (+1) never counts,
            // so 0.4.424 is not superseded by 0.4.424-prerelease.
            bool currentIsPrerelease = currentPrerelease.Length > 0;
            bool newIsPrerelease = newPrerelease.Length > 0;

            if ( currentIsPrerelease != newIsPrerelease )
            {
                return currentIsPrerelease;
            }

            return string.CompareOrdinal( currentPrerelease, newPrerelease ) < 0;
        }
        catch ( Exception )
        {
            return true;
        }
    }

    /// <summary>
    ///     Splits "1.2.3-prerelease+build" into its numeric part and its prerelease tag, discarding
    ///     build metadata. Leading "v", as GitHub tags are usually written, is tolerated.
    /// </summary>
    internal static (Version Number, string Prerelease) Parse( string version )
    {
        if ( string.IsNullOrWhiteSpace( version ) )
        {
            throw new FormatException( "Empty version." );
        }

        string text = version.Trim();

        if ( text.StartsWith( "v", StringComparison.OrdinalIgnoreCase ) )
        {
            text = text[1..];
        }

        int build = text.IndexOf( '+' );

        if ( build >= 0 )
        {
            text = text[..build];
        }

        string prerelease = string.Empty;
        int dash = text.IndexOf( '-' );

        if ( dash >= 0 )
        {
            prerelease = text[( dash + 1 )..];
            text = text[..dash];
        }

        if ( !Version.TryParse( text, out Version number ) )
        {
            throw new FormatException( $"Unrecognised version '{version}'." );
        }

        // Version.CompareTo treats an unspecified component as -1, so 0.4.424 would sort below
        // 0.4.424.0. Normalise both to four components so the two spellings compare equal.
        number = new Version( number.Major, Math.Max( number.Minor, 0 ), Math.Max( number.Build, 0 ),
            Math.Max( number.Revision, 0 ) );

        return ( number, prerelease );
    }
}
