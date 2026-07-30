#region License

// Copyright (C) 2025 Reetus
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
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Plugin.Shared.Reflection;
using ClassicAssist.Plugin.Shared.Reflection.ClassicUO.Objects;
using ClassicAssist.Shared;
using CUO_API;
using StreamJsonRpc;

// ReSharper disable once CheckNamespace
namespace ClassicAssist.Plugin
{
    /// <summary>
    ///     The plugin implementation. Reached only via <see cref="Assistant.Engine" />, which registers the
    ///     assembly resolver before anything in here is touched.
    /// </summary>
    public static class PluginEngine
    {
        public static Queue<Action> TickWorkQueue { get; set; } = new Queue<Action>();
        
        private static IPluginMethods _plugin;
        private static HostMethods _hostMethods;
        private static OnConnected _onConnected;
        private static OnDisconnected _onDisconnected;
        private static OnPacketSendRecv _onReceive;
        private static OnPacketSendRecv _onSend;
        private static OnTick _onTick;
        private static OnGetUOFilePath _getUOFilePath;
        private static OnPacketSendRecv _sendToClient;
        private static OnPacketSendRecv _sendToServer;
        private static OnGetPacketLength _getPacketLength;
        private static OnUpdatePlayerPosition _onPlayerPositionChanged;
        private static OnSetTitle _setTitle;
        private static OnClientClose _onClientClosing;
        private static OnHotkey _onHotkeyPressed;
        private static RequestMove _requestMove;
        private static OnMouse _onMouse;
        private static OnFocusGained _onFocusGained;
        private static OnFocusLost _onFocusLost;

        public static AutoResetEvent ShutdownResetEvent { get; } = new AutoResetEvent( false );

        public static Assembly ClassicAssembly { get; set; }

        public static string ClientPath { get; set; }

        public static Version ClientVersion { get; set; }

        public static string StartupPath { get; set; }

        public static IntPtr WindowHandle { get; set; }

        internal static unsafe void Install( IntPtr header )
        {
            PluginHeader* plugin = (PluginHeader*) header;

            InitializePlugin( plugin );

            ClassicAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( a => a.FullName.StartsWith( "ClassicUO," ) || a.FullName.StartsWith( "TazUO," ) );
            
            if ( ClassicAssembly != null )
            {
                CUOPath = Path.GetDirectoryName( ClassicAssembly.Location );
            }

            LaunchUI();
        }

        public static string CUOPath { get; set; }

        private static unsafe void InitializePlugin( PluginHeader* plugin )
        {
            _onConnected = OnConnected;
            _onDisconnected = OnDisconnected;
            _onReceive = OnPacketReceive;
            _onSend = OnPacketSend;
            _onPlayerPositionChanged = OnPlayerPositionChanged;
            _onClientClosing = OnClientClosing;
            _onHotkeyPressed = OnHotkeyPressed;
            _onMouse = OnMouse;
            _onTick = OnTick;
            _onFocusGained = () => OnFocusChanged( true );
            _onFocusLost = () => OnFocusChanged( false );
            WindowHandle = plugin->HWND;

            plugin->OnConnected = Marshal.GetFunctionPointerForDelegate( _onConnected );
            plugin->OnDisconnected = Marshal.GetFunctionPointerForDelegate( _onDisconnected );
            plugin->OnRecv = Marshal.GetFunctionPointerForDelegate( _onReceive );
            plugin->OnSend = Marshal.GetFunctionPointerForDelegate( _onSend );
            plugin->OnPlayerPositionChanged = Marshal.GetFunctionPointerForDelegate( _onPlayerPositionChanged );
            plugin->OnClientClosing = Marshal.GetFunctionPointerForDelegate( _onClientClosing );
            plugin->OnHotkeyPressed = Marshal.GetFunctionPointerForDelegate( _onHotkeyPressed );
            plugin->OnMouse = Marshal.GetFunctionPointerForDelegate( _onMouse );
            plugin->Tick = Marshal.GetFunctionPointerForDelegate( _onTick );
            plugin->OnFocusGained = Marshal.GetFunctionPointerForDelegate( _onFocusGained );
            plugin->OnFocusLost = Marshal.GetFunctionPointerForDelegate( _onFocusLost );

            _getPacketLength = Marshal.GetDelegateForFunctionPointer<OnGetPacketLength>( plugin->GetPacketLength );
            _getUOFilePath = Marshal.GetDelegateForFunctionPointer<OnGetUOFilePath>( plugin->GetUOFilePath );
            _sendToClient = Marshal.GetDelegateForFunctionPointer<OnPacketSendRecv>( plugin->Recv );
            _sendToServer = Marshal.GetDelegateForFunctionPointer<OnPacketSendRecv>( plugin->Send );
            _requestMove = Marshal.GetDelegateForFunctionPointer<RequestMove>( plugin->RequestMove );
            _setTitle = Marshal.GetDelegateForFunctionPointer<OnSetTitle>( plugin->SetTitle );

            ClientPath = _getUOFilePath();
            ClientVersion = new Version( (byte) ( plugin->ClientVersion >> 24 ), (byte) ( plugin->ClientVersion >> 16 ), (byte) ( plugin->ClientVersion >> 8 ),
                (byte) plugin->ClientVersion );

            if ( !Path.IsPathRooted( ClientPath ) )
            {
                ClientPath = Path.GetFullPath( ClientPath );
            }

            StartupPath = Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location );

