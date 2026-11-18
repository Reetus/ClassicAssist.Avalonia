using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ClassicAssist.Mcp;
using ClassicAssist.Shared;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.Mcp
{
    /// <summary>
    ///     The JSON-RPC and HTTP edge cases of the MCP server beyond the happy paths in McpServerTests:
    ///     malformed or partial requests must get the right JSON-RPC error code (or HTTP status) rather than
    ///     a dropped connection, notifications must still be answered at the HTTP layer, a request split
    ///     across TCP writes must be reassembled, and concurrent clients must each get their own answer.
    ///     An agent that gets a dropped connection instead of an error has nothing to recover from.
    /// </summary>
    public class McpProtocolTests : IDisposable
    {
        private readonly int _port;

        public McpProtocolTests()
        {
            EngineState.Reset();
            McpServer.Shutdown();
            McpServer.Token = null;

            _port = McpHttp.FreePort();
            McpServer.Initialize( _port );
        }

        public void Dispose()
        {
            McpServer.Shutdown();
            McpServer.Token = null;
            EngineState.Reset();
        }

        private async Task<JObject> Rpc( string json )
        {
            McpHttp.Response response = await McpHttp.PostJson( _port, json, TestContext.Current.CancellationToken );

            Assert.Equal( 200, response.Status );

            return JObject.Parse( response.Body );
        }

        [Fact]
        public async Task WillReturnParseErrorWhenBodyIsNotJson()
        {
            JObject response = await Rpc( "{ this is not json" );

            Assert.Equal( -32700, response["error"]?["code"]?.ToObject<int>() );
            Assert.Equal( JTokenType.Null, response["id"]?.Type );
        }

        [Fact]
        public async Task WillReturnInvalidRequestWhenMethodIsMissing()
        {
            JObject response = await Rpc( @"{""jsonrpc"":""2.0"",""id"":5}" );

            Assert.Equal( -32600, response["error"]?["code"]?.ToObject<int>() );
        }

        [Fact]
        public async Task WillReturnInvalidRequestWhenBodyIsEmpty()
        {
            McpHttp.Response response = await McpHttp.SendRaw( _port, TestContext.Current.CancellationToken,
                "POST /mcp HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nContent-Length: 0\r\nConnection: close\r\n\r\n" );

            Assert.Equal( 200, response.Status );
            Assert.Equal( -32600, JObject.Parse( response.Body )["error"]?["code"]?.ToObject<int>() );
        }

        [Fact]
        public async Task WillReturnMethodNotFoundForUnknownRequestAndKeepItsId()
        {
            JObject response = await Rpc( @"{""jsonrpc"":""2.0"",""id"":""abc-1"",""method"":""resources/list""}" );

            Assert.Equal( -32601, response["error"]?["code"]?.ToObject<int>() );
            Assert.Equal( "abc-1", response["id"]?.ToString() );
            Assert.Null( response["result"] );
        }

        [Theory]
        [InlineData( "notifications/initialized" )]
        [InlineData( "notifications/cancelled" )]
        [InlineData( "notifications/somethingNew" )]
        public async Task WillAcceptNotificationsWithAnEmpty202( string method )
        {
            McpHttp.Response response = await McpHttp.PostJson( _port, $@"{{""jsonrpc"":""2.0"",""method"":""{method}""}}",
                TestContext.Current.CancellationToken );

            Assert.Equal( 202, response.Status );
            Assert.Equal( string.Empty, response.Body );
        }

        [Fact]
        public async Task WillReturnInvalidParamsWhenToolNameIsMissing()
        {
            JObject response = await Rpc( @"{""jsonrpc"":""2.0"",""id"":7,""method"":""tools/call"",""params"":{""arguments"":{}}}" );

            Assert.Equal( -32602, response["error"]?["code"]?.ToObject<int>() );
            Assert.Equal( 7, response["id"]?.ToObject<int>() );
        }

        [Fact]
        public async Task WillReturnInvalidParamsWhenParamsAreMissing()
        {
            JObject response = await Rpc( @"{""jsonrpc"":""2.0"",""id"":8,""method"":""tools/call""}" );

            Assert.Equal( -32602, response["error"]?["code"]?.ToObject<int>() );
        }

        [Fact]
        public async Task WillReturnInvalidParamsWhenArgumentsIsNotAnObject()
        {
            JObject response = await Rpc(
                @"{""jsonrpc"":""2.0"",""id"":9,""method"":""tools/call"",""params"":{""name"":""getPlayer"",""arguments"":[1,2]}}" );

            Assert.Equal( -32602, response["error"]?["code"]?.ToObject<int>() );
        }

        /// <summary>
        ///     A tool failure is a tool result with isError, not a protocol error - the MCP spec puts it
        ///     there so the model sees the message.
        /// </summary>
        [Fact]
        public async Task WillReportUnknownToolAsToolErrorNotProtocolError()
        {
            JObject response = await Rpc(
                @"{""jsonrpc"":""2.0"",""id"":10,""method"":""tools/call"",""params"":{""name"":""noSuchTool""}}" );

            Assert.Null( response["error"] );
            Assert.True( response["result"]?["isError"]?.ToObject<bool>() );
            Assert.Contains( "noSuchTool", response["result"]?["content"]?[0]?["text"]?.ToString() );
        }

        [Fact]
        public async Task WillCallToolWithoutArgumentsObject()
        {
            EngineState.ResetWithPlayer().Name = "Protocol Tester";

            JObject response = await Rpc(
                @"{""jsonrpc"":""2.0"",""id"":11,""method"":""tools/call"",""params"":{""name"":""getPlayer""}}" );

            Assert.False( response["result"]?["isError"]?.ToObject<bool>() ?? true );

            JObject player = JObject.Parse( response["result"]?["content"]?[0]?["text"]?.ToString() ?? "{}" );

            Assert.Equal( "Protocol Tester", player["name"]?.ToString() );
        }

        [Fact]
        public async Task WillReassembleRequestSplitAcrossWrites()
        {
            string request = McpHttp.PostRequest( @"{""jsonrpc"":""2.0"",""id"":12,""method"":""ping""}" );
            int split = request.IndexOf( "\r\n\r\n", StringComparison.Ordinal ) + 6;

            McpHttp.Response response = await McpHttp.SendRaw( _port, TestContext.Current.CancellationToken,
                request.Substring( 0, 20 ), request.Substring( 20, split - 20 ), request.Substring( split ) );

            Assert.Equal( 200, response.Status );
            Assert.Equal( 12, JObject.Parse( response.Body )["id"]?.ToObject<int>() );
        }

        [Fact]
        public async Task WillCountContentLengthInBytesNotCharacters()
        {
            // Multi-byte UTF-8 in the body: a server counting characters would stop short and fail to parse.
            JObject response = await Rpc(
                @"{""jsonrpc"":""2.0"",""id"":13,""method"":""tools/call"",""params"":{""name"":""listMacros"",""arguments"":{""filter"":""ÄÖÜ-日本""}}}" );

            Assert.Null( response["error"] );
            Assert.Equal( 13, response["id"]?.ToObject<int>() );
        }

        [Fact]
        public async Task WillAnswerOptionsWith204()
        {
            McpHttp.Response response = await McpHttp.SendRaw( _port, TestContext.Current.CancellationToken,
                "OPTIONS /mcp HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" );

            Assert.Equal( 204, response.Status );
        }

        [Fact]
        public async Task WillAdvertiseAllowedMethodsOn405()
        {
            McpHttp.Response response = await McpHttp.SendRaw( _port, TestContext.Current.CancellationToken,
                "DELETE /mcp HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" );

            Assert.Equal( 405, response.Status );
            Assert.Contains( "Allow: POST, OPTIONS", response.Headers );
        }

        [Fact]
        public async Task WillRejectUnknownPathWith404()
        {
            McpHttp.Response response = await McpHttp.SendRaw( _port, TestContext.Current.CancellationToken,
                McpHttp.PostRequest( @"{""jsonrpc"":""2.0"",""id"":1,""method"":""ping""}" ).Replace( "POST /mcp ", "POST /other " ) );

            Assert.Equal( 404, response.Status );
        }

        [Theory]
        [InlineData( "application/json; charset=utf-8" )]
        [InlineData( "APPLICATION/JSON" )]
        public async Task WillAcceptJsonContentTypeVariants( string contentType )
        {
            McpHttp.Response response = await McpHttp.SendRaw( _port, TestContext.Current.CancellationToken,
                McpHttp.PostRequest( @"{""jsonrpc"":""2.0"",""id"":1,""method"":""ping""}", contentType ) );

            Assert.Equal( 200, response.Status );
        }

        [Fact]
        public async Task WillRejectMissingContentTypeWith415()
        {
            McpHttp.Response response = await McpHttp.SendRaw( _port, TestContext.Current.CancellationToken,
                McpHttp.PostRequest( @"{""jsonrpc"":""2.0"",""id"":1,""method"":""ping""}", null ) );

            Assert.Equal( 415, response.Status );
        }

        [Theory]
        [InlineData( "[::1]" )]
        [InlineData( "localhost" )]
        public async Task WillAcceptLoopbackHostWithPort( string host )
        {
            McpHttp.Response response = await McpHttp.SendRaw( _port, TestContext.Current.CancellationToken,
                McpHttp.PostRequest( @"{""jsonrpc"":""2.0"",""id"":1,""method"":""ping""}" )
                    .Replace( "Host: localhost", $"Host: {host}:{_port}" ) );

            Assert.Equal( 200, response.Status );
        }

        [Fact]
        public async Task WillRejectWrongTokenWithJsonRpcErrorBody()
        {
            McpServer.Token = "right";

            McpHttp.Response response = await McpHttp.SendRaw( _port, TestContext.Current.CancellationToken,
                McpHttp.PostRequest( @"{""jsonrpc"":""2.0"",""id"":1,""method"":""ping""}", extraHeaders: "Authorization: Basic right\r\n" ) );

            Assert.Equal( 401, response.Status );
            Assert.Contains( "WWW-Authenticate: Bearer", response.Headers );
            Assert.Equal( -32001, JObject.Parse( response.Body )["error"]?["code"]?.ToObject<int>() );
        }

        [Fact]
        public async Task WillAnswerConcurrentRequestsEachWithItsOwnId()
        {
            EngineState.ResetWithPlayer();

            List<Task<McpHttp.Response>> requests = new List<Task<McpHttp.Response>>();

            for ( int i = 0; i < 12; i++ )
            {
                string method = i % 2 == 0 ? "ping" : "tools/list";

                requests.Add( McpHttp.PostJson( _port, $@"{{""jsonrpc"":""2.0"",""id"":{100 + i},""method"":""{method}""}}",
                    TestContext.Current.CancellationToken ) );
            }

            McpHttp.Response[] responses = await Task.WhenAll( requests );

            int[] ids = responses.Select( r => JObject.Parse( r.Body )["id"]?.ToObject<int>() ?? -1 ).OrderBy( i => i ).ToArray();

            Assert.All( responses, r => Assert.Equal( 200, r.Status ) );
            Assert.Equal( Enumerable.Range( 100, 12 ).ToArray(), ids );
        }

        [Fact]
        public void WillIgnoreSecondInitializeWhileRunning()
        {
            int otherPort = McpHttp.FreePort();

            McpServer.Initialize( otherPort );

            Assert.True( McpServer.IsRunning );
            Assert.Equal( _port, McpServer.Port );
        }

        [Fact]
        public async Task WillThrowAndStayStoppedWhenPortIsTaken()
        {
            McpServer.Shutdown();

            TcpListener squatter = new TcpListener( IPAddress.Loopback, 0 );
            squatter.Start();

            try
            {
                int taken = ( (IPEndPoint) squatter.LocalEndpoint ).Port;

                Assert.ThrowsAny<SocketException>( () => McpServer.Initialize( taken ) );
                Assert.False( McpServer.IsRunning );
            }
            finally
            {
                squatter.Stop();
            }

            // A failed start must not wedge the server: the next Initialize binds normally.
            int port = McpHttp.FreePort();
            McpServer.Initialize( port );

            McpHttp.Response response = await McpHttp.PostJson( port, @"{""jsonrpc"":""2.0"",""id"":1,""method"":""ping""}",
                TestContext.Current.CancellationToken );

            Assert.Equal( 200, response.Status );
        }

        [Fact]
        public async Task WillListEveryToolWithAnObjectSchemaAndUniqueName()
        {
            JObject response = await Rpc( @"{""jsonrpc"":""2.0"",""id"":1,""method"":""tools/list""}" );

            JArray tools = (JArray) response["result"]?["tools"];

            Assert.NotNull( tools );

            string[] names = tools.Select( t => t["name"]?.ToString() ).ToArray();

            Assert.Equal( names.Length, names.Distinct( StringComparer.Ordinal ).Count() );

            foreach ( JToken tool in tools )
            {
                Assert.Equal( "object", tool["inputSchema"]?["type"]?.ToString() );
                Assert.False( string.IsNullOrEmpty( tool["description"]?.ToString() ), $"{tool["name"]} has no description" );

                // Every required argument must be a declared property, or a client can't build the call.
                if ( tool["inputSchema"]?["required"] is JArray required )
                {
                    foreach ( JToken name in required )
                    {
                        Assert.NotNull( tool["inputSchema"]?["properties"]?[name.ToString()] );
                    }
                }
            }
        }
    }
}
