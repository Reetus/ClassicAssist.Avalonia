using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Data;
using ClassicAssist.Data.Abilities;
using ClassicAssist.Data.Commands;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Misc;
using ClassicAssist.Data.Scavenger;
using ClassicAssist.Data.Targeting;
using ClassicAssist.Misc;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Shared.UO;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;
using Sentry;
// ReSharper disable once RedundantUsingDirective
using System;

[assembly: InternalsVisibleTo( "ClassicAssist.Tests" )]

// ReSharper disable once CheckNamespace
namespace ClassicAssist.Shared;

public static partial class Engine
{
    public delegate void dConnected();

    public delegate void dDisconnected();

    public delegate void dPlayerInitialized( PlayerMobile player );

    public delegate void dSendRecvPacket( byte[] data, int length );

    public delegate void dShutdown();

    public delegate void dUpdateWindowTitle();

    private const int MAX_DISTANCE = 32;

    private static SendRecvPacket _sendToClient;
    private static SendRecvPacket _sendToServer;
    private static GetPacketLength _getPacketLength;
    private static readonly PacketFilter _incomingPacketFilter = new();
    private static readonly PacketFilter _outgoingPacketPreFilter = new();
    private static readonly PacketFilter _outgoingPacketPostFilter = new();

    /// <summary>Last time each key actually ran its hotkey, for <see cref="Options.LimitHotkeyTrigger" />.</summary>
    private static readonly Dictionary<Key, DateTime> _lastKeyAction = [];
    private static Move _requestMove;

    private static readonly int[] _sequenceList = new int[256];

    private static readonly DateTime[] _lastMouseAction = new DateTime[(int) MouseOptions.None];
    private static readonly Lock _clientSendLock = new();
    private static DateTime _nextPacketRecvTime;

    private static readonly TimeSpan PACKET_RECV_DELAY = TimeSpan.FromMilliseconds( 5 );
    private static readonly Lock _serverSendLock = new();

    private static readonly TimeSpan PACKET_SEND_DELAY = TimeSpan.FromMilliseconds( 5 );
    private static DateTime _nextPacketSendTime;
    public static int LastSpellID;
    public static int LastSkillID;
    public static DateTime LastSkillTime { get; set; }
    public static CharacterListFlags CharacterListFlags { get; set; }

    public static Assembly ClassicAssembly { get; set; }

    public static string ClientPath { get; set; }
    public static Version ClientVersion { get; set; }
    public static bool Connected { get; set; }
    public static ShardEntry CurrentShard { get; set; }
    public static IDispatcher Dispatcher { get; set; } = new InlineDispatcher();
    public static FeatureFlags Features { get; set; }
    public static GumpCollection Gumps { get; set; } = new();
    public static IHostMethods Host { get; set; }

    /// <summary>
    ///     The plugin starts pushing callbacks the moment the pipe is attached, which is before
    ///     <see cref="InstallRPC" /> has finished loading the UO files and building the managers.
    ///     Callbacks arriving before that are dropped rather than crashing on half-built state.
    /// </summary>
    public static bool Installed { get; private set; }

    public static bool IsClientFocused { get; set; }
    public static ItemCollection Items { get; set; } = new( 0 );
    public static CircularBuffer<JournalEntry> Journal { get; set; } = new( 1024 );

    public static DateTime LastActionPacket { get; set; }
    public static DateTime LastMoveRequested { get; set; }
    public static int LastPromptID { get; set; }
    public static int LastPromptSerial { get; set; }
    public static int LastPromptType { get; set; }
    public static TargetQueue<TargetQueueObject> LastTargetQueue { get; set; } = new();
    public static MenuCollection Menus { get; set; } = new();
    public static IMessageBoxProvider MessageBoxProvider { get; private set; }
    public static MobileCollection Mobiles { get; set; } = new( Items );
    public static PacketWaitEntries PacketWaitEntries { get; set; }
    public static PlayerMobile Player { get; set; }
    public static QuestPointerList QuestPointers { get; set; } = [];

    /// <summary>
    ///     False when the plugin was loaded via the native DNNE export (modern ClassicUO) rather than
    ///     the managed load path TazUO always uses - see <see cref="IHostMethods.IsReflectionAvailable" />.
    ///     Client-internals reflection (<see cref="ReflectionCommands" />) is only expected to work
    ///     against the TazUO shapes it targets, so callers with a non-reflection fallback should check
    ///     this rather than assume <see cref="Host" /> being set means reflection works.
    /// </summary>
    public static bool ReflectionAvailable { get; set; }