            if ( StartupPath == null )
            {
                throw new InvalidOperationException();
            }
        }

        private const int UI_CONNECT_TIMEOUT_MS = 30000;
        private const int SHUTDOWN_TIMEOUT_MS = 5000;

        /// <summary>
        ///     Locates the UI apphost, which is deployed alongside the plugin in a "ui" subfolder so its
        ///     dependency closure can't collide with the assemblies the game has already loaded.
        /// </summary>
        private static string GetUIPath()
        {
            string fileName = RuntimeInformation.IsOSPlatform( OSPlatform.Windows )
                ? "ClassicAssist.Avalonia.exe"
                : "ClassicAssist.Avalonia";

            string[] candidates = { Path.Combine( StartupPath, "ui", fileName ), Path.Combine( StartupPath, fileName ) };

            return Array.Find( candidates, File.Exists );
        }

        /// <summary>
        ///     Starts the UI process and waits for it to attach, entirely off the thread that called Install.
        ///     The game is mid-startup here; blocking it on a child process that may never appear would hang
        ///     the client rather than merely leave it without an assistant.
        /// </summary>
        private static void LaunchUI()
        {
            ReflectionImpl.Initialize( ClassicAssembly, CUOPath, TickWorkQueue, Move );

            string exePath = GetUIPath();

            if ( exePath == null )
            {
                Console.WriteLine( "ClassicAssist: couldn't find the UI executable, plugin will stay idle." );

                return;
            }

#if NET
            string pipeName = $"CAPlugin_{Environment.ProcessId}";
#else
            string pipeName = $"CAPlugin_{Process.GetCurrentProcess().Id}";
#endif

            NamedPipeServerStream pipe =
                new NamedPipeServerStream( pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous );

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName( exePath ),
                Arguments = pipeName,
                UseShellExecute = false
            };

            Process uiProcess;

            try
            {
                uiProcess = Process.Start( startInfo );
            }
            catch ( Exception e )
            {
                Console.WriteLine( $"ClassicAssist: couldn't start the UI process: {e.Message}" );
                pipe.Dispose();

                return;
            }

