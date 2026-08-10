using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClassicAssist.Launcher.Client;
using ClassicAssist.Launcher.Models;
using ClassicAssist.Launcher.Views;
using ClassicAssist.Launcher.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Launcher.ViewModels;

public class MainViewModel : BaseViewModel
{
    private const string CONFIG_FILENAME = "Launcher.json";
    private const string SHARDS_HASH_URL = "https://raw.githubusercontent.com/Reetus/ClassicAssist-Shards/main/shards.hash.json";
    private const string SHARDS_URL = "https://raw.githubusercontent.com/Reetus/ClassicAssist-Shards/main/shards.json";

    public MainViewModel()
    {
        string fullPath = Path.Combine( AppContext.BaseDirectory, CONFIG_FILENAME );

        if ( !File.Exists( fullPath ) )
        {
            return;
        }

        using JsonTextReader jtr = new( new StreamReader( fullPath ) );
        JObject config = (JObject) JToken.ReadFrom( jtr );

        if ( config["ClientPaths"] != null )
        {
            foreach ( JToken token in config["ClientPaths"] )
            {
                string path = token.ToObject<string>();

                if ( File.Exists( path ) )
                {
                    ClientPaths.Add( path );
                }
            }
        }

        if ( config["SelectedClientPath"] != null )
        {
            string path = config["SelectedClientPath"].ToObject<string>();

            SelectedClientPath = File.Exists( path ) ? path : ClientPaths.FirstOrDefault();
        }

        if ( config["DataPaths"] != null )
        {
            foreach ( JToken token in config["DataPaths"] )
            {
                string path = token.ToObject<string>();

                if ( Directory.Exists( path ) )
                {
                    DataPaths.Add( path );
                }
            }
        }

        if ( config["SelectedDataPath"] != null )
        {
            string path = config["SelectedDataPath"].ToObject<string>();

            SelectedDataPath = Directory.Exists( path ) ? path : DataPaths.FirstOrDefault();
        }

        ShardManager.OverridePresets = config["OverridePresets"]?.ToObject<bool>() ?? false;

        if ( ShardManager.OverridePresets && config["Presets"] != null )
        {
            ShardManager.Shards.Clear();
        }

        ShardsHash = config["ShardsHash"]?.ToObject<string>() ?? string.Empty;
        ShardsDateTime = config["ShardsDateTime"]?.ToObject<DateTime?>();

        if ( config["Presets"] != null )
        {
            foreach ( JToken token in config["Presets"] )
            {
                ShardEntry shard = new()
                {
                    Name = token["Name"]?.ToObject<string>() ?? "Unknown",
                    Address = token["Address"]?.ToObject<string>() ?? "localhost",
                    Port = token["Port"]?.ToObject<int>() ?? 2593,
                    HasStatusProtocol = token["HasStatusProtocol"]?.ToObject<bool>() ?? true,
                    Encryption = token["Encryption"]?.ToObject<bool>() ?? false,
                    Website = token["Website"]?.ToObject<string>(),
                    IsPreset = true,
                    LastPlayed = token["LastPlayed"]?.ToObject<DateTime>() ?? default
                };

                ShardManager.Shards.AddSorted( shard, new ShardEntryComparer() );
            }
        }

        if ( config["Shards"] != null )
        {
            foreach ( JToken token in config["Shards"] )
            {
                ShardEntry shard = new()
                {
                    Name = token["Name"]?.ToObject<string>() ?? "Unknown",
                    Address = token["Address"]?.ToObject<string>() ?? "localhost",
                    Port = token["Port"]?.ToObject<int>() ?? 2593,
                    Website = token["Website"]?.ToObject<string>(),
                    HasStatusProtocol = token["HasStatusProtocol"]?.ToObject<bool>() ?? true,
                    Encryption = token["Encryption"]?.ToObject<bool>() ?? false,
                    LastPlayed = token["LastPlayed"]?.ToObject<DateTime>() ?? default
                };

                ShardManager.Shards.AddSorted( shard, new ShardEntryComparer() );
            }
        }

        if ( config["DeletedPresets"] != null )
        {
            foreach ( JToken token in config["DeletedPresets"] )
            {
                ShardEntry shard = new() { Name = token["Name"]?.ToObject<string>() ?? "Unknown", IsPreset = true };

                ShardEntry preset = ShardManager.Shards.FirstOrDefault( e => e.Equals( shard ) );

                preset?.Deleted = true;
            }
        }

        if ( config["SelectedShard"] != null )
        {
            ShardEntry match = ShardManager.Shards.FirstOrDefault( s => s.Name == config["SelectedShard"]?.ToObject<string>() );

            if ( match != null )
            {
                SelectedShard = match;
            }
        }

        if ( config["Plugins"] != null )
        {
            foreach ( JToken token in config["Plugins"] )
            {
                string pluginPath = token.ToObject<string>();
                Plugins.Add( new PluginEntry { Name = Path.GetFileName( pluginPath ), FullPath = pluginPath } );
            }
        }

        ReadClassicOptions( config );

        if ( !ShardsDateTime.HasValue || DateTime.Now - ShardsDateTime >= TimeSpan.FromHours( 24 ) )
        {
            _ = CheckPresets();
        }
    }