    public static RehueList RehueList { get; set; } = new();
    public static List<ShardEntry> Shards { get; set; }
    public static string StartupPath { get; set; }
    public static bool TargetExists { get; set; }
    public static TargetFlags TargetFlags { get; set; }
    public static int TargetSerial { get; set; }
    public static TargetType TargetType { get; set; }

    /// <summary>
    ///     Work queued from off-tick contexts (macro commands, extensions) that needs to run on the next
    ///     <c>OnTick</c> callback instead. Mirrors upstream ClassicAssist's <c>Engine.TickWorkQueue</c> -
    ///     unlike <c>PluginEngine.TickWorkQueue</c>, which drains reflection calls on the client's own thread,
    ///     this one drains on whatever thread the UI process's inbound RPC <c>OnTick</c> arrives on.
    /// </summary>
    public static Queue<Action> TickWorkQueue { get; set; } = new();

    public static bool TooltipsEnabled { get; set; }

    /// <summary>
    ///     State of the current secure trade window, tracked from the 0x6F packet in both directions.
    /// </summary>
    public static Trade Trade { get; set; } = new();

    public static IUIInvoker UIInvoker { get; set; }
    public static bool WaitingForTarget { get; set; }
    internal static ConcurrentDictionary<uint, int> GumpList { get; set; } = new();

    public static event dShutdown Shutdown;

    public static event dUpdateWindowTitle UpdateWindowTitleEvent;

    public static event dSendRecvPacket InternalPacketSentEvent;
    public static event dSendRecvPacket InternalPacketReceivedEvent;

    public static event dSendRecvPacket PacketReceivedEvent;
    public static event dSendRecvPacket PacketSentEvent;
    public static event dSendRecvPacket SentPacketFilteredEvent;
    public static event dSendRecvPacket ReceivedPacketFilteredEvent;
    public static event dConnected ConnectedEvent;
    public static event dDisconnected DisconnectedEvent;
    public static event dPlayerInitialized PlayerInitializedEvent;
    public static bool InternalTarget { get; set; }
    public static int InternalTargetSerial { get; set; }

    /// <summary>
    ///     Sole entry point for the UI process. The plugin half lives in the game process and is reached
    ///     only through <paramref name="hostMethods" />; there is deliberately no in-process variant,
    ///     because Avalonia on Linux requires the UI to own the process main thread.
    /// </summary>
    public static void InstallRPC( IHostMethods hostMethods, IMessageBoxProvider provider )
    {
        Host = hostMethods;
        MessageBoxProvider = provider;

        Initialize();

        ClientPath = Host.GetClientPath().Result;
        ClientVersion = Version.TryParse( Host.GetClientVersion().Result, out Version clientVersion ) ? clientVersion : new Version( 0, 0, 0, 0 );
        WindowHandle = Host.GetWindowHandle().Result;
        ReflectionAvailable = Host.IsReflectionAvailable().Result;

        if ( !Path.IsPathRooted( ClientPath ) )
        {
            ClientPath = Path.GetFullPath( ClientPath );
        }

        Art.Initialize( ClientPath );
        Hues.Initialize( ClientPath );
        Cliloc.Initialize( ClientPath );
        Skills.Initialize( ClientPath );
        Speech.Initialize( ClientPath );
        TileData.Initialize( ClientPath );
        Statics.Initialize( ClientPath );
        MapInfo.Initialize( ClientPath );

        InitializeExtensions();

        _getPacketLength = id => Host.GetPacketLength( id ).Result;
        _sendToClient = SendPacketToClientPlugin;
        _sendToServer = SendPacketToServerPlugin;
        _requestMove = ( dir, run ) => Host.RequestMove( dir, run ).Result;

        Installed = true;
    }

    public static void InitializeExtensions()
    {
        IEnumerable<Type> types = Assembly.GetExecutingAssembly().GetTypes().Where( t => typeof( IExtension ).IsAssignableFrom( t ) && t.IsClass );

        foreach ( Type type in types )
        {
            try
            {
                IExtension instance = (IExtension) Activator.CreateInstance( type );
                instance?.Initialize();
            }
            catch ( Exception e )
            {
                Console.WriteLine( e.ToString() );
            }
        }
    }