            Task.Run( async () =>
            {
                try
                {
                    await pipe.WaitForConnectionAsync( new CancellationTokenSource( UI_CONNECT_TIMEOUT_MS ).Token );

                    _hostMethods = new HostMethods();

                    JsonRpc rpc = JsonRpc.Attach( pipe, _hostMethods );
                    rpc.Disconnected += ( _, _ ) => Detach();

                    _plugin = rpc.Attach<IPluginMethods>();
                }
                catch ( Exception e )
                {
                    Console.WriteLine( $"ClassicAssist: UI didn't attach: {e.Message}" );

                    Detach();
                    pipe.Dispose();

                    try
                    {
                        if ( uiProcess is { HasExited: false } )
                        {
                            uiProcess.Kill();
                        }
                    }
                    catch ( Exception )
                    {
                        // Nothing useful to do if it's already gone.
                    }
                }
            } );
        }

        /// <summary>
        ///     Drops the UI connection so the game keeps running unassisted rather than blocking forever on
        ///     RPC calls to a process that has gone away.
        /// </summary>
        private static void Detach()
        {
            _plugin = null;
            ShutdownResetEvent.Set();
        }

        private static void OnFocusChanged( bool focus )
        {
            _plugin?.OnFocusChanged( focus );
        }

        private static void OnTick()
        {
            while ( TickWorkQueue.Count > 0 )
            {
                Action action = TickWorkQueue.Dequeue();

                action?.Invoke();
            }
            
            _plugin?.OnTick();
        }

        private static void OnMouse( int button, int wheel )
        {
            _plugin?.OnMouse( button, wheel );
        }

        private static bool OnHotkeyPressed( int key, int mod, bool pressed )
        {
            IPluginMethods plugin = _plugin;

            if ( plugin == null )
            {
                return false;
            }

            try
            {
                return plugin.OnHotkeyPressed( key, mod, pressed ).Result;
            }
            catch ( Exception e )
            {
                OnRpcException( e, nameof( OnHotkeyPressed ) );

                return false;
            }
        }

        /// <summary>
        ///     Decides whether a failed RPC call means the UI is gone or merely that a handler threw.
        ///     Detaching on the latter would silently disable the assistant for the rest of the session after
        ///     a single bad packet, with the UI still on screen looking healthy.
        /// </summary>
        private static void OnRpcException( Exception e, string call )
        {
            Exception inner = e is AggregateException aggregate ? aggregate.GetBaseException() : e;

            // The UI handler threw. Its problem, not the connection's - stay attached.
            if ( inner is RemoteInvocationException || inner is RemoteMethodNotFoundException ||
                 inner is RemoteRpcException && !( inner is ConnectionLostException ) )
            {
                Console.WriteLine( $"ClassicAssist: {call} failed in the UI: {inner.Message}" );

                return;
            }

            Console.WriteLine( $"ClassicAssist: lost the UI connection during {call}: {inner.Message}" );

            Detach();
        }

        private static void OnClientClosing()
        {
            if ( _plugin == null )
            {
                return;
            }

            try
            {
                _plugin.OnClientClosing();

                // Give the UI a moment to persist profiles/options, but never hold the client's shutdown
                // hostage if it has already died.
                ShutdownResetEvent.WaitOne( SHUTDOWN_TIMEOUT_MS );
            }
            catch ( Exception )
            {
                // UI is gone; nothing left to save.
            }
        }

        private static void OnPlayerPositionChanged( int x, int y, int z )
        {
            _plugin?.OnPlayerPositionChanged( x, y, z );
        }

        private static bool OnPacketSend( ref byte[] data, ref int length )
        {
            return FilterPacket( ref data, ref length, ( plugin, buffer ) => plugin.OnPacketSend( buffer, buffer.Length ) );
        }

        private static bool OnPacketReceive( ref byte[] data, ref int length )
        {
            return FilterPacket( ref data, ref length, ( plugin, buffer ) => plugin.OnPacketReceive( buffer, buffer.Length ) );
        }

        /// <summary>
        ///     Round-trips a packet through the UI process and applies any rewrite it asks for. Returning true
        ///     lets the packet through unchanged, which is what we want whenever the UI can't answer.
        /// </summary>
        private static bool FilterPacket( ref byte[] data, ref int length,
            Func<IPluginMethods, byte[], Task<(bool, byte[], int)>> call )
        {
            IPluginMethods plugin = _plugin;

            if ( plugin == null )
            {
                return true;
            }

            byte[] buffer = new byte[length];
            Buffer.BlockCopy( data, 0, buffer, 0, length );

            bool result;
            byte[] newPacket;
            int newLength;

            try
            {
                ( result, newPacket, newLength ) = call( plugin, buffer ).Result;
            }
            catch ( Exception e )
            {
                OnRpcException( e, $"packet filter (0x{buffer[0]:X2})" );

                return true;
            }

            if ( newLength == 0 || !result )
            {
                return result;
            }

            // ClassicUO copies our buffer back into a fixed-size array of its own, so a packet that grew
            // past the original allocation can't be handed back.
            if ( newLength > data.Length )
            {
                return result;
            }

            length = newLength;
            Buffer.BlockCopy( newPacket, 0, data, 0, length );

            return result;
        }

        private static void OnDisconnected()
        {
            _plugin?.OnDisconnected();
        }

        private static void OnConnected()
        {
            _plugin?.OnConnected();
        }

        public class HostMethods : IHostMethods
        {
            public Task<bool> SendPacketToServer( byte[] packet, int length )
            {
                return Task.FromResult( _sendToServer( ref packet, ref length ) );
            }

            public Task<bool> SendPacketToClient( byte[] packet, int length )
            {
                return Task.FromResult( _sendToClient( ref packet, ref length ) );
            }

            public Task<string> GetClientPath()
            {
                return Task.FromResult( ClientPath );
            }

            public Task<Version> GetClientVersion()
            {
                return Task.FromResult( ClientVersion );
            }

            public Task<short> GetPacketLength( int id )
            {
                return Task.FromResult( _getPacketLength( id ) );
            }

            public Task<string> GetUOFilePath()
            {
                return Task.FromResult( _getUOFilePath() );
            }

            public Task<bool> RequestMove( int dir, bool run )
            {
                return Task.FromResult( _requestMove( dir, run ) );
            }

            public void SetTitle( string title )
            {
                _setTitle( title );
            }

            public Task<(int x, int y)> GetGumpPosition( uint id )
            {
                return Task.FromResult( ReflectionImpl.GetGumpPosition( id ) );
            }

            public Task<bool> WalkTo( int x, int y, int z, int distance )
            {
                return Task.FromResult( Pathfinder.WalkTo( x, y, z, distance ) );
            }

            public Task<bool> Pathfinding()
            {
                return Task.FromResult( Pathfinder.AutoWalking );
            }

            public void CancelPathfinding()
            {
                Pathfinder.Cancel();
            }

            public Task<IntPtr> GetWindowHandle()
            {
                return Task.FromResult( WindowHandle );
            }

            public void CreateMacroButton( string name, string value )
            {
                ReflectionImpl.CreateMacroButton( name, value );
            }

            public Task<Point> GetGameWindowCenter()
            {
                return Task.FromResult( ReflectionImpl.GetGameWindowCenter() );
            }

            public Task<bool> UsePrimaryAbility()
            {
                return Task.FromResult( GameActions.UsePrimaryAbility() );
            }

            public Task<bool> UseSecondaryAbility()
            {
                return Task.FromResult( GameActions.UseSecondaryAbility() );
            }

            public Task<bool> Following()
            {
                return Task.FromResult( ReflectionImpl.Following() );
            }

            public void Logout()
            {
                ReflectionImpl.Logout();
            }

            public void Quit()
            {
                ReflectionImpl.Quit();
            }

            public void AddMapMarker( string name, int x, int y, int facet, int zoomLevel, string iconName )
            {
                WorldMapGump.AddMarker( name, x, y, facet, zoomLevel, iconName );
            }

            public Task<bool> Follow( int serial )
            {
                return Task.FromResult( ReflectionImpl.Follow( serial ) );
            }

            public void PlayCUOMacro( string name )
            {
                ReflectionImpl.PlayCUOMacro( name );
            }

            public Task<bool> HasDisconnectedGump()
            {
                return Task.FromResult( ReflectionImpl.HasDisconnectedGump() );
            }

            public void OnShutdown()
            {
                ShutdownResetEvent.Set();
            }
        }

        public static void Move( int subCode, bool b )
        {
            _requestMove(subCode, b );
        }
    }
}