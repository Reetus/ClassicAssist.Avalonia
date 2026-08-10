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

        Version = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
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
        string startupPath = Engine.StartupPath ?? Environment.CurrentDirectory;

        // The apphost has no extension outside Windows, so the .exe spelling alone found nothing on
        // Linux and the button did quietly nothing.
        string updaterPath = Path.Combine( startupPath,
            OperatingSystem.IsWindows() ? "ClassicAssist.Updater.exe" : "ClassicAssist.Updater" );

        Version version = null;

        if ( System.Version.TryParse(
            FileVersionInfo.GetVersionInfo( Assembly.GetExecutingAssembly().Location ).ProductVersion,
            out Version v ) )
        {
            version = v;
        }

        if ( !File.Exists( updaterPath ) )
        {
            return;
        }

        // Quoted: the install path is chosen by the user and routinely contains spaces.
        ProcessStartInfo psi = new( updaterPath,
            $"--pid {Process.GetCurrentProcess().Id} --path \"{startupPath}\"" + ( version != null
                ? $" --version {version}"
                : "" ) )
        { UseShellExecute = false };

        Process.Start( psi );
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
        System.Version.TryParse( FileVersionInfo.GetVersionInfo( assembly.Location ).FileVersion,
            out Version version );

        DateTime buildDateTime =
            new DateTime( 2020, 7, 3 ).Add( new TimeSpan( TimeSpan.TicksPerDay * version.Build ) );

        return buildDateTime;
    }
}