    public static void OnMouse( int button, int wheel )
    {
        MouseOptions mouse = MouseOptions.None;

        if ( button > 0 )
        {
            mouse = SDLKeys.MouseButtonToMouseOptions( button );
        }

        if ( wheel != 0 )
        {
            mouse = wheel < 0 ? MouseOptions.MouseWheelDown : MouseOptions.MouseWheelUp;

            if ( Options.CurrentOptions.LimitMouseWheelTrigger )
            {
                TimeSpan diff = DateTime.Now - _lastMouseAction[(int) mouse];

                if ( diff < TimeSpan.FromMilliseconds( Options.CurrentOptions.LimitMouseWheelTriggerMS ) )
                {
                    return;
                }
            }

            _lastMouseAction[(int) mouse] = DateTime.Now;
        }

        HotkeyManager.GetInstance().OnMouseAction( mouse );
    }

    public static bool OnHotkeyPressed( int key, int mod, bool pressed )
    {
        // Key-up carries no action to run; only fire on key-down, otherwise every hotkey/macro
        // toggle fires twice per press. Returning true lets the release reach the client normally.
        if ( !pressed )
        {
            return true;
        }

        if ( !IsClientFocused )
        {
            return false;
        }

        Key keys = SDLKeys.SDLKeyToKeys( key );
        Key modifier = SDLKeys.SDLKeymodToKey( mod );

        // Retrigger limit: a key held down (or mashed) repeats at the OS repeat rate, which fires the
        // bound macro over and over. When throttled the action is skipped but the lookup still runs, so
        // the key is still withheld from the client - otherwise a bound key would leak through to UO
        // exactly while the user is leaning on it.
        bool noexecute = false;

        if ( Options.CurrentOptions.LimitHotkeyTrigger &&
             _lastKeyAction.TryGetValue( keys, out DateTime lastAction ) )
        {
            noexecute = DateTime.Now - lastAction <
                        TimeSpan.FromMilliseconds( Options.CurrentOptions.LimitHotkeyTriggerMS );
        }

        (bool found, bool pass) = HotkeyManager.GetInstance().OnHotkeyPressed( keys, modifier, noexecute );

        if ( found && !noexecute )
        {
            _lastKeyAction[keys] = DateTime.Now;
        }

        return !pass;
    }

    public static void OnClientClosing()
    {
        Options.Save( Options.CurrentOptions );
        AssistantOptions.Save();
        SentrySdk.Close();
        Host?.OnShutdown();
        Shutdown?.Invoke();
    }

    public static void OnPlayerPositionChanged( int x, int y, int z )
    {
        if ( Player != null )
        {
            Player.X = x;
            Player.Y = y;
            Player.Z = z;
        }

        Items.RemoveByDistance( MAX_DISTANCE, x, y );
        Mobiles.RemoveByDistance( MAX_DISTANCE, x, y );
        ScavengerManager.GetInstance().CheckArea?.Invoke();
    }

    public static Item GetOrCreateItem( int serial, int containerSerial = -1 )
    {
        Item item = Items.GetItem( serial );

        if ( item != null )
        {
            return item;
        }

        item = new Item( serial, containerSerial );

        if ( IncomingPacketHandlers.PropertyCache.TryGetValue( serial, out Property[] properties ) )
        {
            item.Properties = properties;
        }

        return item;
    }

    public static Mobile GetOrCreateMobile( int serial )
    {
        if ( Player?.Serial == serial )
        {
            return Player;
        }

        if ( Mobiles.GetMobile( serial, out Mobile mobile ) )
        {
            return mobile;
        }

        mobile = new Mobile( serial );

        if ( IncomingPacketHandlers.PropertyCache.TryGetValue( serial, out Property[] properties ) )
        {
            mobile.Properties = properties;
        }

        return mobile;
    }

    public static void Initialize()
    {
        StartupPath = Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location );

        if ( StartupPath == null )
        {
            throw new InvalidOperationException();
        }

        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

        PacketWaitEntries = new PacketWaitEntries();

        IncomingQueue = new ThreadQueue<Packet>( ProcessIncomingQueue );
        OutgoingQueue = new ThreadQueue<Packet>( ProcessOutgoingQueue );

        IncomingPacketHandlers.Initialize();
        OutgoingPacketHandlers.Initialize();

        IncomingPacketFilters.Initialize();
        OutgoingPacketFilters.Initialize();

        CommandsManager.Initialize();

