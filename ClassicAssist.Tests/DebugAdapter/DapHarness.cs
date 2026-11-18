using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.DebugAdapter;
using ClassicAssist.DebugAdapter.Dap;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.DebugAdapter
{
    /// <summary>
    ///     A minimal DAP client on one end of a loopback socket with a real <see cref="DapSession" /> on
    ///     the other - the session only takes a <see cref="TcpClient" />. Everything it reads is parsed
    ///     with its own Content-Length framing (strict: the body must be exactly the advertised number of
    ///     UTF-8 bytes and valid JSON), so a framing mistake on the server side fails the test that
    ///     caused it. Every wait is bounded by <see cref="Timeout" />.
    /// </summary>
    internal sealed class DapHarness : IAsyncDisposable
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds( 10 );

        private readonly TcpClient _client;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly NetworkStream _stream;
        private readonly List<JObject> _unconsumed = new List<JObject>();
        private byte[] _pending = new byte[0];
        private int _nextSeq = 1;

        private DapHarness( TcpClient client, DapSession session )
        {
            _client = client;
            _stream = client.GetStream();
            Session = session;
            RunTask = session == null ? Task.CompletedTask : Task.Run( () => session.RunAsync( _cts.Token ) );
        }

        public DapSession Session { get; }

        public Task RunTask { get; }

        /// <summary>Every message received so far, in arrival order.</summary>
        public List<JObject> Received { get; } = new List<JObject>();

        public bool Closed { get; private set; }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            Session?.Close();
            _client.Dispose();

            try
            {
                await RunTask.WaitAsync( Timeout );
            }
            catch ( Exception )
            {
                // the session is gone either way
            }

            _cts.Dispose();
        }

        /// <summary>Closes the client end abruptly, as an editor that crashed or was killed would.</summary>
        public void DropConnection()
        {
            _client.Close();
        }

        public static async Task<DapHarness> StartAsync( DebugManager manager )
        {
            TcpListener listener = new TcpListener( IPAddress.Loopback, 0 );
            listener.Start();

            try
            {
                int port = ( (IPEndPoint) listener.LocalEndpoint ).Port;
                TcpClient client = new TcpClient { NoDelay = true };
                Task connect = client.ConnectAsync( IPAddress.Loopback, port );
                TcpClient server = await listener.AcceptTcpClientAsync().WaitAsync( Timeout );
                await connect.WaitAsync( Timeout );

                return new DapHarness( client, new DapSession( server, manager ) );
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>A client connected to an existing server (DapServer), with no session of its own.</summary>
        public static async Task<DapHarness> ConnectAsync( int port )
        {
            TcpClient client = new TcpClient { NoDelay = true };
            await client.ConnectAsync( IPAddress.Loopback, port ).WaitAsync( Timeout );

            return new DapHarness( client, null );
        }

        public static byte[] Frame( string json )
        {
            byte[] body = Encoding.UTF8.GetBytes( json );
            byte[] header = Encoding.ASCII.GetBytes( $"Content-Length: {body.Length}\r\n\r\n" );
            byte[] result = new byte[header.Length + body.Length];
            header.CopyTo( result, 0 );
            body.CopyTo( result, header.Length );

            return result;
        }

        public string RequestJson( int seq, string command, object arguments = null )
        {
            JObject request = new JObject { ["seq"] = seq, ["type"] = "request", ["command"] = command };

            if ( arguments != null )
            {
                request["arguments"] = JToken.FromObject( arguments );
            }

            return request.ToString( Formatting.None );
        }

        public async Task WriteRawAsync( byte[] bytes )
        {
            await _stream.WriteAsync( bytes, 0, bytes.Length, _cts.Token );
            await _stream.FlushAsync( _cts.Token );
        }

        public async Task<int> SendAsync( string command, object arguments = null )
        {
            int seq = _nextSeq++;
            await WriteRawAsync( Frame( RequestJson( seq, command, arguments ) ) );

            return seq;
        }

        public async Task<JObject> RequestAsync( string command, object arguments = null )
        {
            int seq = await SendAsync( command, arguments );

            return await WaitForResponseAsync( seq );
        }

        public Task<JObject> WaitForResponseAsync( int requestSeq )
        {
            return WaitForAsync( m => (string) m["type"] == "response" && (int) m["request_seq"] == requestSeq,
                $"response to request {requestSeq}" );
        }

        public Task<JObject> WaitForEventAsync( string name, Func<JObject, bool> predicate = null )
        {
            return WaitForAsync(
                m => (string) m["type"] == "event" && (string) m["event"] == name &&
                     ( predicate == null || predicate( m ) ), $"'{name}' event" );
        }

        /// <summary>
        ///     Returns the first message, already received or yet to come, that matches. Messages that
        ///     don't match are kept for later waits.
        /// </summary>
        public async Task<JObject> WaitForAsync( Func<JObject, bool> predicate, string what )
        {
            for ( int i = 0; i < _unconsumed.Count; i++ )
            {
                if ( predicate( _unconsumed[i] ) )
                {
                    JObject match = _unconsumed[i];
                    _unconsumed.RemoveAt( i );

                    return match;
                }
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource( _cts.Token );
            timeout.CancelAfter( Timeout );

            while ( true )
            {
                JObject message;

                try
                {
                    message = await ReadMessageAsync( timeout.Token );
                }
                catch ( OperationCanceledException )
                {
                    Assert.Fail( $"Timed out waiting for {what}. Received: {Describe()}" );

                    return null;
                }

                if ( message == null )
                {
                    Assert.Fail( $"Connection closed while waiting for {what}. Received: {Describe()}" );
                }

                if ( predicate( message ) )
                {
                    return message;
                }

                _unconsumed.Add( message );
            }
        }

        /// <summary>Reads until the server closes the connection. Returns false on timeout.</summary>
        public async Task<bool> WaitForCloseAsync()
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource( _cts.Token );
            timeout.CancelAfter( Timeout );

            try
            {
                while ( true )
                {
                    JObject message = await ReadMessageAsync( timeout.Token );

                    if ( message == null )
                    {
                        return true;
                    }

                    _unconsumed.Add( message );
                }
            }
            catch ( OperationCanceledException )
            {
                return false;
            }
            catch ( System.IO.IOException )
            {
                // reset rather than an orderly close - still closed
                return true;
            }
        }

        /// <summary>
        ///     Reads exactly one framed message, or null at end of stream. The header must carry a
        ///     Content-Length and the body must be exactly that many bytes of JSON.
        /// </summary>
        private async Task<JObject> ReadMessageAsync( CancellationToken token )
        {
            while ( true )
            {
                if ( TryTakeMessage( out JObject message ) )
                {
                    Received.Add( message );

                    return message;
                }

                if ( Closed )
                {
                    return null;
                }

                byte[] chunk = new byte[8192];
                int read = await _stream.ReadAsync( chunk, 0, chunk.Length, token );

                if ( read == 0 )
                {
                    Closed = true;

                    continue;
                }

                byte[] combined = new byte[_pending.Length + read];
                _pending.CopyTo( combined, 0 );
                Array.Copy( chunk, 0, combined, _pending.Length, read );
                _pending = combined;
            }
        }

        private bool TryTakeMessage( out JObject message )
        {
            message = null;

            int headerEnd = -1;

            for ( int i = 0; i + 3 < _pending.Length; i++ )
            {
                if ( _pending[i] == '\r' && _pending[i + 1] == '\n' && _pending[i + 2] == '\r' &&
                     _pending[i + 3] == '\n' )
                {
                    headerEnd = i;

                    break;
                }
            }

            if ( headerEnd < 0 )
            {
                return false;
            }

            string header = Encoding.ASCII.GetString( _pending, 0, headerEnd );
            Assert.StartsWith( "Content-Length: ", header );
            int length = int.Parse( header.Substring( "Content-Length: ".Length ) );

            int bodyStart = headerEnd + 4;

            if ( _pending.Length < bodyStart + length )
            {
                return false;
            }

            string json = Encoding.UTF8.GetString( _pending, bodyStart, length );
            message = JObject.Parse( json );

            byte[] rest = new byte[_pending.Length - bodyStart - length];
            Array.Copy( _pending, bodyStart + length, rest, 0, rest.Length );
            _pending = rest;

            return true;
        }

        private string Describe()
        {
            StringBuilder sb = new StringBuilder();

            foreach ( JObject message in Received )
            {
                sb.Append( message.ToString( Formatting.None ) ).Append( ' ' );
            }

            return sb.ToString();
        }
    }
}
