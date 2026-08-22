#region License

// Copyright (C) 2026 Reetus
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY

#endregion

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Regions;
using ClassicAssist.Data.Screenshot;
using ClassicAssist.Data.Targeting;
using ClassicAssist.Misc;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UI.ViewModels.Agents.Screenshot;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Shared.UI.ViewModels.Agents;

/// <summary>
///     The Screenshot agent: a gallery of what has been taken, the info bar drawn over each shot, and
///     the death triggers that take them unattended.
///     <para>
///         Upstream captures the client window with GDI (<c>BitBlt</c> from the plugin header's window
///         handle). Neither half of that survives the port - the handle is <c>IntPtr.Zero</c> on every
///         client this fork loads into except ClassicUO on Windows, and GDI is Windows-only anyway - so
///         the pixels come from the client's own graphics device instead, through
///         <see cref="ReflectionCommands.CaptureClientFrame" />. That is the same thing the client's
///         PrintScreen handler does, works the same on all three platforms, and captures the client
///         window rather than the desktop. Upstream's UO-only/fullscreen choice therefore has no
///         meaning here and is gone; the macro command still accepts the argument so existing scripts
///         keep working.
///     </para>
/// </summary>
public class ScreenshotTabViewModel : BaseViewModel, ISettingProvider
{
    private const string SCREENSHOT_DIRECTORY_NAME = "Screenshots";
    private const string DEFAULT_FILENAME_FORMAT = "ClassicAssist-{date}-{longTime}";
    private const string DEFAULT_INFO_BAR_FORMAT = "{player} ({shard}) - {date} {time}";

    private static readonly string[] _extensions = [".png", ".gif"];
    private readonly ScreenshotComparer _comparer = new();
    private string _screenshotPath;
    private FileSystemWatcher _watcher;

    public ScreenshotTabViewModel()
    {
        ScreenshotManager manager = ScreenshotManager.GetInstance();

        manager.TakeScreenshot = TakeScreenshot;
        manager.OnPlayerDeath = OnPlayerDeath;
        manager.OnMobileDeath = OnMobileDeath;

        Engine.ConnectedEvent += OnConnected;

        _ = RefreshCaptureSupport();
    }

    public bool AutoScreenshot
    {
        get;
        set => SetProperty( ref field, value );
    }

    public string BackgroundColor
    {
        get;
        set => SetProperty( ref field, value );
    } = "#FF000000";

    /// <summary>
    ///     False when this client cannot be captured at all - a NativeAOT ClassicUO, whose graphics
    ///     stack is native code with no managed device to read back. The tab disables itself and says
    ///     so, rather than offering a button that fails every time. Starts true so nothing flickers
    ///     disabled while the probe is in flight.
    /// </summary>
    public bool CaptureSupported
    {
        get;
        private set => SetProperty( ref field, value );
    } = true;

    public ICommand ConfigureFilterCommand => field ??= new RelayCommandAsync( ConfigureFilter, o => true );

    public int Distance
    {
        get;
        set => SetProperty( ref field, value );
    } = 12;

    public string FilenameFormat
    {
        get;
        set => SetProperty( ref field, value );
    } = DEFAULT_FILENAME_FORMAT;

    public string FontColor
    {
        get;
        set => SetProperty( ref field, value );
    } = "#FFFFFFFF";

    public int FontSize
    {
        get;
        set => SetProperty( ref field, value );
    } = 16;

    public string Format
    {
        get;
        set => SetProperty( ref field, value );
    } = DEFAULT_INFO_BAR_FORMAT;

    public bool IncludeInfoBar
    {
        get;
        set => SetProperty( ref field, value );
    } = true;

    public bool MobileDeath
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int MobileDeathDelay
    {
        get;
        set => SetProperty( ref field, value );
    } = 500;

    public List<ScreenshotMobileFilterEntry> MobileDeathFilter
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public bool OnlyIfEnemy
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand OpenFolderCommand => field ??= new RelayCommand( OpenFolder, o => true );

    public ICommand OpenScreenshotCommand => field ??= new RelayCommand( OpenScreenshot, o => o != null );

    public bool PlayerDeath
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int PlayerDeathDelay
    {
        get;
        set => SetProperty( ref field, value );
    } = 2000;

    public ObservableCollection<ScreenshotEntry> Screenshots
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ICommand SetBackgroundColourCommand =>
        field ??= new RelayCommandAsync( SetBackgroundColour, o => IncludeInfoBar );

    public ICommand SetFontColourCommand => field ??= new RelayCommandAsync( SetFontColour, o => IncludeInfoBar );

    public ICommand TakeSnapshotCommand => field ??= new RelayCommandAsync( TakeSnapshot, o => CaptureSupported );

