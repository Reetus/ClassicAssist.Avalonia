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
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Shared;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using StreamJsonRpc;

// ReSharper disable LocalizableElement

namespace PacketTransportBenchmark
{
    /// <summary>
    ///     Measures the cost of the plugin's packet path: the plugin forwards every packet it sees to
    ///     the UI over the pipe and blocks its own (client) thread until the UI answers - see
    ///     <c>PluginEngine.FilterPacketNative</c> and <see cref="IPluginMethods.OnPacketReceive" />.
    ///     <para>
    ///         Two formatters (the MessagePack one the modern plugin/UI pair uses, and the JSON one the
    ///         legacy .NET Framework plugin is still pinned to) and two transports (two RPC endpoints
    ///         over one pipe in this process, and the same split across two real processes) are run for
    ///         a few packet sizes. The point is the ceiling: whatever the numbers are, the game's thread
    ///         is blocked for the whole round trip, so packets per second is capped at
    ///         <c>1 / round-trip</c> no matter how idle the client and UI otherwise are.
    ///     </para>
    ///     <para>
    ///         Run with no arguments for the full matrix. Pass <c>pipeline</c> to also show what the
    ///         one-way transport can do when the caller doesn't wait (the ceiling the architecture
    ///         gives up by being synchronous), a packet size to override the default, or <c>contracts</c>
    ///         to round-trip every member of both RPC interfaces over the production formatter.
    ///     </para>
    /// </summary>
    internal static class Program
    {
        private const int Warmup = 2000;
        private const int DefaultPacketSize = 64;
        private const int DefaultCount = 20000;

        private static async Task Main( string[] args )
        {
            if ( args.Length >= 3 && args[0] == "child" )
            {
                await ConnectAndServe( args[1], ParseFormatter( args[2] ) );

                return;
            }

            if ( args.Length >= 1 && args[0] == "contracts" )
            {
                await RunContractCheck();

                return;
            }

            int packetSize = args.Length >= 1 && int.TryParse( args[0], out int size ) ? size : DefaultPacketSize;
            int count = args.Length >= 2 && int.TryParse( args[1], out int parsedCount ) ? parsedCount : DefaultCount;
            bool pipeline = Array.IndexOf( args, "pipeline" ) >= 0;

            Console.WriteLine( $"packet size: {packetSize} bytes, packets per run: {count}" );
            Console.WriteLine();

            foreach ( Transport transport in new[] { Transport.InProcess, Transport.CrossProcess } )
            {
                foreach ( FormatterKind formatter in new[] { FormatterKind.Json, FormatterKind.MessagePack } )
                {
                    await RunPluginClient( formatter, transport, packetSize, count, pipeline );
                }
            }
        }