    /// <summary>Set once by MainWindow's code-behind after construction; used for dialog ownership.</summary>
    public Window OwnerWindow { get; set; }

    public ClassicOptions ClassicOptions { get; set; } = new ClassicOptions();

    public ObservableCollection<string> ClientPaths
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ICommand ClosingCommand => field ??= new RelayCommand( Closing, o => true );

    public ObservableCollection<string> DataPaths
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ICommand OptionsCommand => field ??= new RelayCommandAsync( ShowOptionsWindow, o => true );

    public List<PluginEntry> Plugins { get; set; } = [];

    /// <summary>Raised once the client process has been started successfully; the app should shut down.</summary>
    public event Action RequestShutdown;

    public ICommand SelectClientPathCommand => field ??= new RelayCommandAsync( SelectClientPath, o => true );

    public ICommand SelectDataPathCommand => field ??= new RelayCommandAsync( SelectDataPath, o => true );

    public string SelectedClientPath
    {
        get;
        set => SetProperty( ref field, value );
    }

    public string SelectedDataPath
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ShardEntry SelectedShard
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ShardManager ShardManager => ShardManager.GetInstance();

    public DateTime? ShardsDateTime { get; set; }

    public string ShardsHash { get; set; } = string.Empty;

    public ICommand ShowShardsWindowCommand => field ??= new RelayCommandAsync( ShowShardsWindow, o => true );

    public ICommand StartCommand => field ??= new RelayCommandAsync( Start, o => !string.IsNullOrEmpty( SelectedClientPath ) && !string.IsNullOrEmpty( SelectedDataPath ) );

    private async Task CheckPresets()
    {
        try
        {
            string hash = await GetShardsHash( SHARDS_HASH_URL );

            if ( !string.IsNullOrEmpty( hash ) && !ShardsHash.Equals( hash ) )
            {
                List<ShardEntry> shards = await GetShards( SHARDS_URL );

                if ( shards != null )
                {
                    foreach ( ShardEntry shardEntry in shards )
                    {
                        shardEntry.IsPreset = true;
                    }

                    ShardManager.ImportPresets( shards );
                    ShardsHash = hash;
                    ShardsDateTime = DateTime.Now;
                }
            }
        }
        catch ( Exception )
        {
            // we tried
        }
    }

    private static async Task<string> GetShardsHash( string shardsHashUrl )
    {
        using HttpClient httpClient = new();

        HttpResponseMessage response = await httpClient.GetAsync( shardsHashUrl );

        if ( !response.IsSuccessStatusCode )
        {
            return null;
        }

        string json = await response.Content.ReadAsStringAsync();

        JToken obj = JToken.Parse( json );

        return obj["SHA1"]?.ToObject<string>();
    }

    private static async Task<List<ShardEntry>> GetShards( string shardsUrl )
    {
        using HttpClient httpClient = new();

        HttpResponseMessage response = await httpClient.GetAsync( shardsUrl );

        if ( !response.IsSuccessStatusCode )
        {
            return null;
        }

        string json = await response.Content.ReadAsStringAsync();

        return JsonConvert.DeserializeObject<List<ShardEntry>>( json );
    }

    private void ReadClassicOptions( JObject config )
    {
        PropertyInfo[] properties = typeof( ClassicOptions ).GetProperties( BindingFlags.Public | BindingFlags.Instance );

        foreach ( PropertyInfo property in properties )
        {
            string propName = property.Name;
            Type propType = property.PropertyType;

            object defaultValue = null;

            ClassicOptionAttribute attr = property.GetCustomAttribute<ClassicOptionAttribute>();

            if ( attr?.DefaultValue != null )
            {
                defaultValue = attr.DefaultValue;
            }

            defaultValue ??= Activator.CreateInstance( propType );

            object val = config[propName]?.ToObject( propType ) ?? defaultValue;

            property.SetValue( ClassicOptions, val );
        }
    }

    private async Task ShowOptionsWindow( object arg )
    {
        // OptionsWindow's own constructor wires OwnerWindow onto the ViewModel its XAML
        // instantiates; assigning a separately-constructed DataContext here (as this used to)
        // would silently replace that instance, leaving OwnerWindow null and the "Add" plugin
        // file picker doing nothing when clicked. Populate the window's own instance instead.
        OptionsWindow window = new();

        if ( window.DataContext is not OptionsViewModel vm )
        {
            return;
        }

        vm.Plugins = new ObservableCollection<PluginEntry>( Plugins );
        vm.ClassicOptions = ClassicOptions;

        await window.ShowDialog( OwnerWindow );

        if ( !vm.DialogResult )
        {
            return;
        }

        Plugins.Clear();
        Plugins.AddRange( vm.Plugins );
        ClassicOptions = vm.ClassicOptions;
    }

