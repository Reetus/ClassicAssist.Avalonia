using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using ClassicAssist.DebugAdapter;
using ClassicAssist.DebugAdapter.Dap;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.DebugAdapter
{
    /// <summary>
    ///     <see cref="DapServer" />, the loopback listener the editor connects to: it serves a session per
    ///     connection, takes the next connection once the current one ends, shuts down cleanly (closing
    ///     the live session), and a failed bind leaves it stopped and able to start again rather than
    ///     wedged behind a half-built listener.
    /// </summary>
    public class DapServerTests : IAsyncLifetime
    {
        private static readonly FieldInfo ListenerField =
            typeof( DapServer ).GetField( "_listener", BindingFlags.NonPublic | BindingFlags.Static );

        private DebugAdapterGlobals _globals;

        public ValueTask InitializeAsync()
        {
            EngineState.Reset();
            DapServer.Shutdown();
            _globals = new DebugAdapterGlobals();

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DapServer.Shutdown();
            _globals.Dispose();

            return ValueTask.CompletedTask;
        }

        /// <summary>Port 0 asks the OS for a free port; DapServer.Port reports the requested one, so read the bound one.</summary>
        private static int BoundPort()
        {
            TcpListener listener = (TcpListener) ListenerField.GetValue( null );

            return ( (IPEndPoint) listener.LocalEndpoint ).Port;
        }

        [Fact]
        public async Task WillServeASessionOverLoopback()
        {
            DapServer.Initialize( 0 );

            Assert.True( DapServer.IsRunning );
            Assert.NotNull( DebugManager.Instance );

            await using DapHarness client = await DapHarness.ConnectAsync( BoundPort() );
            JObject response = await client.RequestAsync( "initialize" );

            Assert.True( (bool) response["success"] );
            Assert.Equal( IPAddress.Loopback, ( (IPEndPoint) ( (TcpListener) ListenerField.GetValue( null ) ).LocalEndpoint ).Address );
        }

        [Fact]
        public async Task WillCloseTheLiveSessionOnShutdown()
        {
            DapServer.Initialize( 0 );
            await using DapHarness client = await DapHarness.ConnectAsync( BoundPort() );
            await client.RequestAsync( "initialize" );

            DapServer.Shutdown();

            Assert.False( DapServer.IsRunning );
            Assert.True( await client.WaitForCloseAsync(), "the session stayed open after shutdown" );
        }

        [Fact]
        public async Task WillServeTheNextClientAfterTheFirstLeaves()
        {
            DapServer.Initialize( 0 );
            int port = BoundPort();

            await using ( DapHarness first = await DapHarness.ConnectAsync( port ) )
            {
                await first.RequestAsync( "initialize" );
                await first.RequestAsync( "disconnect" );
                Assert.True( await first.WaitForCloseAsync() );
            }

            await using DapHarness second = await DapHarness.ConnectAsync( port );
            JObject response = await second.RequestAsync( "threads" );

            Assert.True( (bool) response["success"] );
        }

        [Fact]
        public async Task WillStartAgainAfterShutdown()
        {
            DapServer.Initialize( 0 );
            DapServer.Shutdown();
            DapServer.Initialize( 0 );

            Assert.True( DapServer.IsRunning );

            await using DapHarness client = await DapHarness.ConnectAsync( BoundPort() );
            Assert.True( (bool) ( await client.RequestAsync( "threads" ) )["success"] );
        }

        [Fact]
        public void WillIgnoreASecondInitialise()
        {
            DapServer.Initialize( 0 );
            TcpListener listener = (TcpListener) ListenerField.GetValue( null );

            DapServer.Initialize( 0 );

            Assert.Same( listener, ListenerField.GetValue( null ) );
        }

        [Fact]
        public void WillStayStoppedAndRecoverableWhenThePortIsTaken()
        {
            TcpListener squatter = new TcpListener( IPAddress.Loopback, 0 );
            squatter.Start();

            try
            {
                int taken = ( (IPEndPoint) squatter.LocalEndpoint ).Port;

                Assert.ThrowsAny<SocketException>( () => DapServer.Initialize( taken ) );
                Assert.False( DapServer.IsRunning );
            }
            finally
            {
                squatter.Stop();
            }

            DapServer.Initialize( 0 );
            Assert.True( DapServer.IsRunning );
        }
    }
}