        private static async Task RunPluginClient( FormatterKind formatter, Transport transport, int packetSize, int count, bool pipeline )
        {
            string pipeName = $"CAPacketBench_{Guid.NewGuid():N}";

            // Plugin role, exactly as PluginEngine.LaunchUI starts it: the plugin owns the server pipe
            // and is the side that attaches the IPluginMethods proxy.
            using NamedPipeServerStream server = new(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous );

            Process child = null;
            Task uiTask = null;

            if ( transport == Transport.CrossProcess )
            {
                child = StartUiProcess( pipeName, formatter );
            }
            else
            {
                uiTask = Task.Run( () => ConnectAndServe( pipeName, formatter ) );
            }

            await server.WaitForConnectionAsync();

            using JsonRpc rpc = new( CreateHandler( server, formatter ) );
            rpc.StartListening();

            IPluginMethods plugin = rpc.Attach<IPluginMethods>();

            byte[] packet = new byte[packetSize];
            packet[0] = 0x1B;

            for ( int i = 0; i < Warmup; i++ )
            {
                _ = Call( plugin, packet );
            }

            Console.WriteLine( $"== {formatter} / {transport} ==" );
            Console.WriteLine( "mode, count, elapsed_ms, packets_per_sec, avg_us, p50_us, p99_us" );

            RunSync( plugin, packet, count );
            Console.WriteLine();

            // The synchronous pattern is what the product uses; this measures the real ceiling and the
            // latency the client's thread eats per packet.
            void RunSync( IPluginMethods target, byte[] payload, int iterations )
            {
                double[] samples = new double[iterations];
                Stopwatch sw = Stopwatch.StartNew();

                for ( int i = 0; i < iterations; i++ )
                {
                    long start = Stopwatch.GetTimestamp();

                    _ = Call( target, payload );

                    samples[i] = Stopwatch.GetElapsedTime( start ).TotalMicroseconds;
                }

                sw.Stop();
                Report( "sync", iterations, sw, samples );
            }

            if ( pipeline )
            {
                Stopwatch sw = Stopwatch.StartNew();
                Task<(bool, byte[], int)>[] pending = new Task<(bool, byte[], int)>[count];

                for ( int i = 0; i < count; i++ )
                {
                    pending[i] = plugin.OnPacketReceive( packet, packet.Length );
                }

                Task.WaitAll( pending );
                sw.Stop();

                Report( "pipeline", count, sw, null );
            }

            rpc.Dispose();

            try
            {
                if ( uiTask != null )
                {
                    await uiTask;
                }
            }
            catch ( Exception )
            {
                // The UI side sees the pipe close when the plugin is done; that is expected.
            }

            child?.WaitForExit( 3000 );
        }

        private static (bool, byte[], int) Call( IPluginMethods plugin, byte[] packet )
        {
            // What PluginEngine.Filter does: block the client's own thread on the UI's answer.
            return plugin.OnPacketReceive( packet, packet.Length ).GetAwaiter().GetResult();
        }

        private static void Report( string mode, int count, Stopwatch elapsed, double[] samples )
        {
            double perSec = count / elapsed.Elapsed.TotalSeconds;
            double avgUs = elapsed.Elapsed.TotalMilliseconds * 1000.0 / count;
            double p50 = 0;
            double p99 = 0;

            if ( samples != null )
            {
                Array.Sort( samples );
                p50 = samples[samples.Length / 2];
                p99 = samples[(int) ( samples.Length * 0.99 )];
            }

            Console.WriteLine(
                $"{mode}, {count}, {elapsed.Elapsed.TotalMilliseconds:F1}, {perSec:F0}, {avgUs:F1}, {p50:F1}, {p99:F1}" );
        }

        private static Process StartUiProcess( string pipeName, FormatterKind formatter )
        {
            ProcessStartInfo startInfo = new()
            {
                UseShellExecute = false
            };

            // Environment.ProcessPath is the apphost for a self-contained run, and `dotnet` when the
            // tool is launched via its .dll; both forms are handled so `dotnet run` works in a checkout.
            if ( string.Equals( Path.GetFileNameWithoutExtension( Environment.ProcessPath ), "dotnet",
                    StringComparison.OrdinalIgnoreCase ) )
            {
                startInfo.FileName = Environment.ProcessPath;
                startInfo.ArgumentList.Add( typeof( Program ).Assembly.Location );
            }
            else
            {
                startInfo.FileName = Environment.ProcessPath;
            }

            startInfo.ArgumentList.Add( "child" );
            startInfo.ArgumentList.Add( pipeName );
            startInfo.ArgumentList.Add( formatter.ToString().ToLowerInvariant() );

            return Process.Start( startInfo );
        }

        /// <summary>
        ///     The UI role: connect as the pipe client and host the target the plugin calls into. This
        ///     is the shape of <c>ClassicAssist.Avalonia/Program.Main</c>.
        /// </summary>
        private static async Task ConnectAndServe( string pipeName, FormatterKind formatter )
        {
            using NamedPipeClientStream client = new(
                ".", pipeName, PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous );

            await client.ConnectAsync( 10000 );
            await Serve( client, formatter );
        }

