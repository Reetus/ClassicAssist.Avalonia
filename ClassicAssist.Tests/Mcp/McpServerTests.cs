using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ClassicAssist.Mcp;
using ClassicAssist.Shared;
using Xunit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Tests.Mcp
{
    public class McpServerTests : IDisposable
    {
        public void Dispose()
        {
            McpServer.Shutdown();
            McpServer.Token = null;
        }

        [Fact]
        public void ToolRegistryIsPopulated()
        {
            McpTool[] tools = McpTools.GetTools().ToArray();

            Assert.Contains( tools, t => t.Name == "listMacros" );
            Assert.Contains( tools, t => t.Name == "getPlayer" );
            Assert.Contains( tools, t => t.Name == "invokeCommand" );
            Assert.Contains( tools, t => t.Name == "listAgents" );

            // Tools ported from upstream's additional-tools set.
            foreach ( string name in new[]
                     {
                         "getSnapshot", "getBuffs", "getSkills", "getSkill", "getTarget", "getHotkeys", "executeHotkey",
                         "getCurrentMacro", "getRunningMacros", "waitForMacro"
                     } )
            {
                Assert.True( tools.Any( t => t.Name == name ), $"Missing tool '{name}'." );
            }
        }

        [Fact]
        public void ToolsCarryAnnotations()
        {
            McpTool[] tools = McpTools.GetTools().ToArray();

            McpTool readOnly = tools.First( t => t.Name == "getPlayer" );

            Assert.True( readOnly.Annotations?["readOnlyHint"]?.ToObject<bool>() );
            Assert.False( readOnly.Annotations?["destructiveHint"]?.ToObject<bool>() ?? true );

            McpTool mutating = tools.First( t => t.Name == "executeHotkey" );

            Assert.False( mutating.Annotations?["readOnlyHint"]?.ToObject<bool>() ?? true );
            Assert.True( mutating.Annotations?["destructiveHint"]?.ToObject<bool>() );
        }

        [Fact]
        public void PaginateReturnsPageMetadata()
        {
            int[] source = Enumerable.Range( 0, 500 ).ToArray();

            (List<int> page, int total, int offsetValue, int limitValue) = McpTools.Paginate( source, 200, 0 );

            Assert.Equal( 500, total );
            Assert.Equal( 0, offsetValue );
            Assert.Equal( 200, limitValue );
            Assert.Equal( 200, page.Count );
            Assert.Equal( 0, page[0] );

            JObject result = new();

            McpTools.WithPageInfo( result, total, offsetValue, limitValue, page.Count );

            Assert.Equal( 500, result["total"]?.ToObject<int>() );
            Assert.Equal( 200, result["returned"]?.ToObject<int>() );
            Assert.True( result["truncated"]?.ToObject<bool>() );
        }

        [Fact]
        public void InspectionToolsInvokeWithoutAGameSession()
        {
            foreach ( string name in new[] { "getHotkeys", "getSkills", "getTarget", "getRunningMacros" } )
            {
                CallToolResult result = McpTools.Invoke( name, new JObject() );

                Assert.False( result.IsError, $"{name} returned an error: {result.Content.FirstOrDefault()?.Text}" );
                Assert.NotNull( result.Content.FirstOrDefault()?.Text );
            }
        }

        [Fact]
        public void UnknownSkillReportsError()
        {
            CallToolResult result = McpTools.Invoke( "getSkill", new JObject { ["name"] = "NoSuchSkill" } );

            Assert.True( result.IsError );
        }

        [Fact]
        public void WaitForMacroWithoutARunningMacroReportsError()
        {
            CallToolResult result = McpTools.Invoke( "waitForMacro", new JObject() );

            Assert.True( result.IsError );
        }

        [Fact]
        public void SnapshotWithoutHostReportsError()
        {
            CallToolResult result = McpTools.Invoke( "getSnapshot", new JObject() );

            Assert.True( result.IsError );
        }

        [Fact]
        public void ListMacrosInvokes()
        {
            CallToolResult result = McpTools.Invoke( "listMacros", new JObject() );

            Assert.False( result.IsError );
            Assert.NotNull( result.Content.FirstOrDefault()?.Text );
        }

        [Fact]
        public void UnknownToolReportsError()
        {
            CallToolResult result = McpTools.Invoke( "noSuchTool", new JObject() );

            Assert.True( result.IsError );
        }

        [Fact]
        public void ListCommandsInvokes()
        {
            CallToolResult result = McpTools.Invoke( "listCommands", new JObject() );

            Assert.False( result.IsError );

            JArray commands = JArray.Parse( result.Content.FirstOrDefault()?.Text ?? "[]" );

            Assert.True( commands.Count > 0 );
            Assert.Contains( commands, c => c["name"]?.ToString() == "SysMessage" );
        }

        [Fact]
        public void TryParseIntHandlesHexAndDecimal()
        {
            Assert.True( McpTools.TryParseInt( "0x400d379d", out int hex ) );
            Assert.Equal( 0x400d379d, hex );

            Assert.True( McpTools.TryParseInt( "deadbee", out int hexBare ) );
            Assert.Equal( 0xdeadbee, hexBare );

            Assert.True( McpTools.TryParseInt( "12345", out int dec ) );
            Assert.Equal( 12345, dec );

            Assert.False( McpTools.TryParseInt( "not-a-number", out _ ) );
        }

        [Fact]
        public async Task InitializeRoundTripsOverHttp()
        {
            int port = FreePort();
            McpServer.Initialize( port );

            Assert.True( McpServer.IsRunning );
            Assert.Equal( port, McpServer.Port );

            string body = await Post( port, @"{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize""}" );

            JObject response = JObject.Parse( body );

            Assert.Equal( 1, response["id"]?.ToObject<int>() );
            Assert.Equal( "2025-06-18", response["result"]?["protocolVersion"]?.ToString() );
            Assert.NotNull( response["result"]?["capabilities"]?["tools"] );
            Assert.NotNull( response["result"]?["serverInfo"]?["name"] );
        }

        [Fact]
        public async Task ToolsListRoundTripsOverHttp()
        {
            int port = FreePort();
            McpServer.Initialize( port );

            string body = await Post( port, @"{""jsonrpc"":""2.0"",""id"":2,""method"":""tools/list""}" );

            JObject response = JObject.Parse( body );

            Assert.Equal( 2, response["id"]?.ToObject<int>() );
            Assert.True( response["result"]?["tools"] is JArray { Count: > 0 } );
        }

        [Fact]
        public async Task ToolsCallRoundTripsOverHttp()
        {
            int port = FreePort();
            McpServer.Initialize( port );

            string body = await Post( port, @"{
                ""jsonrpc"":""2.0"",""id"":3,""method"":""tools/call"",
                ""params"":{""name"":""listMacros"",""arguments"":{}}
            }" );

            JObject response = JObject.Parse( body );

            Assert.Equal( 3, response["id"]?.ToObject<int>() );
            Assert.False( response["result"]?["isError"]?.ToObject<bool>() ?? true );
            Assert.True( response["result"]?["content"] is JArray { Count: 1 } );
        }

        [Fact]
        public void NonPostIsRejected()
        {
            int port = FreePort();
            McpServer.Initialize( port );

            using TcpClient client = new();
            client.Connect( IPAddress.Loopback, port );
            using NetworkStream stream = client.GetStream();

            byte[] request = Encoding.ASCII.GetBytes( "GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" );
            stream.Write( request, 0, request.Length );

            using StreamReader reader = new( stream, Encoding.UTF8, false, leaveOpen: true );
            string firstLine = reader.ReadLine() ?? string.Empty;

            Assert.StartsWith( "HTTP/1.1 405", firstLine, StringComparison.Ordinal );
        }

        [Fact]
        public void BrowserOriginIsRejected()
        {
            int port = FreePort();
            McpServer.Initialize( port );

            Assert.Equal( 403, Send( port, Request( "Host: localhost", "Origin: https://example.com" ) ) );
        }

        [Fact]
        public void NonLoopbackHostIsRejected()
        {
            int port = FreePort();
            McpServer.Initialize( port );

            Assert.Equal( 403, Send( port, Request( "Host: attacker.example:" + port ) ) );
        }

        [Fact]
        public void NonJsonContentTypeIsRejected()
        {
            int port = FreePort();
            McpServer.Initialize( port );

            Assert.Equal( 415, Send( port, Request( "Host: localhost", "Content-Type: text/plain" ) ) );
        }

        [Fact]
        public void TokenIsRequiredWhenSet()
        {
            int port = FreePort();
            McpServer.Initialize( port );
            McpServer.Token = "secret";

            Assert.Equal( 401, Send( port, Request( "Host: localhost" ) ) );
            Assert.Equal( 401, Send( port, Request( "Host: localhost", "Authorization: Bearer wrong" ) ) );
            Assert.Equal( 200, Send( port, Request( "Host: 127.0.0.1:" + port, "Authorization: Bearer secret" ) ) );
        }

        private static string Request( params string[] headers )
        {
            byte[] body = Encoding.UTF8.GetBytes( @"{""jsonrpc"":""2.0"",""id"":1,""method"":""ping""}" );

            bool hasContentType = headers.Any( h => h.StartsWith( "Content-Type:", StringComparison.OrdinalIgnoreCase ) );

            return "POST /mcp HTTP/1.1\r\n" + string.Join( "", headers.Select( h => h + "\r\n" ) ) +
                   ( hasContentType ? "" : "Content-Type: application/json\r\n" ) +
                   $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n" + Encoding.UTF8.GetString( body );
        }

        private static int Send( int port, string request )
        {
            using TcpClient client = new();
            client.Connect( IPAddress.Loopback, port );
            using NetworkStream stream = client.GetStream();

            byte[] bytes = Encoding.UTF8.GetBytes( request );
            stream.Write( bytes, 0, bytes.Length );

            using StreamReader reader = new( stream, Encoding.UTF8, false, leaveOpen: true );
            string[] statusLine = ( reader.ReadLine() ?? string.Empty ).Split( ' ' );

            return statusLine.Length > 1 && int.TryParse( statusLine[1], out int status ) ? status : -1;
        }

        private static int FreePort()
        {
            using TcpListener listener = new( IPAddress.Loopback, 0 );

            listener.Start();

            return ( (IPEndPoint) listener.LocalEndpoint ).Port;
        }

        private static async Task<string> Post( int port, string json )
        {
            using TcpClient client = new();
            client.Connect( IPAddress.Loopback, port );
            using NetworkStream stream = client.GetStream();

            byte[] body = Encoding.UTF8.GetBytes( json );
            string header = $"POST /mcp HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            byte[] request = Encoding.ASCII.GetBytes( header ).Concat( body ).ToArray();

            await stream.WriteAsync( request, 0, request.Length );
            await stream.FlushAsync();

            byte[] buffer = new byte[64 * 1024];
            int read = await stream.ReadAsync( buffer, 0, buffer.Length );
            string response = Encoding.ASCII.GetString( buffer, 0, read );

            int bodyStart = response.IndexOf( "\r\n\r\n", StringComparison.Ordinal );

            Assert.StartsWith( "HTTP/1.1 200", response, StringComparison.Ordinal );
            Assert.True( bodyStart > 0 );

            return response.Substring( bodyStart + 4 );
        }
    }
}