using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ClassicAssist.Mcp;
using ClassicAssist.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Tests.Mcp
{
    [TestClass]
    public class McpServerTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            McpServer.Shutdown();
        }

        [TestMethod]
        public void ToolRegistryIsPopulated()
        {
            McpTool[] tools = McpTools.GetTools().ToArray();

            Assert.IsTrue( tools.Any( t => t.Name == "listMacros" ) );
            Assert.IsTrue( tools.Any( t => t.Name == "getPlayer" ) );
            Assert.IsTrue( tools.Any( t => t.Name == "invokeCommand" ) );
            Assert.IsTrue( tools.Any( t => t.Name == "listAgents" ) );
        }

        [TestMethod]
        public void ListMacrosInvokes()
        {
            CallToolResult result = McpTools.Invoke( "listMacros", new JObject() );

            Assert.IsFalse( result.IsError );
            Assert.IsNotNull( result.Content.FirstOrDefault()?.Text );
        }

        [TestMethod]
        public void UnknownToolReportsError()
        {
            CallToolResult result = McpTools.Invoke( "noSuchTool", new JObject() );

            Assert.IsTrue( result.IsError );
        }

        [TestMethod]
        public void ListCommandsInvokes()
        {
            CallToolResult result = McpTools.Invoke( "listCommands", new JObject() );

            Assert.IsFalse( result.IsError );

            JArray commands = JArray.Parse( result.Content.FirstOrDefault()?.Text ?? "[]" );

            Assert.IsTrue( commands.Count > 0 );
            Assert.IsTrue( commands.Any( c => c["name"]?.ToString() == "SysMessage" ) );
        }

        [TestMethod]
        public void TryParseIntHandlesHexAndDecimal()
        {
            Assert.IsTrue( McpTools.TryParseInt( "0x400d379d", out int hex ) );
            Assert.AreEqual( 0x400d379d, hex );

            Assert.IsTrue( McpTools.TryParseInt( "deadbee", out int hexBare ) );
            Assert.AreEqual( 0xdeadbee, hexBare );

            Assert.IsTrue( McpTools.TryParseInt( "12345", out int dec ) );
            Assert.AreEqual( 12345, dec );

            Assert.IsFalse( McpTools.TryParseInt( "not-a-number", out _ ) );
        }

        [TestMethod]
        public async Task InitializeRoundTripsOverHttp()
        {
            int port = FreePort();
            McpServer.Initialize( port );

            Assert.IsTrue( McpServer.IsRunning );
            Assert.AreEqual( port, McpServer.Port );

            string body = await Post( port, @"{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize""}" );

            JObject response = JObject.Parse( body );

            Assert.AreEqual( 1, response["id"]?.ToObject<int>() );
            Assert.AreEqual( "2025-06-18", response["result"]?["protocolVersion"]?.ToString() );
            Assert.IsNotNull( response["result"]?["capabilities"]?["tools"] );
            Assert.IsNotNull( response["result"]?["serverInfo"]?["name"] );
        }

        [TestMethod]
        public async Task ToolsListRoundTripsOverHttp()
        {
            int port = FreePort();
            McpServer.Initialize( port );

            string body = await Post( port, @"{""jsonrpc"":""2.0"",""id"":2,""method"":""tools/list""}" );

            JObject response = JObject.Parse( body );

            Assert.AreEqual( 2, response["id"]?.ToObject<int>() );
            Assert.IsTrue( response["result"]?["tools"] is JArray { Count: > 0 } );
        }

        [TestMethod]
        public async Task ToolsCallRoundTripsOverHttp()
        {
            int port = FreePort();
            McpServer.Initialize( port );

            string body = await Post( port, @"{
                ""jsonrpc"":""2.0"",""id"":3,""method"":""tools/call"",
                ""params"":{""name"":""listMacros"",""arguments"":{}}
            }" );

            JObject response = JObject.Parse( body );

            Assert.AreEqual( 3, response["id"]?.ToObject<int>() );
            Assert.IsFalse( response["result"]?["isError"]?.ToObject<bool>() ?? true );
            Assert.IsTrue( response["result"]?["content"] is JArray { Count: 1 } );
        }

        [TestMethod]
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

            Assert.IsTrue( firstLine.StartsWith( "HTTP/1.1 405", StringComparison.Ordinal ) );
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

            Assert.IsTrue( response.StartsWith( "HTTP/1.1 200", StringComparison.Ordinal ) );
            Assert.IsTrue( bodyStart > 0 );

            return response.Substring( bodyStart + 4 );
        }
    }
}