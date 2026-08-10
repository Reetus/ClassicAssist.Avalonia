using System;
using System.IO;
using ClassicAssist.Launcher.ViewModels;
using Newtonsoft.Json;

namespace ClassicAssist.Updater.Models;

public class UpdaterSettings : BaseViewModel
{
    private const string SETTINGS_FILE = "updater.settings.json";

    /// <summary>
    ///     Where releases are listed. Kept in the settings file rather than hard coded so a fork or a
    ///     private build can be repointed without a rebuild; see <see cref="Services.ReleaseSource" />
    ///     for the shape it expects.
    /// </summary>
    public string ReleasesURL
    {
        get;
        set => SetProperty( ref field, value );
    } = DEFAULT_RELEASES_URL;

    public const string DEFAULT_RELEASES_URL =
        "https://api.github.com/repos/Reetus/ClassicAssist.Avalonia/releases";

    public bool InstallPrereleases
    {
        get;
        set => SetProperty( ref field, value );
    }

    public static UpdaterSettings Load( string path )
    {
        string settingsFile = Path.Combine( path ?? string.Empty, SETTINGS_FILE );

        if ( !File.Exists( settingsFile ) )
        {
            return new UpdaterSettings();
        }

        try
        {
            using StreamReader streamReader = new( settingsFile );
            using JsonTextReader reader = new( streamReader );

            UpdaterSettings updaterSettings = new JsonSerializer().Deserialize<UpdaterSettings>( reader );

            if ( updaterSettings == null )
            {
                return new UpdaterSettings();
            }

            // An older file, or one hand-edited to blank, would otherwise leave the updater with
            // nowhere to look and no way back short of deleting the file.
            if ( string.IsNullOrWhiteSpace( updaterSettings.ReleasesURL ) )
            {
                updaterSettings.ReleasesURL = DEFAULT_RELEASES_URL;
            }

            return updaterSettings;
        }
        catch ( Exception )
        {
            // A corrupt settings file must not stop the updater from running - defaults get written
            // back over it on exit.
            return new UpdaterSettings();
        }
    }

    public static void Save( UpdaterSettings updaterSettings, string path )
    {
        try
        {
            string settingsFile = Path.Combine( path ?? string.Empty, SETTINGS_FILE );

            using StreamWriter streamWriter = new( settingsFile );
            using JsonTextWriter writer = new( streamWriter ) { Formatting = Formatting.Indented };

            new JsonSerializer().Serialize( writer, updaterSettings );
        }
        catch ( Exception )
        {
            // Read-only install directory; not worth failing the update over.
        }
    }
}
