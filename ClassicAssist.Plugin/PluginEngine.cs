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
using System.Runtime.CompilerServices;
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

        // Addresses out of the client's PluginHeader, invoked through calli. See InitializePlugin for
        // why these aren't delegates.
        private static IntPtr _sendToClientNewPtr;
        private static IntPtr _sendToServerNewPtr;
        private static IntPtr _getPacketLengthPtr;
        private static IntPtr _getUOFilePathPtr;
        private static IntPtr _setTitlePtr;
        private static IntPtr _requestMovePtr;

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
            WindowHandle = plugin->HWND;

            // Everything below deliberately avoids Marshal.GetFunctionPointerForDelegate and
            // Marshal.GetDelegateForFunctionPointer, in both directions. Those two only interoperate
            // when the host and the plugin agree on the *identity* of the CUO_API delegate types, and
            // that assumption does not hold on every load path:
            //
            //   managed load (TazUO, ClassicUO.Bootstrap)  cuoapi resolves to the host's copy, so the
            //                                              pointer is a managed thunk the runtime
            //                                              unwraps straight back to the original
            //                                              delegate. Nothing is marshalled.
            //   DNNE native load (modern ClassicUO)        DNNE calls hostfxr's
            //                                              load_assembly_and_get_function_pointer,
            //                                              which always uses an
            //                                              IsolatedComponentLoadContext. We get our
            //                                              own cuoapi, so identity differs even though
            //                                              it is the same file and version.
            //
            // In that second case GetDelegateForFunctionPointer throws InvalidCastException outright,
            // and any delegate we hand back makes the host throw the same way when it reads the
            // header. Where it does not throw it silently makes things worse: the runtime falls back
            // to building a real marshalling stub, and `ref byte[]` carries no element count across
            // one, so packet buffers arrive with a length unrelated to the count beside them.
            //
            // Raw function pointers have none of that coupling - a calli is just an address and a
            // signature - so the same registration is correct on every host and every load path.
            RegisterCallbacks( plugin );
            BindHostCallbacks( plugin );

            ClientPath = GetUOFilePath();
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

        /// <summary>
        ///     Publishes our callbacks into the header as raw <see cref="UnmanagedCallersOnlyAttribute" />
        ///     pointers.
        ///     <para>
        ///         <c>OnRecv</c> / <c>OnSend</c> are deliberately left null. They take <c>ref byte[]</c>,
        ///         which is not blittable and so cannot be exposed this way at all, and a marshalled delegate
        ///         in those slots is exactly what breaks the DNNE path. Every host this plugin supports -
        ///         modern ClassicUO, ClassicUO.Bootstrap and TazUO - checks <c>OnRecv_new</c> /
        ///         <c>OnSend_new</c> first and only falls back to the old pair when they are null, so leaving
        ///         them empty costs nothing. A client predating the <c>_new</c> slots would get no packet
        ///         filtering, but such a client also has no room in its header for them (see below).
        ///     </para>
        /// </summary>
        private static unsafe void RegisterCallbacks( PluginHeader* plugin )
        {
            plugin->OnConnected = (IntPtr) (delegate* unmanaged[Cdecl]<void>) &NativeOnConnected;
            plugin->OnDisconnected = (IntPtr) (delegate* unmanaged[Cdecl]<void>) &NativeOnDisconnected;
            plugin->OnClientClosing = (IntPtr) (delegate* unmanaged[Cdecl]<void>) &NativeOnClientClosing;
            plugin->Tick = (IntPtr) (delegate* unmanaged[Cdecl]<void>) &NativeOnTick;
            plugin->OnFocusGained = (IntPtr) (delegate* unmanaged[Cdecl]<void>) &NativeOnFocusGained;
            plugin->OnFocusLost = (IntPtr) (delegate* unmanaged[Cdecl]<void>) &NativeOnFocusLost;
            plugin->OnMouse = (IntPtr) (delegate* unmanaged[Cdecl]<int, int, void>) &NativeOnMouse;
            plugin->OnPlayerPositionChanged =
                (IntPtr) (delegate* unmanaged[Cdecl]<int, int, int, void>) &NativeOnPlayerPositionChanged;

            // bool is 4-byte BOOL by default in interop, and the host declares these without any
            // MarshalAs, so int is what actually crosses.
            plugin->OnHotkeyPressed = (IntPtr) (delegate* unmanaged[Cdecl]<int, int, int, int>) &NativeOnHotkeyPressed;

            // The cuoapi PluginHeader stops at SetTitle, but every current client appends four more
            // slots. They are fixed-offset in a sequential struct of pointers, so reach them by hand:
            //
            //   184  OnRecv_new    192  OnSend_new    200  Recv_new    208  Send_new
            //
            // This assumes the header the client passed is the long form. That is true of modern
            // ClassicUO, ClassicUO.Bootstrap and TazUO; against a client old enough to pass the short
            // 184-byte header these writes would land on its stack.
            byte* raw = (byte*) plugin;

            *(IntPtr*) ( raw + 184 ) = (IntPtr) (delegate* unmanaged[Cdecl]<IntPtr, int*, byte>) &OnPacketReceiveNative;
            *(IntPtr*) ( raw + 192 ) = (IntPtr) (delegate* unmanaged[Cdecl]<IntPtr, int*, byte>) &OnPacketSendNative;
        }

        /// <summary>
        ///     Caches the host's side of the header. Stored as raw addresses and invoked through calli, for
        ///     the reasons in <see cref="InitializePlugin" />.
        /// </summary>
        private static unsafe void BindHostCallbacks( PluginHeader* plugin )
        {
            byte* raw = (byte*) plugin;

            _getPacketLengthPtr = plugin->GetPacketLength;
            _getUOFilePathPtr = plugin->GetUOFilePath;
            _requestMovePtr = plugin->RequestMove;
            _setTitlePtr = plugin->SetTitle;
            _sendToClientNewPtr = *(IntPtr*) ( raw + 200 );
            _sendToServerNewPtr = *(IntPtr*) ( raw + 208 );
        }

        [UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
        private static void NativeOnConnected()
        {
            OnConnected();
        }

        [UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
        private static void NativeOnDisconnected()
        {
            OnDisconnected();
        }

        [UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
        private static void NativeOnClientClosing()
        {
            OnClientClosing();
        }

        [UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
        private static void NativeOnTick()
        {
            OnTick();
        }

        [UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
        private static void NativeOnFocusGained()
        {
            OnFocusChanged( true );
        }

        [UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
        private static void NativeOnFocusLost()
        {
            OnFocusChanged( false );
        }

        [UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
        private static void NativeOnMouse( int button, int wheel )
        {
            OnMouse( button, wheel );
        }

        [UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
        private static void NativeOnPlayerPositionChanged( int x, int y, int z )
        {
            OnPlayerPositionChanged( x, y, z );
        }

        [UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
        private static int NativeOnHotkeyPressed( int key, int mod, int pressed )
        {
            return OnHotkeyPressed( key, mod, pressed != 0 ) ? 1 : 0;
        }

        private static unsafe short GetPacketLength( int id )
        {
            return _getPacketLengthPtr == IntPtr.Zero
                ? (short) -1
                : ( (delegate* unmanaged[Cdecl]<int, short>) _getPacketLengthPtr )( id );
        }

        private static unsafe string GetUOFilePath()
        {
            if ( _getUOFilePathPtr == IntPtr.Zero )
            {
                return null;
            }

            // Ansi, because the host declares the delegate without a CharSet. The buffer is allocated
            // by the host's return-value marshaller and is ours to free, but this runs once at startup
            // and guessing the wrong allocator would corrupt its heap, so leave it.
            return Marshal.PtrToStringAnsi( ( (delegate* unmanaged[Cdecl]<IntPtr>) _getUOFilePathPtr )() );
        }

        private static unsafe bool RequestMove( int dir, bool run )
        {
            return _requestMovePtr != IntPtr.Zero &&
                   ( (delegate* unmanaged[Cdecl]<int, int, int>) _requestMovePtr )( dir, run ? 1 : 0 ) != 0;
        }

        private static unsafe void SetTitle( string title )
        {
            if ( _setTitlePtr == IntPtr.Zero )
            {
                return;
            }

            IntPtr ptr = Marshal.StringToHGlobalAnsi( title );

            try
            {
                ( (delegate* unmanaged[Cdecl]<IntPtr, void>) _setTitlePtr )( ptr );
            }
            finally
            {
                Marshal.FreeHGlobal( ptr );
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

        private static readonly HashSet<string> _warned = new HashSet<string>();

        /// <summary>
        ///     Logs a given message once. These fire from the packet path, which runs for every packet.
        /// </summary>
        private static void WarnOnce( string message )
        {
            lock ( _warned )
            {
                if ( !_warned.Add( message ) )
                {
                    return;
                }
            }

            Console.WriteLine( $"ClassicAssist: {message}" );
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

        /// <summary>
        ///     Outgoing half of the OnSend_new / OnRecv_new pair. See <see cref="FilterPacketNative" /> for why
        ///     these exist at all.
        /// </summary>
        [UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
        private static unsafe byte OnPacketSendNative( IntPtr data, int* length )
        {
            return FilterPacketNative( data, length,
                ( plugin, buffer ) => plugin.OnPacketSend( buffer, buffer.Length ) );
        }

        [UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
        private static unsafe byte OnPacketReceiveNative( IntPtr data, int* length )
        {
            return FilterPacketNative( data, length,
                ( plugin, buffer ) => plugin.OnPacketReceive( buffer, buffer.Length ) );
        }

        /// <summary>
        ///     The packet callbacks the host actually prefers. The host declares them as
        ///     <c>bool(byte[] data, ref int length)</c>, but we register them as raw
        ///     <see cref="UnmanagedCallersOnlyAttribute" /> pointers rather than as marshalled delegates, and
        ///     that difference is the whole point.
        ///     <para>
        ///         The old <c>OnRecv</c> / <c>OnSend</c> pair takes <c>ref byte[]</c>. That only ever worked
        ///         because the host and the plugin shared one CUO_API identity, so
        ///         <c>Marshal.GetDelegateForFunctionPointer</c> handed the original delegate straight back and
        ///         no marshalling happened. Under DNNE the plugin lives in an IsolatedComponentLoadContext with
        ///         its own copy of cuoapi, identity differs, and the runtime builds a real marshalling stub -
        ///         at which point <c>ref byte[]</c> arrives carrying no element count and the array bears no
        ///         relation to <c>length</c>.
        ///     </para>
        ///     <para>
        ///         Taking the buffer as a pointer plus an explicit count sidesteps that entirely: nothing about
        ///         the signature depends on shared type identity, so the same registration is correct on both
        ///         load paths. The host pins its array for the duration of the call, so writing through
        ///         <paramref name="data" /> updates the caller's buffer in place - but only up to the length it
        ///         handed us, since that is all it allocated.
        ///     </para>
        /// </summary>
        private static unsafe byte FilterPacketNative( IntPtr data, int* length,
            Func<IPluginMethods, byte[], Task<(bool, byte[], int)>> call )
        {
            IPluginMethods plugin = _plugin;

            if ( plugin == null || data == IntPtr.Zero || length == null || *length <= 0 )
            {
                return 1;
            }

            int capacity = *length;
            byte[] buffer = new byte[capacity];

            Marshal.Copy( data, buffer, 0, capacity );

            ( bool result, byte[] newPacket, int newLength ) = Filter( plugin, buffer, call );

            if ( newPacket == null || newLength <= 0 || newLength > capacity )
            {
                return result ? (byte) 1 : (byte) 0;
            }

            Marshal.Copy( newPacket, 0, data, newLength );
            *length = newLength;

            return result ? (byte) 1 : (byte) 0;
        }

        /// <summary>
        ///     Asks the UI process what to do with a packet. Never throws: any failure means the UI can't
        ///     answer, and the packet should go through untouched rather than take the client down with it.
        /// </summary>
        /// <returns>
        ///     Whether to let the packet through, plus the rewritten packet, if any. A null or empty rewrite
        ///     means leave the buffer alone. The rewrite is not length-checked here - each caller knows how
        ///     much room its own buffer has.
        /// </returns>
        private static (bool, byte[], int) Filter( IPluginMethods plugin, byte[] buffer,
            Func<IPluginMethods, byte[], Task<(bool, byte[], int)>> call )
        {
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

                return ( true, null, 0 );
            }

            if ( !result )
            {
                return ( false, null, 0 );
            }

            // The UI process may return a buffer whose length doesn't match newLength (JSON-RPC
            // serialisation quirk). Clamp to what it actually sent.
            if ( newPacket != null && newLength > newPacket.Length )
            {
                newLength = newPacket.Length;
            }

            return ( true, newPacket, newLength );
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
            public unsafe Task<bool> SendPacketToServer( byte[] packet, int length )
            {
                int len = length;

                if ( _sendToServerNewPtr != IntPtr.Zero )
                {
                    fixed ( byte* ptr = packet )
                    {
                        // byte, not bool: the host declares these [return: MarshalAs(UnmanagedType.I1)].
                        ( (delegate* unmanaged[Cdecl]<IntPtr, ref int, byte>)_sendToServerNewPtr )(
                            (IntPtr) ptr, ref len );
                    }
                }
                else
                {
                    WarnOnce( "client did not provide Send_new; outgoing packets cannot be injected." );
                }

                return Task.FromResult( true );
            }

            public unsafe Task<bool> SendPacketToClient( byte[] packet, int length )
            {
                int len = length;

                if ( _sendToClientNewPtr != IntPtr.Zero )
                {
                    fixed ( byte* ptr = packet )
                    {
                        // byte, not bool: the host declares these [return: MarshalAs(UnmanagedType.I1)].
                        ( (delegate* unmanaged[Cdecl]<IntPtr, ref int, byte>)_sendToClientNewPtr )(
                            (IntPtr) ptr, ref len );
                    }
                }
                else
                {
                    WarnOnce( "client did not provide Recv_new; incoming packets cannot be injected." );
                }

                return Task.FromResult( true );
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
                return Task.FromResult( PluginEngine.GetPacketLength( id ) );
            }

            public Task<string> GetUOFilePath()
            {
                return Task.FromResult( PluginEngine.GetUOFilePath() );
            }

            public Task<bool> RequestMove( int dir, bool run )
            {
                return Task.FromResult( PluginEngine.RequestMove( dir, run ) );
            }

            public void SetTitle( string title )
            {
                PluginEngine.SetTitle( title );
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
            RequestMove( subCode, b );
        }
    }
}