    public void Serialize( JObject json )
    {
        if ( json == null )
        {
            return;
        }

        JObject obj = new()
        {
            { "FilenameFormat", FilenameFormat },
            { "IncludeInfoBar", IncludeInfoBar },
            { "Format", Format },
            { "FontSize", FontSize },
            { "FontColor", FontColor },
            { "BackgroundColor", BackgroundColor },
            { "AutoScreenshot", AutoScreenshot },
            { "PlayerDeath", PlayerDeath },
            { "PlayerDeathDelay", PlayerDeathDelay },
            { "MobileDeath", MobileDeath },
            { "MobileDeathDelay", MobileDeathDelay },
            { "Distance", Distance },
            { "OnlyIfEnemy", OnlyIfEnemy }
        };

        JArray filter = [];

        foreach ( ScreenshotMobileFilterEntry entry in MobileDeathFilter )
        {
            filter.Add( new JObject { { "ID", entry.ID }, { "Note", entry.Note }, { "Enabled", entry.Enabled } } );
        }

        obj.Add( "MobileDeathFilter", filter );
        json.Add( "Screenshot", obj );
    }

    public void Deserialize( JObject json, Options options )
    {
        StartWatchingScreenshots();

        JToken screenshot = json?["Screenshot"];

        if ( screenshot == null )
        {
            MobileDeathFilter = GetDefaultMobileIDs();
            return;
        }

        FilenameFormat = screenshot["FilenameFormat"]?.ToObject<string>() ?? DEFAULT_FILENAME_FORMAT;
        IncludeInfoBar = screenshot["IncludeInfoBar"]?.ToObject<bool>() ?? true;
        Format = screenshot["Format"]?.ToObject<string>() ?? DEFAULT_INFO_BAR_FORMAT;
        FontSize = screenshot["FontSize"]?.ToObject<int>() ?? 16;
        BackgroundColor = screenshot["BackgroundColor"]?.ToObject<string>() ?? "#FF000000";
        FontColor = screenshot["FontColor"]?.ToObject<string>() ?? "#FFFFFFFF";
        AutoScreenshot = screenshot["AutoScreenshot"]?.ToObject<bool>() ?? false;
        PlayerDeath = screenshot["PlayerDeath"]?.ToObject<bool>() ?? false;
        PlayerDeathDelay = screenshot["PlayerDeathDelay"]?.ToObject<int>() ?? 2000;
        MobileDeath = screenshot["MobileDeath"]?.ToObject<bool>() ?? false;
        MobileDeathDelay = screenshot["MobileDeathDelay"]?.ToObject<int>() ?? 500;
        Distance = screenshot["Distance"]?.ToObject<int>() ?? 12;
        OnlyIfEnemy = screenshot["OnlyIfEnemy"]?.ToObject<bool>() ?? false;

        // FontSize 0 means a profile written before these settings existed; upstream rewrote the whole
        // object in that case, but every read above already defaults, so only the size needs fixing.
        if ( FontSize == 0 )
        {
            FontSize = 16;
        }

        MobileDeathFilter = screenshot["MobileDeathFilter"] is JArray mobileIdArray
            ? mobileIdArray.Cast<JObject>().Select( obj => new ScreenshotMobileFilterEntry
            {
                ID = obj["ID"]?.ToObject<int>() ?? 0,
                Note = obj["Note"]?.ToObject<string>() ?? string.Empty,
                Enabled = obj["Enabled"]?.ToObject<bool>() ?? false
            } ).ToList()
            : GetDefaultMobileIDs();
    }

    /// <summary>
    ///     Captures the client window and writes the PNG, returning its path - or null if this client
    ///     cannot be captured, the client stopped ticking mid-capture, or there is no composer (a host
    ///     without rendering).
    /// </summary>
    public async Task<string> TakeScreenshot( string mobileName = null, string filename = null )
    {
        ScreenshotFrame frame = await ReflectionCommands.CaptureClientFrame();

        if ( frame?.Path == null )
        {
            // A capture that comes back empty is either an unsupported client or a client that stopped
            // ticking. Re-probe so the tab can tell the user which, instead of silently doing nothing.
            await RefreshCaptureSupport();

            return null;
        }

        try
        {
            if ( Engine.ScreenshotComposer == null )
            {
                return null;
            }

            DateTime now = DateTime.Now;

            string filePath =
                $"{GetFormattedText( !string.IsNullOrEmpty( filename ) ? filename : FilenameFormat, now, mobileName, true )}.png";

            if ( !Path.IsPathRooted( filePath ) )
            {
                string path = GetScreenshotPath();

                Directory.CreateDirectory( path );

                filePath = Path.Combine( path, filePath );
            }

            await Engine.ScreenshotComposer.ComposeAsync( new ScreenshotComposeRequest
            {
                OutputPath = filePath,
                FramePath = frame.Path,
                Width = frame.Width,
                Height = frame.Height,
                InfoBarText = IncludeInfoBar ? GetFormattedText( Format, now, mobileName ) : null,
                FontSize = FontSize,
                FontColour = FontColor,
                BackgroundColour = BackgroundColor
            } );

            return filePath;
        }
        finally
        {
            // The frame file is ours once it has been read - see ScreenshotFrame.
            try
            {
                File.Delete( frame.Path );
            }
            catch ( Exception )
            {
                // Swept by the plugin later if it is still there.
            }
        }
    }

