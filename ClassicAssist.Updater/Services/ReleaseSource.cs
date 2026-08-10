using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ClassicAssist.Updater.Models;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Updater.Services;

/// <summary>
///     Reads the release list.
///     <para>
///         The WPF build fetched a hand-published manifest - a JSON array of ChangelogEntry with the
///         download url already in it. This reads GitHub's releases API directly instead, so cutting a
///         release is the only step: there is no second artifact to remember to publish, and no window
///         where the manifest and the releases disagree. A manifest in the old shape is still accepted,
///         so a fork can point <see cref="UpdaterSettings.ReleasesURL" /> at a static file.
///     </para>
/// </summary>
internal class ReleaseSource
{
    private readonly string _url;

    public ReleaseSource( string url )
    {
        _url = string.IsNullOrWhiteSpace( url ) ? UpdaterSettings.DEFAULT_RELEASES_URL : url;
    }

    /// <summary>
    ///     Releases, newest first, each already resolved to this platform's package. Releases that
    ///     publish nothing for this platform are dropped rather than offered and then failed on.
    /// </summary>
    public async Task<IEnumerable<ChangelogEntry>> GetReleases()
    {
        using HttpClient httpClient = new() { Timeout = TimeSpan.FromMinutes( 1 ) };

        // GitHub rejects requests with no user agent outright.
        httpClient.DefaultRequestHeaders.Add( "User-Agent", "ClassicAssist Updater" );
        httpClient.DefaultRequestHeaders.Add( "Accept", "application/vnd.github+json" );

        HttpResponseMessage response = await httpClient.GetAsync( _url );

        if ( response.StatusCode != HttpStatusCode.OK )
        {
            throw new InvalidOperationException( DescribeFailure( response ) );
        }

        string json = await response.Content.ReadAsStringAsync();

        return Parse( json );
    }

    public async Task<ChangelogEntry> GetLatestRelease( bool prereleases )
    {
        IEnumerable<ChangelogEntry> releases = await GetReleases();

        return releases?.FirstOrDefault( e => prereleases || !e.Prerelease );
    }

    /// <summary>
    ///     A 404 on a repository with no releases yet is the expected state before the first one is
    ///     cut, and a rate limit is the other thing a user will actually hit; both deserve better than
    ///     the raw status code.
    /// </summary>
    private string DescribeFailure( HttpResponseMessage response )
    {
        if ( response.StatusCode == HttpStatusCode.NotFound )
        {
            return $"No releases found at {_url}";
        }

        if ( response.StatusCode == HttpStatusCode.Forbidden &&
             response.Headers.TryGetValues( "X-RateLimit-Remaining", out IEnumerable<string> remaining ) &&
             remaining.FirstOrDefault() == "0" )
        {
            return "GitHub rate limit reached, try again later";
        }

        return $"{(int) response.StatusCode} {response.ReasonPhrase} from {_url}";
    }

    /// <summary>
    ///     Accepts either GitHub's release json or the WPF build's flat manifest. They are told apart
    ///     by the fields present rather than by a mode switch, so the same url setting covers both.
    /// </summary>
    internal static IEnumerable<ChangelogEntry> Parse( string json )
    {
        JArray array = JArray.Parse( json );
        List<ChangelogEntry> entries = [];

        foreach ( JToken token in array )
        {
            if ( token is not JObject release )
            {
                continue;
            }

            ChangelogEntry entry = release["assets"] is JArray assets
                ? FromGitHubRelease( release, assets )
                : FromManifest( release );

            if ( entry != null )
            {
                entries.Add( entry );
            }
        }

        return entries;
    }

    private static ChangelogEntry FromGitHubRelease( JObject release, JArray assets )
    {
        if ( release.Value<bool?>( "draft" ) == true )
        {
            return null;
        }

        Dictionary<string, JObject> byName = [];

        foreach ( JToken token in assets )
        {
            if ( token is JObject asset && asset.Value<string>( "name" ) is { } name )
            {
                byName[name] = asset;
            }
        }

        string selected = PlatformPackage.Select( [.. byName.Keys] );

        if ( selected == null )
        {
            return null;
        }

        JObject selectedAsset = byName[selected];

        return new ChangelogEntry
        {
            Version = release.Value<string>( "tag_name" ) ?? release.Value<string>( "name" ),
            Description = release.Value<string>( "body" ),
            CreatedAt = ReadDate( release, "published_at" ) ?? ReadDate( release, "created_at" ) ?? default,
            Prerelease = release.Value<bool?>( "prerelease" ) ?? false,
            PackageName = selected,
            DownloadURL = selectedAsset.Value<string>( "browser_download_url" ),
            DownloadSize = selectedAsset.Value<long?>( "size" ) ?? 0
        };
    }

    /// <summary>
    ///     Json.NET turns an ISO timestamp into a DateTime while parsing, and DateTime does not cast
    ///     to DateTimeOffset through Value&lt;T&gt;. Read it back out as text and parse it here.
    /// </summary>
    private static DateTimeOffset? ReadDate( JObject release, string name )
    {
        string text = release.Value<string>( name );

        return DateTimeOffset.TryParse( text, out DateTimeOffset value ) ? value : null;
    }

    private static ChangelogEntry FromManifest( JObject release )
    {
        string url = release.Value<string>( "DownloadURL" );

        if ( string.IsNullOrEmpty( url ) )
        {
            return null;
        }

        return new ChangelogEntry
        {
            Version = release.Value<string>( "Version" ),
            Description = release.Value<string>( "Description" ),
            CreatedAt = ReadDate( release, "CreatedAt" ) ?? default,
            Prerelease = release.Value<bool?>( "Prerelease" ) ?? false,
            PackageName = url[( url.LastIndexOf( '/' ) + 1 )..],
            DownloadURL = url,
            DownloadSize = release.Value<long?>( "DownloadSize" ) ?? 0
        };
    }
}