        AssistantOptions.Load();
    }

    private static void ProcessIncomingQueue( Packet packet )
    {
        try
        {
            PacketReceivedEvent?.Invoke( packet.GetPacket(), packet.GetLength() );

            PacketHandler handler = IncomingPacketHandlers.GetHandler( packet.GetPacketID() );

            int length = _getPacketLength( packet.GetPacketID() );

            handler?.OnReceive?.Invoke( new PacketReader( packet.GetPacket(), packet.GetLength(), length > 0 ) );

            PacketWaitEntries?.CheckWait( packet.GetPacket(), PacketDirection.Incoming );
        }
        catch ( Exception e )
        {
            SentrySdk.CaptureException( e, scope =>
            {
                scope.SetExtra( "Packet", packet.GetPacket() );
                scope.SetExtra( "Player", Player.ToString() );
                scope.SetExtra( "WorldItemCount", Items.Count() );
                scope.SetExtra( "WorldMobileCount", Mobiles.Count() );
            } );
        }
    }

    private static void ProcessOutgoingQueue( Packet packet )
    {
        try
        {
            PacketSentEvent?.Invoke( packet.GetPacket(), packet.GetLength() );

            PacketHandler handler = OutgoingPacketHandlers.GetHandler( packet.GetPacketID() );

            int length = _getPacketLength( packet.GetPacketID() );

            handler?.OnReceive?.Invoke( new PacketReader( packet.GetPacket(), packet.GetLength(), length > 0 ) );

            PacketWaitEntries?.CheckWait( packet.GetPacket(), PacketDirection.Outgoing );
        }
        catch ( Exception e )
        {
            SentrySdk.CaptureException( e, scope =>
            {
                scope.SetExtra( "Packet", packet.GetPacket() );
                scope.SetExtra( "Player", Player.ToString() );
                scope.SetExtra( "WorldItemCount", Items.Count() );
                scope.SetExtra( "WorldMobileCount", Mobiles.Count() );
            } );
        }
    }

    private static Assembly OnAssemblyResolve( object sender, ResolveEventArgs args )
    {
        string assemblyname = new AssemblyName( args.Name ).Name;

        string[] searchPaths = [StartupPath, RuntimeEnvironment.GetRuntimeDirectory()];

        if ( assemblyname.Contains( "Colletions" ) )
        {
            assemblyname = "System.Collections";
        }

        foreach ( string searchPath in searchPaths )
        {
            string fullPath = Path.Combine( searchPath, assemblyname + ".dll" );

            string culture = new AssemblyName( args.Name ).CultureName;

            if ( !File.Exists( fullPath ) )
            {
                string culturePath = Path.Combine( searchPath, culture, assemblyname + ".dll" );

                if ( File.Exists( culturePath ) )
                {
                    fullPath = culturePath;
                }
                else
                {
                    continue;
                }
            }

            Assembly assembly = Assembly.LoadFrom( fullPath );

            return assembly;
        }

        return null;
    }

    public static void SetPlayer( PlayerMobile mobile )
    {
        Player = mobile;

        PlayerInitializedEvent?.Invoke( mobile );

        mobile.MobileStatusUpdated += ( status, newStatus ) =>
        {
            if ( !Options.CurrentOptions.UseDeathScreenWhilstHidden )
            {
                return;
            }

            if ( newStatus.HasFlag( MobileStatus.Hidden ) )
            {
                SendPacketToClient( new MobileUpdate( mobile.Serial, mobile.ID == 0x191 ? 0x193 : 0x192, mobile.Hue, newStatus, mobile.X, mobile.Y, mobile.Z, mobile.Direction ) );
            }
        };

        //TODO
        //Task.Run( async () =>
        //{
        //    try
        //    {
        //        GitHubClient client = new GitHubClient( new ProductHeaderValue( "ClassicAssist" ) );

        //        IReadOnlyList<Release> releases =
        //            await client.Repository.Release.GetAll( "Reetus", "ClassicAssist" );

        //        Release latestRelease = releases.FirstOrDefault();

        //        if ( latestRelease == null )
        //        {
        //            return;
        //        }

        //        Version latestVersion = Version.Parse( latestRelease.TagName );

        //        if ( !Version.TryParse(
        //            FileVersionInfo.GetVersionInfo( Path.Combine( StartupPath, "ClassicAssist.dll" ) )
        //                .ProductVersion, out Version localVersion ) )
        //        {
        //            return;
        //        }

        //        if ( latestVersion > localVersion && AssistantOptions.UpdateGumpVersion < latestVersion )
        //        {
        //            IReadOnlyList<GitHubCommit> commits =
        //                await client.Repository.Commit.GetAll( "Reetus", "ClassicAssist" );

        //            IEnumerable<GitHubCommit> latestCommits =
        //                commits.OrderByDescending( c => c.Commit.Author.Date ).Take( 15 );

        //            StringBuilder commitMessage = new StringBuilder();

        //            foreach ( GitHubCommit gitHubCommit in latestCommits )
        //            {
        //                commitMessage.AppendLine( $"{gitHubCommit.Commit.Author.Date.Date.ToShortDateString()}:" );
        //                commitMessage.AppendLine();
        //                commitMessage.AppendLine( gitHubCommit.Commit.Message );
        //                commitMessage.AppendLine();
        //            }

        //            StringBuilder message = new StringBuilder();
        //            message.AppendLine( Strings.ProductName );
        //            message.AppendLine(
        //                $"{Strings.New_version_available_} <A HREF=\"https://github.com/Reetus/ClassicAssist/releases/tag/{latestVersion}\">{latestVersion}</A>" );
        //            message.AppendLine();
        //            message.AppendLine( commitMessage.ToString() );
        //            message.AppendLine(
        //                $"<A HREF=\"https://github.com/Reetus/ClassicAssist/commits/master\">{Strings.See_More}</A>" );

        //            UpdateMessageGump gump =
        //                new UpdateMessageGump( WindowHandle, message.ToString(), latestVersion );
        //            gump.SendGump();
        //        }
        //    }
        //    catch ( Exception )
        //    {
        //        // Squash all
        //    }
        //} );

        AbilitiesManager.GetInstance().Enabled = AbilityType.None;
        AbilitiesManager.GetInstance().ResendGump( AbilityType.None );

        Task.Run( async () =>
        {
            await Task.Delay( 3000 );
            MacroManager.GetInstance().Autostart();
        } );
    }

    public static void SendPacketToServer( byte[] packet, int length )
    {
        lock ( _serverSendLock )
        {
            while ( DateTime.Now < _nextPacketSendTime )
            {
                Thread.Sleep( 1 );
            }

            InternalPacketSentEvent?.Invoke( packet, length );

            PacketWaitEntries?.CheckWait( packet, PacketDirection.Outgoing, true );

            (byte[] data, int dataLength) = Utility.CopyBuffer( packet, length );

            _sendToServer?.Invoke( ref data, ref dataLength );

            _nextPacketSendTime = DateTime.Now + PACKET_SEND_DELAY;
        }
    }

    public static void SendPacketToClient( byte[] packet, int length, bool delay = true )
    {
        try
        {
            lock ( _clientSendLock )
            {
                if ( delay )
                {
                    while ( DateTime.Now < _nextPacketRecvTime )
                    {
                        Thread.Sleep( 1 );
                    }
                }

                InternalPacketReceivedEvent?.Invoke( packet, length );

                _sendToClient?.Invoke( ref packet, ref length );

                _nextPacketRecvTime = DateTime.Now + PACKET_RECV_DELAY;
            }
        }
        catch ( ThreadInterruptedException )
        {
            // Macro was interupted whilst we were waiting...
        }
    }

    public static void SendPacketToClient( PacketWriter packet )
    {
        byte[] data = packet.ToArray();

        SendPacketToClient( data, data.Length );
    }

    public static void SendPacketToClient( BasePacket basePacket, bool delay = true )
    {
        if ( basePacket.Direction is not PacketDirection.Any and not PacketDirection.Incoming )
        {
            throw new InvalidOperationException( "Send packet wrong direction." );
        }

        byte[] data = basePacket.ToArray();

        SendPacketToClient( data, data.Length, delay );
    }

    public static void SendPacketToServer( PacketWriter packet )
    {
        byte[] data = packet.ToArray();

        SendPacketToServer( data, data.Length );
    }

    public static void SendPacketToServer( BasePacket basePacket )
    {
        if ( basePacket.Direction is not PacketDirection.Any and not PacketDirection.Outgoing )
        {
            throw new InvalidOperationException( "Send packet wrong direction." );
        }

        byte[] data = basePacket.ToArray();

        if ( data == null )
        {
            return;
        }

        basePacket.ThrottleBeforeSend();

        SendPacketToServer( data, data.Length );
    }

    public static bool Move( Direction direction, bool run )
    {
        return _requestMove?.Invoke( (int) direction, run ) ?? false;
    }

    public static void UpdateWindowTitle()
    {
        UpdateWindowTitleEvent?.Invoke();
    }

    /// <summary>
    ///     Sets the CUO client window title (via the plugin's native <c>SetTitle</c> hook) to
    ///     "PlayerName (ShardName)" when <see cref="Options.SetUOTitle" /> is enabled, or clears it
    ///     otherwise so the client falls back to its own title.
    /// </summary>
    public static void SetTitle( string title = null )
    {
        if ( Options.CurrentOptions.SetUOTitle )
        {
            Host?.SetTitle( string.IsNullOrEmpty( title )
                ? Player == null ? string.Empty : $"{Player.Name} ({CurrentShard?.Name})"
                : title );
        }
        else
        {
            Host?.SetTitle( string.Empty );
        }
    }

    public static void GetMapZ( int x, int y, out sbyte groundZ, out sbyte staticZ )
    {
        groundZ = staticZ = (sbyte) ( Player?.Z ?? 0 );

        if ( ClassicAssembly == null )
        {
            return;
        }

        PropertyInfo mapProperty = ClassicAssembly.GetType( "ClassicUO.Game.World" )?.GetProperty( "Map" );

        if ( mapProperty == null )
        {
            return;
        }

        object mapInstance = mapProperty.GetMethod.Invoke( mapProperty, null );

        MethodInfo getMapZMethod = mapInstance?.GetType().GetMethod( "GetMapZ" );

        if ( getMapZMethod == null )
        {
            return;
        }

        object[] parameters = [x, y, null, null];

        getMapZMethod.Invoke( mapInstance, parameters );

        groundZ = (sbyte) parameters[2];
        staticZ = (sbyte) parameters[3];
    }

    public static Stream GetResourceStream( string name )
    {
        return Assembly.GetAssembly( typeof( Engine ) ).GetManifestResourceStream( $"ClassicAssist.Shared.Resources.{name}" );
    }

    private static void OnTick()
    {
        try
        {
            while ( TickWorkQueue.Count > 0 )
            {
                Action action = TickWorkQueue.Dequeue();

                action?.Invoke();
            }
        }
        catch ( Exception e )
        {
            SentrySdk.CaptureException( e );
            Commands.SystemMessage( e.Message );
        }
    }

    public class PluginMethods : IPluginMethods
    {
        public void OnConnected()
        {
            if ( !Installed )
            {
                return;
            }

            Engine.OnConnected();
        }

        public void OnDisconnected()
        {
            if ( !Installed )
            {
                return;
            }

            Engine.OnDisconnected();
        }

        public Task<(bool, byte[], int)> OnPacketReceive( byte[] data, int length )
        {
            if ( !Installed )
            {
                return Task.FromResult( (true, Array.Empty<byte>(), 0) );
            }

            byte[] original = new byte[length];
            int originalLength = length;
            Array.Copy( data, original, length );

            bool result = Engine.OnPacketReceive( data, length );

            bool modified = length != originalLength || !original.SequenceEqual( data );

            return Task.FromResult( (result, modified ? data : [], modified ? length : 0) );
        }

        public Task<(bool, byte[], int)> OnPacketSend( byte[] data, int length )
        {
            if ( !Installed )
            {
                return Task.FromResult( (true, Array.Empty<byte>(), 0) );
            }

            byte[] original = new byte[length];
            int originalLength = length;
            Array.Copy( data, original, length );

            bool result = Engine.OnPacketSend( data, length );

            bool modified = length != originalLength || !original.SequenceEqual( data );

            return Task.FromResult( (result, modified ? data : [], modified ? length : 0) );
        }

        public void OnClientClosing()
        {
            if ( !Installed )
            {
                return;
            }

            Engine.OnClientClosing();
        }

        public Task<bool> OnHotkeyPressed( int key, int mod, bool pressed )
        {
            return Task.FromResult( Installed && Engine.OnHotkeyPressed( key, mod, pressed ) );
        }

        public void OnMouse( int button, int wheel )
        {
            if ( !Installed )
            {
                return;
            }

            Engine.OnMouse( button, wheel );
        }

        public void OnTick()
        {
            if ( !Installed )
            {
                return;
            }

            Engine.OnTick();
        }

        public void OnFocusChanged( bool focus )
        {
            if ( !Installed )
            {
                return;
            }

            if ( focus )
            {
                OnFocusGained();
            }
            else
            {
                OnFocusLost();
            }
        }

        public void OnPlayerPositionChanged( int x, int y, int z )
        {
            if ( !Installed )
            {
                return;
            }

            Engine.OnPlayerPositionChanged( x, y, z );
        }
    }

    public static bool CheckOutgoingPreFilter( byte[] data )
    {
        if ( _outgoingPacketPreFilter.MatchFilterAll( data, out PacketFilterInfo[] pfis ) <= 0 )
        {
            return false;
        }

        foreach ( PacketFilterInfo pfi in pfis )
        {
            pfi.Action?.Invoke( data, pfi );
        }

        SentPacketFilteredEvent?.Invoke( data, data.Length );

        PacketWaitEntries.CheckWait( data, PacketDirection.Outgoing, true );

        return true;
    }

    #region ClassicUO Events

    public static bool OnPacketSend( byte[] data, int length )
    {
        bool filter = false;

        if ( CommandsManager.IsSpeechPacket( data[0] ) )
        {
            filter = CommandsManager.CheckCommand( data, length );
        }

        if ( _outgoingPacketPreFilter.MatchFilterAll( data, out PacketFilterInfo[] pfis ) > 0 )
        {
            foreach ( PacketFilterInfo pfi in pfis )
            {
                pfi.Action?.Invoke( data, pfi );
            }

            SentPacketFilteredEvent?.Invoke( data, data.Length );

            PacketWaitEntries.CheckWait( data, PacketDirection.Outgoing, true );

            return false;
        }

        if ( OutgoingPacketFilters.CheckPacket( ref data, ref length ) )
        {
            SentPacketFilteredEvent?.Invoke( data, data.Length );

            return false;
        }

        OutgoingQueue.Enqueue( new Packet( data, length ) );

        // ReSharper disable once InvertIf
        if ( _outgoingPacketPostFilter.MatchFilterAll( data, out PacketFilterInfo[] pfisPost ) > 0 )
        {
            foreach ( PacketFilterInfo pfi in pfisPost )
            {
                pfi.Action?.Invoke( data, pfi );
            }

            SentPacketFilteredEvent?.Invoke( data, data.Length );

            PacketWaitEntries.CheckWait( data, PacketDirection.Outgoing, true );

            return false;
        }

        return !filter;
    }

    public static IntPtr WindowHandle { get; private set; }

    public static ThreadQueue<Packet> IncomingQueue { get; set; }

    public static ThreadQueue<Packet> OutgoingQueue { get; set; }

    public static bool OnPacketReceive( byte[] data, int length )
    {
        if ( _incomingPacketFilter.MatchFilterAll( data, out PacketFilterInfo[] pfis ) > 0 )
        {
            foreach ( PacketFilterInfo pfi in pfis )
            {
                pfi.Action?.Invoke( data, pfi );
            }

            ReceivedPacketFilteredEvent?.Invoke( data, length );

            PacketWaitEntries.CheckWait( data, PacketDirection.Incoming, true );

            return false;
        }

        if ( IncomingPacketFilters.CheckPacket( ref data, ref length ) )
        {
            ReceivedPacketFilteredEvent?.Invoke( data, length );

            return false;
        }

        IncomingQueue.Enqueue( new Packet( data, length ) );

        return true;
    }

    public static Direction GetSequence( int sequence )
    {
        return (Direction) Volatile.Read( ref _sequenceList[sequence] );
    }

    public static void SetSequence( int sequence, Direction direction )
    {
        _sequenceList[sequence] = (int) direction;
    }

    public static void OnConnected()
    {
        Connected = true;

        ConnectedEvent?.Invoke();
    }

    public static void OnDisconnected()
    {
        Connected = false;

        Items.Clear();
        Mobiles.Clear();
        Player = null;

        DisconnectedEvent?.Invoke();
    }

    private static void OnFocusGained()
    {
        IsClientFocused = true;
    }

    private static void OnFocusLost()
    {
        IsClientFocused = false;
    }

    private static bool SendPacketToServerPlugin( ref byte[] data, ref int length )
    {
        return Host.SendPacketToServer( data, length ).Result;
    }

    private static bool SendPacketToClientPlugin( ref byte[] data, ref int length )
    {
        return Host.SendPacketToClient( data, length ).Result;
    }

    #endregion
}