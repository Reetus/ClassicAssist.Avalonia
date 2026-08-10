using System;

namespace ClassicAssist.Updater.Models;

/// <summary>
///     One release, flattened down to the fields the updater acts on. Same shape as the WPF build's
///     ChangelogEntry, with <see cref="DownloadURL" /> and <see cref="DownloadSize" /> already resolved
///     to the asset for the running platform - see <see cref="Services.PlatformPackage" />.
/// </summary>
public class ChangelogEntry
{
    public DateTimeOffset CreatedAt { get; set; }
    public string Description { get; set; }

    /// <summary>Bytes, as reported by the release. Zero when the source does not say.</summary>
    public long DownloadSize { get; set; }

    public string DownloadURL { get; set; }

    /// <summary>Asset file name, for the log line naming what is about to be fetched.</summary>
    public string PackageName { get; set; }

    public bool Prerelease { get; set; }
    public string Version { get; set; }
}