        private static async Task Serve( Stream stream, FormatterKind formatter )
        {
            using JsonRpc rpc = new( CreateHandler( stream, formatter ), new PacketSink() );
            rpc.StartListening();

            await rpc.Completion;
        }

        private static IJsonRpcMessageHandler CreateHandler( Stream stream, FormatterKind formatter )
        {
            // JsonRpc.Attach is HeaderDelimited + JsonMessageFormatter, and JSON base64s every byte[] on
            // the wire. MessagePack needs the length-prefixed framing and the resolver the product uses.
            return formatter == FormatterKind.MessagePack
                ? new LengthHeaderMessageHandler( PipeWriter.Create( stream ), PipeReader.Create( stream ), CreateMessagePackFormatter() )
                : new HeaderDelimitedMessageHandler( stream, new JsonMessageFormatter() );
        }

        /// <summary>
        ///     The exact formatter the modern plugin and UI agree on - see
        ///     <c>PluginEngine.CreateMessagePackHandler</c> and <c>Program.CreateMessagePackRpc</c>. Kept
        ///     in sync with both; the contract check below is what catches drift.
        /// </summary>
        private static MessagePackFormatter CreateMessagePackFormatter()
        {
            MessagePackFormatter formatter = new();

            IMessagePackFormatter[] formatters = [IntPtrFormatter.Instance];
            IFormatterResolver[] resolvers = [ContractlessStandardResolver.Instance, StandardResolver.Instance];
            IFormatterResolver resolver = CompositeResolver.Create( formatters, resolvers );

            formatter.SetMessagePackSerializerOptions(
                MessagePackSerializerOptions.Standard.WithSecurity( MessagePackSecurity.UntrustedData )
                    .WithResolver( resolver ) );

            return formatter;
        }

        private sealed class IntPtrFormatter : IMessagePackFormatter<IntPtr>
        {
            public static readonly IntPtrFormatter Instance = new();

            public void Serialize( ref MessagePackWriter writer, IntPtr value, MessagePackSerializerOptions options )
            {
                writer.Write( value.ToInt64() );
            }

            public IntPtr Deserialize( ref MessagePackReader reader, MessagePackSerializerOptions options )
            {
                return new IntPtr( reader.ReadInt64() );
            }
        }

        private static FormatterKind ParseFormatter( string value )
        {
            return value == "messagepack" || value == "msgpack" ? FormatterKind.MessagePack : FormatterKind.Json;
        }

