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
using System.IO.Pipes;
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
                Console.Error.WriteLine( "ClassicAssist UI expects the plugin's pipe name as its first argument." );

                return;
            }

            string pipeName = args[0];

            NamedPipeClientStream clientStream =
                new NamedPipeClientStream( ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous );

            try
            {
                clientStream.Connect( CONNECT_TIMEOUT_MS );
            }
            catch ( Exception e )
            {
                Console.Error.WriteLine( $"Couldn't connect to plugin pipe '{pipeName}': {e.Message}" );

                return;
            }

            Rpc = JsonRpc.Attach( clientStream, new Shared.Engine.PluginMethods() );
            Host = Rpc.Attach<IHostMethods>();

            // If the game process goes away, so do we - there is nothing left to assist.
            Rpc.Disconnected += ( _, _ ) => Environment.Exit( 0 );

            // Avalonia on Linux requires the UI to own the process main thread, which is the entire
            // reason this runs as a separate process rather than inside the plugin. Blocking call.
            BuildAvaloniaApp().StartWithClassicDesktopLifetime( args );
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>().UsePlatformDetect();
        }
    }
}
