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
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using Avalonia;
using ClassicAssist.Plugin.Shared;
using StreamJsonRpc;

namespace ClassicAssist.Avalonia
{
    internal class Program
    {
        private const int CONNECT_TIMEOUT_MS = 30000;

        /// <summary>
        ///     The RPC connection to the plugin, established before Avalonia starts so that
        ///     <see cref="App.OnFrameworkInitializationCompleted" /> can install the engine as soon as the
        ///     dispatcher exists.
        /// </summary>
        internal static IHostMethods Host { get; private set; }

        internal static JsonRpc Rpc { get; private set; }

        // Don't use any Avalonia, third-party APIs or any SynchronizationContext-reliant code before
        // AppMain is called: things aren't initialized yet and stuff might break.
        [STAThread]
        public static void Main( string[] args )
        {
            if ( args == null || args.Length == 0 )
            {
                Console.Error.WriteLine(
                    "ClassicAssist UI expects the plugin's endpoint as its first argument." );

                return;
            }

            Stream stream = Connect( args[0] );

            if ( stream == null )
            {
                return;
            }

            Rpc = JsonRpc.Attach( stream, new Shared.Engine.PluginMethods() );
            Host = Rpc.Attach<IHostMethods>();

            // Group the assistant window with the game window into one taskbar button on Windows.
            // The id is keyed on the game run's process id (queried over RPC) so that each game
            // instance + assistant pair stays its own group when multiboxing.
            int gameProcessId = Host.GetProcessId().GetAwaiter().GetResult();

            ClassicAssist.Shared.NativeMethods.SetAppUserModelId( gameProcessId );

            // If the game process goes away, so do we - there is nothing left to assist.
            Rpc.Disconnected += ( _, _ ) => Environment.Exit( 0 );

            // Avalonia on Linux requires the UI to own the process main thread, which is the entire
            // reason this runs as a separate process rather than inside the plugin. Blocking call.
            BuildAvaloniaApp().StartWithClassicDesktopLifetime( args );
        }

        /// <summary>
        ///     Opens the connection back to the plugin.
        ///     <para>
        ///         Two forms, because the plugin has two builds. "tcp:port:token" is the .NET Framework one
        ///         loaded by the Mono-based legacy client: Mono's named pipes are not the same thing as
        ///         .NET's on Unix, so a pipe created there is not connectable from here. Anything else is a
        ///         pipe name, which is what the modern build still uses.
        ///     </para>
        /// </summary>
        private static Stream Connect( string endpoint )
        {
            if ( !endpoint.StartsWith( "tcp:", StringComparison.Ordinal ) )
            {
                NamedPipeClientStream pipe =
                    new NamedPipeClientStream( ".", endpoint, PipeDirection.InOut, PipeOptions.Asynchronous );

                try
                {
                    pipe.Connect( CONNECT_TIMEOUT_MS );

                    return pipe;
                }
                catch ( Exception e )
                {
                    Console.Error.WriteLine( $"Couldn't connect to plugin pipe '{endpoint}': {e.Message}" );

                    return null;
                }
            }

            string[] parts = endpoint.Split( new[] { ':' }, 3 );

            if ( parts.Length != 3 || !int.TryParse( parts[1], out int port ) )
            {
                Console.Error.WriteLine( $"Malformed endpoint '{endpoint}'." );

                return null;
            }

            try
            {
                TcpClient client = new TcpClient();

                client.Connect( IPAddress.Loopback, port );
                client.NoDelay = true;

                NetworkStream stream = client.GetStream();

                // The listener is on loopback, so anything else on this machine could reach it. Prove we
                // are the process the plugin started before it will talk to us.
                byte[] token = System.Text.Encoding.ASCII.GetBytes( parts[2] + "\n" );

                stream.Write( token, 0, token.Length );
                stream.Flush();

                return stream;
            }
            catch ( Exception e )
            {
                Console.Error.WriteLine( $"Couldn't connect to the plugin on port {port}: {e.Message}" );

                return null;
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>().UsePlatformDetect();
        }
    }
}
