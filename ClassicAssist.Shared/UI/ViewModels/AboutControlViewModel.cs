using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows.Input;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.UI.ViewModels;

public class AboutControlViewModel : BaseViewModel
{
    private Timer _pingTimer;
    private Timer _timer;

    public string Framework { get; } = RuntimeInformation.FrameworkDescription;

    public AboutControlViewModel()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        Version version = assembly.GetName().Version;

        // The informational version, not AssemblyVersion: the latter is numeric only, so a
        // development build showed as a plain 0.5.x.0 with nothing to distinguish it from a
        // release. The -develop suffix is what decides whether the updater will replace this build,
        // which makes it the part worth reading here and in a pasted bug report.
        Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ??
            $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

        BuildDate = $"{GetBuildDateTime( assembly ).ToLongDateString()}";

        Engine.ConnectedEvent += OnConnectedEvent;
        Engine.DisconnectedEvent += OnDisconnectedEvent;
        Engine.PlayerInitializedEvent += PlayerInitializedEvent;
        Engine.Items.CollectionChanged += ItemsOnCollectionChanged;
        Engine.Mobiles.CollectionChanged += MobilesOnCollectionChanged;

        IncomingPacketHandlers.MobileUpdatedEvent += OnMobileUpdatedEvent;
    }

    public string BuildDate { get; set; }

    public ICommand CheckForUpdatesCommand => field ??= new RelayCommand( CheckForUpdates, o => true );

    public bool Connected
    {
        get;
        set => SetProperty( ref field, value );
    }

    public DateTime ConnectedTime
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int ItemCount
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int LastTargetSerial
    {
        get;
        set => SetProperty( ref field, value );
    }

    public double Latency
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand LaunchHomepageCommand => field ??= new RelayCommand( LaunchHomepage, o => true );

    public int MobileCount
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand OpenPayPalCommand => field ??= new RelayCommand( OpenPayPal, o => true );

    public string PlayerName
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int PlayerSerial
    {
        get;
        set => SetProperty( ref field, value );
    }

    public string PlayerStatus
    {
        get;
        set => SetProperty( ref field, value );
    }

    public string Product { get; } = Strings.ProductName;

    public string ShardFeatures
    {
        get;
        set => SetProperty( ref field, value );
    }

    public string ShardName
    {
        get;
        set => SetProperty( ref field, value );
    } = "Unknown";

    public ICommand ShowItemsCommand => field ??= new RelayCommand( o => ShowItems(), o => Connected );

    public string Version { get; set; }

    private static void OpenPayPal( object obj )
    {
        ShellLauncher.OpenUrl( "https://www.paypal.me/reeeetus" );
    }

    private void LastTargetChangedEvent( int serial )
    {
        LastTargetSerial = serial;
    }

    private void OnMobileUpdatedEvent( Mobile mobile )
    {
        if ( mobile.Serial == Engine.Player?.Serial )
        {
            PlayerInitializedEvent( Engine.Player );
        }
    }

    private static void LaunchHomepage( object obj )
    {
        ShellLauncher.OpenUrl( "https://github.com/Reetus/ClassicAssist" );
    }

    private void MobilesOnCollectionChanged( int totalcount, bool added, Mobile[] mobiles )
    {
        MobileCount = totalcount;
    }

    private void ItemsOnCollectionChanged( int totalcount, bool added, Item[] items )
    {
        ItemCount = Engine.Items.GetTotalItemCount();
    }

    private static void ShowItems()
    {
        Item[] e = ItemCollection.GetAllItems( Engine.Items.GetItems() );

        Engine.UIInvoker?.Invoke( "EntityCollectionViewer", null, typeof( EntityCollectionViewerViewModel ),
            [new ItemCollection( 0 ) { e }] );
    }

    private void PlayerInitializedEvent( PlayerMobile player )
    {
        PlayerSerial = player.Serial;
        PlayerName = player.Name;
        PlayerStatus = player.Status.ToString();
        ShardFeatures = Engine.Features.ToString();
        player.LastTargetChangedEvent += LastTargetChangedEvent;
        player.MobileStatusUpdated += OnMobileStatusUpdated;
        ShardName = Engine.CurrentShard?.Name ?? "Unknown";
    }

    private void OnMobileStatusUpdated( MobileStatus oldstatus, MobileStatus newstatus )
    {
        PlayerStatus = newstatus.ToString();
    }

    private void OnDisconnectedEvent()
    {
        Connected = false;

        _timer?.Stop();
    }

    private static void CheckForUpdates( object obj )
    {
        string updaterPath = FindUpdater();

        if ( updaterPath == null )
        {
            return;
        }

        // The install root is the folder the updater sits in, not the one this assembly runs from.
        // Handing over StartupPath would point the updater at ui/, and it would then copy a whole
        // release into ui/ and keep its settings there.
        string installPath = Path.GetDirectoryName( updaterPath );

        // Passed on as text rather than through System.Version, which cannot parse a prerelease tag
        // at all: a development build's "0.5.2230.0-develop" failed to parse, --version was dropped
        // entirely, and the updater was left to rediscover the version itself. It does, and gets the
        // tag - but only because that fallback exists, and losing the tag means the updater stops
        // recognising a development build and offers to overwrite it with a release.
        string version = FileVersionInfo.GetVersionInfo( Assembly.GetExecutingAssembly().Location )
            .ProductVersion;

        // Quoted: the install path is chosen by the user and routinely contains spaces.
        ProcessStartInfo psi = new( updaterPath,
            $"--pid {Process.GetCurrentProcess().Id} --path \"{installPath}\"" +
            ( !string.IsNullOrWhiteSpace( version ) ? $" --version \"{version}\"" : "" ) )
        { UseShellExecute = false, WorkingDirectory = installPath };

        Process.Start( psi );
    }

    /// <summary>
    ///     Locates the updater, which lives in the install root while this assembly runs from the ui
    ///     subfolder below it. WPF has no such split and looks only beside itself, which is why the
    ///     button did nothing at all here - the file was never where it looked. The flat case is
    ///     probed first anyway, for a development build run out of one folder.
    /// </summary>
    internal static string FindUpdater()
    {
        // The apphost has no extension outside Windows, so the .exe spelling alone found nothing on
        // Linux even once the folder was right.
        string fileName = OperatingSystem.IsWindows() ? "ClassicAssist.Updater.exe" : "ClassicAssist.Updater";

        string startupPath = Engine.StartupPath ?? Environment.CurrentDirectory;

        // Normalised rather than left as ui/../: the path is handed to the updater, which shows it
        // to the user and matches it against the module paths of running clients.
        string[] candidates =
        [
            Path.GetFullPath( Path.Combine( startupPath, fileName ) ),
            Path.GetFullPath( Path.Combine( startupPath, "..", fileName ) )
        ];

        return Array.Find( candidates, File.Exists );
    }

    private void OnConnectedEvent()
    {
        Connected = true;
        ConnectedTime = DateTime.Now;

        _timer = new Timer( 1000 ) { AutoReset = true };
        _timer.Elapsed += ( sender, args ) => { NotifyPropertyChanged( nameof( ConnectedTime ) ); };
        _timer.Start();

        _pingTimer = new Timer( 3000 ) { AutoReset = true };
        _pingTimer.Elapsed += ( sender, args ) => PingServer();
        _pingTimer.Start();
    }

    private void PingServer()
    {
        _pingTimer.Interval = 30000;

        Random random = new();

        byte value = (byte) random.Next( 1, byte.MaxValue );

        Stopwatch sw = new();
        sw.Start();

        PacketWaitEntry we = Engine.PacketWaitEntries.Add(
            new PacketFilterInfo( 0x73, [new PacketFilterCondition( 1, [value], 1 )] ),
            PacketDirection.Incoming, true );

        Engine.SendPacketToServer( new Ping( value ) );

        bool result = we.Lock.WaitOne( 5000 );

        sw.Stop();

        if ( result )
        {
            Latency = sw.ElapsedMilliseconds;
        }
    }

    internal static DateTime GetBuildDateTime( Assembly assembly )
    {
        BuildDateAttribute attribute = assembly.GetCustomAttribute<BuildDateAttribute>();

        // Absent only in a build without the AssemblyAttribute item - a consumer of this library
        // outside the solution. Not worth crashing the About tab over.
        return attribute?.DateTime ?? File.GetLastWriteTime( assembly.Location );
    }
}