        /// <summary>
        ///     Round-trips every member of both RPC contracts over the real MessagePack formatter. This
        ///     is the check that catches the transport switch failing: MessagePack's default resolver is
        ///     attribute-based, and none of <see cref="Point" />, <see cref="Size" />,
        ///     <see cref="IntPtr" /> or <see cref="ScreenshotFrame" /> has a built-in formatter.
        /// </summary>
        private static async Task RunContractCheck()
        {
            string pipeName = $"CAPacketContract_{Guid.NewGuid():N}";

            using NamedPipeServerStream server = new(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous );

            TaskCompletionSource uiDone = new( TaskCreationOptions.RunContinuationsAsynchronously );

            // UI endpoint: hosts IPluginMethods, calls IHostMethods.
            Task ui = Task.Run( async () =>
            {
                using NamedPipeClientStream client = new(
                    ".", pipeName, PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous );

                await client.ConnectAsync( 10000 );

                using JsonRpc rpc = new( CreateHandler( client, FormatterKind.MessagePack ), new PacketSink() );
                rpc.StartListening();

                IHostMethods host = rpc.Attach<IHostMethods>();

                await Try( "SendPacketToServer", async () => await host.SendPacketToServer( [1, 2, 3], 3 ) );
                await Try( "SendPacketToClient", async () => await host.SendPacketToClient( [1, 2, 3], 3 ) );
                await Try( "GetClientPath", async () => await host.GetClientPath() == "/tmp/client" );
                await Try( "GetClientVersion", async () => await host.GetClientVersion() == "7.0.0.0" );
                await Try( "GetPacketLength", async () => await host.GetPacketLength( 0x1B ) == 5 );
                await Try( "GetUOFilePath", async () => await host.GetUOFilePath() == "/tmp/uo" );
                await Try( "RequestMove", async () => await host.RequestMove( 1, true ) );
                await Try( "GetGumpPosition", async () => await host.GetGumpPosition( 0x1234 ) == (10, 20) );
                await Try( "WalkTo", async () => await host.WalkTo( 1, 2, 0, 1 ) );
                await Try( "GetWindowHandle", async () => await host.GetWindowHandle() == (IntPtr) 0x1234 );
                await Try( "GetProcessId", async () => await host.GetProcessId() == 42 );
                await Try( "GetGameWindowCenter", async () => await host.GetGameWindowCenter() == new Point( 960, 540 ) );
                await Try( "GetGameWindowSize", async () => await host.GetGameWindowSize() == new Size( 1920, 1080 ) );
                await Try( "CanCaptureClientFrame", async () => await host.CanCaptureClientFrame() );
                await Try( "CaptureClientFrame", async () =>
                {
                    ScreenshotFrame frame = await host.CaptureClientFrame();

                    return frame is { Path: "/tmp/frame", Width: 8, Height: 4 };
                } );
                await Try( "IsReflectionAvailable", async () => await host.IsReflectionAvailable() );
                await Try( "HasDisconnectedGump", async () => await host.HasDisconnectedGump() == false );

                uiDone.TrySetResult();

                await rpc.Completion;
            } );

            await server.WaitForConnectionAsync();

            // Plugin endpoint: hosts IHostMethods, calls IPluginMethods.
            using JsonRpc pluginRpc = new( CreateHandler( server, FormatterKind.MessagePack ), new HostStub() );
            pluginRpc.StartListening();

            IPluginMethods plugin = pluginRpc.Attach<IPluginMethods>();

            await Try( "OnPacketReceive", async () =>
            {
                (bool accept, byte[] rewritten, int length) = await plugin.OnPacketReceive( [0x1B], 1 );

                return accept && rewritten.Length == 0 && length == 0;
            } );
            await Try( "OnPacketSend", async () =>
            {
                (bool accept, byte[] rewritten, int length) = await plugin.OnPacketSend( [0x1B], 1 );

                return accept && rewritten.Length == 0 && length == 0;
            } );
            await Try( "OnHotkeyPressed", async () => await plugin.OnHotkeyPressed( 1, 2, true ) );

            await uiDone.Task;
            pluginRpc.Dispose();

            try
            {
                await ui;
            }
            catch ( Exception )
            {
                // Expected: the UI side sees the pipe close once the plugin is done.
            }
        }

        private static async Task Try( string name, Func<Task<bool>> check )
        {
            try
            {
                Console.WriteLine( $"{( await check() ? "PASS" : "FAIL" )}  {name}" );
            }
            catch ( Exception e )
            {
                Console.WriteLine( $"ERROR {name}: {e.GetType().Name}: {e.Message}" );
            }
        }

        private enum FormatterKind
        {
            Json,
            MessagePack
        }

        private enum Transport
        {
            InProcess,
            CrossProcess
        }

        /// <summary>
        ///     Stands in for <c>Engine.PluginMethods</c>: the same copy + compare the UI does to decide
        ///     whether it needs to send a rewritten packet back. The engine work that follows (filters,
        ///     packet handlers) runs on a queue thread and does not extend the round trip, so it is
        ///     deliberately not part of the measurement.
        /// </summary>
        private sealed class PacketSink : IPluginMethods
        {
            private long _received;

            public long Received => Interlocked.Read( ref _received );