    private async Task SelectClientPath( object obj )
    {
        if ( OwnerWindow?.StorageProvider == null )
        {
            return;
        }

        FilePickerFileType[] fileTypes = OperatingSystem.IsWindows()
            ? [new FilePickerFileType( "Client executable" ) { Patterns = new[] { "*.exe" } }]
            : [FilePickerFileTypes.All];

        IReadOnlyList<IStorageFile> files = await OwnerWindow.StorageProvider.OpenFilePickerAsync( new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select a client",
            FileTypeFilter = fileTypes
        } );

        if ( files.Count == 0 )
        {
            return;
        }

        string path = files[0].TryGetLocalPath();

        if ( string.IsNullOrEmpty( path ) )
        {
            return;
        }

        if ( !ClientPaths.Contains( path ) )
        {
            ClientPaths.Add( path );
        }

        SelectedClientPath = path;
    }

    private async Task SelectDataPath( object obj )
    {
        if ( OwnerWindow?.StorageProvider == null )
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await OwnerWindow.StorageProvider.OpenFolderPickerAsync( new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select your Ultima Online directory"
        } );

        if ( folders.Count == 0 )
        {
            return;
        }

        string path = folders[0].TryGetLocalPath();

        if ( string.IsNullOrEmpty( path ) )
        {
            return;
        }

        if ( !DataPaths.Contains( path ) )
        {
            DataPaths.Add( path );
        }

        SelectedDataPath = path;
    }

    /*
     * Command line parameter documentation...
     * https://github.com/andreakarasho/ClassicUO/wiki/Distribuite-ClassicUO
     * https://github.com/andreakarasho/ClassicUO/wiki/Launch-Arguments
     */
    private async Task Start( object obj )
    {
        IPAddress ip = await Utility.ResolveAddress( SelectedShard.Address );

        if ( ip == null )
        {
            await MessageBoxWindow.ShowAsync( OwnerWindow, "Unable to resolve shard hostname." );
            return;
        }

        ClientRuntimeFormat format = ClientRuntimeDetector.Detect( SelectedClientPath );
        string pluginPath;

        try
        {
            pluginPath = PluginPathResolver.Resolve( format );
        }
        catch ( InvalidOperationException e )
        {
            await MessageBoxWindow.ShowAsync( OwnerWindow, e.Message );
            return;
        }

        if ( !File.Exists( pluginPath ) )
        {
            await MessageBoxWindow.ShowAsync( OwnerWindow, $"Could not find the ClassicAssist plugin for this client:\n{pluginPath}" );
            return;
        }

        List<string> pluginList = [pluginPath];

        foreach ( PluginEntry plugin in Plugins )
        {
            pluginList.Add( plugin.FullPath );
        }

        if ( !OperatingSystem.IsWindows() )
        {
            UnixFileMode mode = File.GetUnixFileMode( SelectedClientPath );

            if ( ( mode & UnixFileMode.UserExecute ) == 0 )
            {
                File.SetUnixFileMode( SelectedClientPath, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute );
            }
        }

        ProcessStartInfo psi = new()
        {
            WorkingDirectory = Path.GetDirectoryName( SelectedClientPath ) ?? throw new InvalidOperationException(),
            FileName = SelectedClientPath,
            UseShellExecute = false
        };

        psi.ArgumentList.Add( "-plugins" );
        psi.ArgumentList.Add( string.Join( ",", pluginList ) );
        psi.ArgumentList.Add( "-ip" );
        psi.ArgumentList.Add( ip.ToString() );
        psi.ArgumentList.Add( "-port" );
        psi.ArgumentList.Add( SelectedShard.Port.ToString() );
        psi.ArgumentList.Add( "-uopath" );
        psi.ArgumentList.Add( SelectedDataPath );
        psi.ArgumentList.Add( "-encryption" );
        psi.ArgumentList.Add( SelectedShard.Encryption ? "1" : "0" );
        psi.ArgumentList.Add( "-shard" );
        psi.ArgumentList.Add( SelectedShard.ShardType > 0 ? SelectedShard.ShardType.ToString() : "0" );

        BuildClassicOptions( psi.ArgumentList );

        Process p = Process.Start( psi );

        SelectedShard.LastPlayed = DateTime.Now;

        JumpListService.Update( ShardManager );

        if ( p != null && !p.HasExited )
        {
            Closing( null );
            RequestShutdown?.Invoke();
        }
    }

    private void BuildClassicOptions( ICollection<string> args )
    {
        PropertyInfo[] properties = typeof( ClassicOptions ).GetProperties( BindingFlags.Public | BindingFlags.Instance );

        foreach ( PropertyInfo property in properties )
        {
            ClassicOptionAttribute attr = property.GetCustomAttribute<ClassicOptionAttribute>();
            object val = property.GetValue( ClassicOptions );

            if ( attr == null )
            {
                continue;
            }

            bool skip = val is bool b && !b && !attr.IncludeIfFalse;
            bool canInclude = true;

            if ( !string.IsNullOrEmpty( attr.CanIncludeProperty ) )
            {
                PropertyInfo canIncludeProperty = typeof( ClassicOptions ).GetProperty( attr.CanIncludeProperty );

                if ( canIncludeProperty != null )
                {
                    canInclude = (bool) canIncludeProperty.GetValue( ClassicOptions );
                }
            }

            if ( !skip && canInclude )
            {
                args.Add( attr.Argument );
                args.Add( val?.ToString() ?? string.Empty );
            }
        }
    }

    private async Task ShowShardsWindow( object obj )
    {
        // Same reasoning as ShowOptionsWindow: use the window's own XAML-instantiated
        // ViewModel rather than constructing and injecting a second one.
        ShardsWindow window = new();

        if ( window.DataContext is not ShardsViewModel vm )
        {
            return;
        }

        await window.ShowDialog( OwnerWindow );

        if ( !vm.DialogResult || vm.SelectedShard == null )
        {
            return;
        }

        SelectedShard = vm.SelectedShard;
    }

    private void Closing( object obj )
    {
        JObject config = [];

        JArray clientPathArray = [.. ClientPaths];

        config.Add( "ClientPaths", clientPathArray );
        config.Add( "SelectedClientPath", SelectedClientPath ?? string.Empty );

        JArray dataPathArray = [.. DataPaths];

        config.Add( "DataPaths", dataPathArray );
        config.Add( "SelectedDataPath", SelectedDataPath ?? string.Empty );
        config.Add( "SelectedShard", SelectedShard?.Name );

        IEnumerable<ShardEntry> shards = ShardManager.Shards.Where( s => !s.IsPreset );

        JArray shardArray = [];

        foreach ( ShardEntry shard in shards )
        {
            JObject shardObj = new()
            {
                { "Name", shard.Name },
                { "Address", shard.Address },
                { "Port", shard.Port },
                { "HasStatusProtocol", shard.HasStatusProtocol },
                { "Encryption", shard.Encryption }
            };

            shardArray.Add( shardObj );
        }

        config.Add( "Shards", shardArray );

        IEnumerable<ShardEntry> deletedPresets = ShardManager.Shards.Where( s => s.IsPreset && s.Deleted );

        JArray deletedArray = [];

        foreach ( ShardEntry shard in deletedPresets )
        {
            deletedArray.Add( new JObject { { "Name", shard.Name } } );
        }

        config.Add( "DeletedPresets", deletedArray );

        JArray pluginsArray = [];

        foreach ( PluginEntry plugin in Plugins )
        {
            pluginsArray.Add( plugin.FullPath );
        }

        config.Add( "Plugins", pluginsArray );

        config.Add( "OverridePresets", ShardManager.OverridePresets );
        config.Add( "ShardsHash", ShardsHash );
        config.Add( "ShardsDateTime", ShardsDateTime );

        if ( ShardManager.OverridePresets )
        {
            IEnumerable<ShardEntry> presets = ShardManager.Shards.Where( s => s.IsPreset );

            JArray presetsArray = [];

            foreach ( ShardEntry shard in presets )
            {
                JObject shardObj = new()
                {
                    { "Name", shard.Name },
                    { "Address", shard.Address },
                    { "Port", shard.Port },
                    { "HasStatusProtocol", shard.HasStatusProtocol },
                    { "Website", shard.Website },
                    { "Encryption", shard.Encryption },
                    { "LastPlayed", shard.LastPlayed }
                };

                presetsArray.Add( shardObj );
            }

            config.Add( "Presets", presetsArray );
        }

        WriteClassicOptions( config );

        using JsonTextWriter jtw = new( new StreamWriter( Path.Combine( AppContext.BaseDirectory, CONFIG_FILENAME ) ) );
        jtw.Formatting = Formatting.Indented;
        config.WriteTo( jtw );
    }

    private void WriteClassicOptions( JObject config )
    {
        PropertyInfo[] properties = typeof( ClassicOptions ).GetProperties( BindingFlags.Public | BindingFlags.Instance );

        foreach ( PropertyInfo property in properties )
        {
            string propName = property.Name;
            object val = property.GetValue( ClassicOptions );

            if ( val != null )
            {
                config.Add( propName, val.ToString() );
            }
        }
    }
}