    public void OnPlayerDeath( string name )
    {
        if ( !AutoScreenshot || !PlayerDeath )
        {
            return;
        }

        _ = TakeDelayedScreenshot( PlayerDeathDelay, name );
    }

    private void OnMobileDeath( Mobile mobile )
    {
        if ( !AutoScreenshot || !MobileDeath || !MobileDeathFilter.Any( e => e.ID == mobile.ID && e.Enabled ) ||
             mobile.Distance > Distance )
        {
            return;
        }

        if ( OnlyIfEnemy && AliasCommands.GetAlias( "enemy" ) != mobile.Serial )
        {
            return;
        }

        _ = TakeDelayedScreenshot( MobileDeathDelay, mobile.Name );
    }

    private async Task TakeDelayedScreenshot( int delay, string mobileName )
    {
        try
        {
            if ( delay > 0 )
            {
                await Task.Delay( delay );
            }

            await TakeScreenshot( mobileName );
        }
        catch ( Exception )
        {
            // A screenshot that fails must not take a death handler down with it.
        }
    }

    private void OnConnected()
    {
        // The device is only reachable once the client has one, so a probe from the constructor can run
        // before the game window exists. Ask again on connect.
        _ = RefreshCaptureSupport();
    }

    private async Task RefreshCaptureSupport()
    {
        try
        {
            CaptureSupported = await ReflectionCommands.CanCaptureClientFrame();
        }
        catch ( Exception )
        {
            CaptureSupported = false;
        }
    }

    private static List<ScreenshotMobileFilterEntry> GetDefaultMobileIDs()
    {
        TargetManager targetManager = TargetManager.GetInstance();

        return targetManager.BodyData
            .Where( b => b.BodyType == TargetBodyType.Humanoid && !b.Name.Contains( "Dead" ) ).Select( b =>
                new ScreenshotMobileFilterEntry { ID = b.Graphic, Note = b.Name, Enabled = true } ).ToList();
    }

    private async Task ConfigureFilter( object obj )
    {
        ScreenshotMobileFilterViewModel vm = new( MobileDeathFilter );

        await Engine.UIInvoker.InvokeDialog( "ScreenshotMobileFilterWindow", dataContext: vm );

        if ( vm.Result )
        {
            MobileDeathFilter = [.. vm.Items];
        }
    }

    private async Task TakeSnapshot( object obj )
    {
        string fileName = await TakeScreenshot();

        if ( !string.IsNullOrEmpty( fileName ) )
        {
            return;
        }

        // Say which of the two it was: a client that can never do this, or one that did not answer.
        string message = CaptureSupported ? Strings.Snapshot_failed : Strings.Screenshots_not_supported;

        await ( Engine.MessageBoxProvider?.Show( message, Strings.Screenshot, MessageBoxButtons.OK,
            MessageBoxImage.Warning ) ?? Task.FromResult( MessageBoxResult.OK ) );
    }

    private async Task SetBackgroundColour( object obj )
    {
        string colour = await PickColour( BackgroundColor );

        if ( colour != null )
        {
            BackgroundColor = colour;
        }
    }

    private async Task SetFontColour( object obj )
    {
        string colour = await PickColour( FontColor );

        if ( colour != null )
        {
            FontColor = colour;
        }
    }

    private static async Task<string> PickColour( string current )
    {
        MacrosGumpTextColorSelectorViewModel vm = new() { SelectedColor = current };

        await Engine.UIInvoker.InvokeDialog( "MacrosGumpTextColorWindow", dataContext: vm );

        return vm.Result ? vm.SelectedColor : null;
    }

    private static void OpenScreenshot( object obj )
    {
        if ( obj is ScreenshotEntry screenshot )
        {
            ShellLauncher.OpenFile( screenshot.Path );
        }
    }

    private void OpenFolder( object obj )
    {
        ShellLauncher.OpenFolder( GetScreenshotPath() );
    }