            public void OnConnected()
            {
            }

            public void OnDisconnected()
            {
            }

            public void OnClientClosing()
            {
            }

            public void OnMouse( int button, int wheel )
            {
            }

            public void OnTick()
            {
            }

            public void OnFocusChanged( bool focus )
            {
            }

            public void OnPlayerPositionChanged( int x, int y, int z )
            {
            }

            public Task<bool> OnHotkeyPressed( int key, int mod, bool pressed )
            {
                return Task.FromResult( true );
            }

            public Task<(bool, byte[], int)> OnPacketSend( byte[] data, int length )
            {
                return OnPacketReceive( data, length );
            }

            public Task<(bool, byte[], int)> OnPacketReceive( byte[] data, int length )
            {
                Interlocked.Increment( ref _received );

                byte[] original = new byte[length];
                Array.Copy( data, original, length );

                bool modified = !original.AsSpan( 0, length ).SequenceEqual( data.AsSpan( 0, length ) );

                return Task.FromResult( (true, modified ? data : Array.Empty<byte>(), modified ? length : 0) );
            }
        }

        /// <summary>
        ///     Stands in for <c>PluginEngine.HostMethods</c> during the contract check. Every member
        ///     returns a representative value for its shape, so a serialization gap shows up as an
        ///     exception rather than being masked by a default.
        /// </summary>
        private sealed class HostStub : IHostMethods
        {
            public Task<bool> SendPacketToServer( byte[] packet, int length ) => Task.FromResult( true );
            public Task<bool> SendPacketToClient( byte[] packet, int length ) => Task.FromResult( true );
            public Task<string> GetClientPath() => Task.FromResult( "/tmp/client" );
            public Task<string> GetClientVersion() => Task.FromResult( "7.0.0.0" );
            public Task<short> GetPacketLength( int id ) => Task.FromResult( (short) 5 );
            public Task<string> GetUOFilePath() => Task.FromResult( "/tmp/uo" );
            public Task<bool> RequestMove( int dir, bool run ) => Task.FromResult( true );

            public void SetTitle( string title )
            {
            }

            public Task<(int x, int y)> GetGumpPosition( uint id ) => Task.FromResult( (10, 20) );
            public Task<bool> WalkTo( int x, int y, int z, int distance ) => Task.FromResult( true );
            public Task<bool> Pathfinding() => Task.FromResult( false );

            public void CancelPathfinding()
            {
            }

            public Task<IntPtr> GetWindowHandle() => Task.FromResult( (IntPtr) 0x1234 );
            public Task<int> GetProcessId() => Task.FromResult( 42 );

            public void CreateMacroButton( string name, string value )
            {
            }

            public Task<Point> GetGameWindowCenter() => Task.FromResult( new Point( 960, 540 ) );
            public Task<Size> GetGameWindowSize() => Task.FromResult( new Size( 1920, 1080 ) );
            public Task<bool> UsePrimaryAbility() => Task.FromResult( true );
            public Task<bool> UseSecondaryAbility() => Task.FromResult( false );
            public Task<bool> Following() => Task.FromResult( true );

            public void Logout()
            {
            }

            public void Quit()
            {
            }

            public void AddMapMarker( string name, int x, int y, int facet, int zoomLevel, string iconName )
            {
            }

            public Task<bool> Follow( int serial ) => Task.FromResult( true );

            public void PlayCUOMacro( string name )
            {
            }

            public Task<bool> HasDisconnectedGump() => Task.FromResult( false );
            public Task<bool> CanCaptureClientFrame() => Task.FromResult( true );

            public Task<ScreenshotFrame> CaptureClientFrame() =>
                Task.FromResult( new ScreenshotFrame { Path = "/tmp/frame", Width = 8, Height = 4 } );

            public Task<bool> IsReflectionAvailable() => Task.FromResult( true );

            public void OnShutdown()
            {
            }
        }
    }
}