    private string GetScreenshotPath()
    {
        return _screenshotPath ??= Path.Combine( Engine.StartupPath ?? string.Empty, SCREENSHOT_DIRECTORY_NAME );
    }

    /// <summary>
    ///     Fills the gallery from disk and watches the folder, so shots taken by a macro or dropped in
    ///     by hand show up too.
    /// </summary>
    private void StartWatchingScreenshots()
    {
        if ( _watcher != null )
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        string path = GetScreenshotPath();

        try
        {
            Directory.CreateDirectory( path );

            foreach ( string file in _extensions.SelectMany( ext => Directory.GetFiles( path, $"*{ext}" ) ) )
            {
                AddScreenshot( file );
            }

            _watcher = new FileSystemWatcher( path, "*.*" ) { EnableRaisingEvents = true };
            _watcher.Created += OnScreenshotCreated;
            _watcher.Deleted += OnScreenshotDeleted;
        }
        catch ( Exception )
        {
            // No screenshots folder is survivable - the gallery is empty until one can be made.
        }
    }

    private void OnScreenshotDeleted( object sender, FileSystemEventArgs e )
    {
        ScreenshotEntry screenshot = Screenshots.FirstOrDefault( s => s.Path.Equals( e.FullPath ) );

        if ( screenshot != null )
        {
            _dispatcher.Invoke( () => Screenshots.Remove( screenshot ) );
        }
    }

    private void OnScreenshotCreated( object sender, FileSystemEventArgs e )
    {
        if ( !_extensions.Contains( Path.GetExtension( e.FullPath ) ) )
        {
            return;
        }

        // Created fires when the file appears, not when it is finished - give the writer a moment
        // before anything tries to decode it for a thumbnail.
        Task.Delay( 1000 ).ContinueWith( t => AddScreenshot( e.FullPath ) );
    }

    private void AddScreenshot( string file )
    {
        _dispatcher.Invoke( () =>
        {
            if ( Screenshots.Any( s => s.Path.Equals( file ) ) )
            {
                return;
            }

            Screenshots.AddSorted( new ScreenshotEntry
            {
                Path = file,
                CreatedDate = File.GetCreationTime( file ),
                Extension = Path.GetExtension( file ).Replace( ".", string.Empty ).ToUpper()
            }, _comparer );
        } );
    }

    private static string GetFormattedText( string format, DateTime now, string mobileName,
        bool filenameChars = false )
    {
        if ( filenameChars && string.IsNullOrEmpty( format ) )
        {
            format = DEFAULT_FILENAME_FORMAT;
        }

        Dictionary<string, Func<string>> replacements = new()
        {
            { "player", () => Engine.Player?.Name },
            { "shard", () => Engine.CurrentShard?.Name },
            { "mobile", () => mobileName },
            { "date", now.ToShortDateString },
            { "time", now.ToShortTimeString },
            { "longDate", now.ToLongDateString },
            { "longTime", now.ToLongTimeString },
            { "isoDate", () => now.ToString( "O" ) },
            { "x", () => Engine.Player?.X.ToString() },
            { "y", () => Engine.Player?.Y.ToString() },
            { "map", () => Engine.Player?.Map.ToString() },
            { "region", () => Regions.GetRegion( Engine.Player )?.Name },
            { "ticks", now.Ticks.ToString }
        };

        return Regex.Replace( format ?? string.Empty, "{(.*?)}", match =>
        {
            string key = match.Groups[1].Value;
            string replacementValue = replacements.TryGetValue( key, out Func<string> replacement )
                ? replacement()
                : key;
            string str = string.IsNullOrEmpty( replacementValue ) ? string.Empty : replacementValue;

            if ( filenameChars )
            {
                str = Path.GetInvalidFileNameChars().Aggregate( str, ( current, c ) => current.Replace( c, '-' ) );
            }

            return str;
        } ).Trim();
    }

    private class ScreenshotComparer : IComparer<ScreenshotEntry>
    {
        public int Compare( ScreenshotEntry x, ScreenshotEntry y )
        {
            if ( ReferenceEquals( x, y ) )
            {
                return 0;
            }

            if ( y is null )
            {
                return 1;
            }

            if ( x is null )
            {
                return -1;
            }

            return y.CreatedDate.CompareTo( x.CreatedDate );
        }
    }

    /// <summary>
    ///     One entry in the gallery. Only the path travels: decoding it into a thumbnail is the view's
    ///     job, since this assembly has no toolkit to decode with.
    /// </summary>
    public class ScreenshotEntry
    {
        public DateTime CreatedDate { get; set; }
        public string Extension { get; set; }
        public string Path { get; set; }
    }
}
