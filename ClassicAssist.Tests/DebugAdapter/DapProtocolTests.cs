using System;
using System.IO;
using System.Reflection;
using System.Text;
using ClassicAssist.DebugAdapter.Dap;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.DebugAdapter
{
    /// <summary>
    ///     The DAP wire format: a "Content-Length: N" header, a blank line, then exactly N bytes of
    ///     UTF-8 JSON. Pins the serialiser (byte count, not char count; nulls omitted; event bodies) and
    ///     the session's frame extractor on the read side - messages split across reads, several in one
    ///     read, multi-byte characters and header variations - since a framing slip desynchronises every
    ///     message after it.
    /// </summary>
    public class DapProtocolTests
    {
        private static readonly MethodInfo TryExtract =
            typeof( DapSession ).GetMethod( "TryExtractMessage", BindingFlags.NonPublic | BindingFlags.Static );

        private static string Split( byte[] framed, out JObject body )
        {
            string text = Encoding.UTF8.GetString( framed );
            int separator = text.IndexOf( "\r\n\r\n", StringComparison.Ordinal );
            body = JObject.Parse( text.Substring( separator + 4 ) );

            return text.Substring( 0, separator );
        }

        private static bool Extract( MemoryStream pending, out string json )
        {
            object[] args = { pending, null };
            bool found = (bool) TryExtract.Invoke( null, args );
            json = args[1] == null ? null : Encoding.UTF8.GetString( (byte[]) args[1] );

            return found;
        }

        private static MemoryStream Pending( params byte[][] chunks )
        {
            MemoryStream stream = new MemoryStream();

            foreach ( byte[] chunk in chunks )
            {
                stream.Write( chunk, 0, chunk.Length );
            }

            return stream;
        }

        [Fact]
        public void WillCountTheContentLengthInUtf8Bytes()
        {
            DapResponse response = new DapResponse { Command = "evaluate", Message = "café → ✓", Success = true };

            byte[] framed = DapProtocol.Serialize( response );
            string text = Encoding.UTF8.GetString( framed );
            int separator = text.IndexOf( "\r\n\r\n", StringComparison.Ordinal );
            int bodyBytes = framed.Length - Encoding.UTF8.GetByteCount( text.Substring( 0, separator + 4 ) );

            Assert.Equal( $"Content-Length: {bodyBytes}", text.Substring( 0, separator ) );
            Assert.True( bodyBytes > text.Length - separator - 4, "body has multi-byte characters" );
            Assert.Equal( "café → ✓", (string) JObject.Parse( text.Substring( separator + 4 ) )["message"] );
        }

        [Fact]
        public void WillSerialiseAResponseWithItsCorrelationFields()
        {
            DapResponse response = new DapResponse
            {
                Seq = 5, RequestSeq = 3, Command = "threads", Success = true, Body = new { threads = new int[0] }
            };

            Split( DapProtocol.Serialize( response ), out JObject body );

            Assert.Equal( 5, (int) body["seq"] );
            Assert.Equal( "response", (string) body["type"] );
            Assert.Equal( 3, (int) body["request_seq"] );
            Assert.Equal( "threads", (string) body["command"] );
            Assert.True( (bool) body["success"] );
            Assert.NotNull( body["body"]["threads"] );
        }

        /// <summary>Clients treat a present-but-null "message" or "body" differently from an absent one.</summary>
        [Fact]
        public void WillOmitNullMessageAndBody()
        {
            Split( DapProtocol.Serialize( new DapResponse { Command = "next", Success = true } ), out JObject body );

            Assert.Null( body.Property( "message" ) );
            Assert.Null( body.Property( "body" ) );

            Split( DapProtocol.Serialize( DapEvent.Initialized() ), out JObject evt );
            Assert.Equal( "event", (string) evt["type"] );
            Assert.Equal( "initialized", (string) evt["event"] );
            Assert.Null( evt.Property( "body" ) );
        }

        [Fact]
        public void WillSerialiseEventBodies()
        {
            Split( DapProtocol.Serialize( DapEvent.Stopped( 12, "breakpoint" ) ), out JObject stopped );
            Assert.Equal( "stopped", (string) stopped["event"] );
            Assert.Equal( "breakpoint", (string) stopped["body"]["reason"] );
            Assert.Equal( 12, (int) stopped["body"]["threadId"] );
            Assert.False( (bool) stopped["body"]["allThreadsStopped"] );

            Split( DapProtocol.Serialize( DapEvent.Continued( 12 ) ), out JObject continued );
            Assert.Equal( 12, (int) continued["body"]["threadId"] );
            Assert.False( (bool) continued["body"]["allThreadsContinued"] );

            Split( DapProtocol.Serialize( DapEvent.Thread( 4, "exited" ) ), out JObject thread );
            Assert.Equal( "exited", (string) thread["body"]["reason"] );
            Assert.Equal( 4, (int) thread["body"]["threadId"] );

            Split( DapProtocol.Serialize( DapEvent.Output( "hello\n" ) ), out JObject output );
            Assert.Equal( "console", (string) output["body"]["category"] );
            Assert.Equal( "hello\n", (string) output["body"]["output"] );

            Split( DapProtocol.Serialize( DapEvent.Terminated() ), out JObject terminated );
            Assert.Equal( "terminated", (string) terminated["event"] );
        }

        [Fact]
        public void WillReadARequestsSeqCommandAndArguments()
        {
            DapRequest request = DapProtocol.DeserializeRequest(
                Encoding.UTF8.GetBytes( "{\"seq\":9,\"type\":\"request\",\"command\":\"next\",\"arguments\":{\"threadId\":4}}" ) );

            Assert.Equal( 9, request.Seq );
            Assert.Equal( "next", request.Command );
            Assert.Equal( 4, DapProtocol.DeserializeArguments<NextArguments>( request.Arguments ).ThreadId );
        }

        [Fact]
        public void WillDefaultMissingFieldsAndTreatNullArgumentsAsAbsent()
        {
            DapRequest request = DapProtocol.DeserializeRequest( Encoding.UTF8.GetBytes( "{\"arguments\":null}" ) );

            Assert.Equal( 0, request.Seq );
            Assert.Equal( "", request.Command );
            Assert.Null( request.Arguments );
            Assert.Null( DapProtocol.DeserializeArguments<NextArguments>( request.Arguments ) );
        }

        [Fact]
        public void WillThrowOnABodyThatIsNotJson()
        {
            Assert.ThrowsAny<Exception>( () => DapProtocol.DeserializeRequest( Encoding.UTF8.GetBytes( "{oops" ) ) );
        }

        [Fact]
        public void WillWaitForTheRestOfASplitMessage()
        {
            byte[] framed = DapHarness.Frame( "{\"seq\":1,\"command\":\"threads\"}" );
            MemoryStream pending = new MemoryStream();

            // Every split point: header incomplete, header complete but body partial, ...
            for ( int i = 0; i < framed.Length - 1; i++ )
            {
                pending.WriteByte( framed[i] );
                Assert.False( Extract( pending, out _ ), $"extracted after only {i + 1} of {framed.Length} bytes" );
            }

            pending.WriteByte( framed[^1] );

            Assert.True( Extract( pending, out string json ) );
            Assert.Equal( "{\"seq\":1,\"command\":\"threads\"}", json );
            Assert.Equal( 0, pending.Length );
        }

        /// <summary>
        ///     Several messages in one read come out one at a time and in order, with a trailing partial
        ///     message kept (at the start of the buffer, where further reads append) for the next read.
        /// </summary>
        [Fact]
        public void WillExtractSeveralMessagesFromOneReadAndKeepTheRemainder()
        {
            byte[] third = DapHarness.Frame( "{\"seq\":3}" );
            MemoryStream pending = Pending( DapHarness.Frame( "{\"seq\":1}" ), DapHarness.Frame( "{\"seq\":2}" ),
                third[..10] );

            Assert.True( Extract( pending, out string first ) );
            Assert.True( Extract( pending, out string second ) );
            Assert.False( Extract( pending, out _ ) );

            Assert.Equal( "{\"seq\":1}", first );
            Assert.Equal( "{\"seq\":2}", second );
            Assert.Equal( 10, pending.Length );
            Assert.Equal( 10, pending.Position );

            pending.Write( third, 10, third.Length - 10 );

            Assert.True( Extract( pending, out string last ) );
            Assert.Equal( "{\"seq\":3}", last );
        }

        [Fact]
        public void WillTakeTheBodyLengthInBytesForMultiByteText()
        {
            const string json = "{\"command\":\"évaluer ✓ 日本\"}";
            MemoryStream pending = Pending( DapHarness.Frame( json ), DapHarness.Frame( "{\"seq\":2}" ) );

            Assert.True( Extract( pending, out string first ) );
            Assert.True( Extract( pending, out string second ) );

            Assert.Equal( json, first );
            Assert.Equal( "{\"seq\":2}", second );
        }

        [Fact]
        public void WillAcceptAnyHeaderCaseExtraHeadersAndSpacing()
        {
            byte[] body = Encoding.UTF8.GetBytes( "{\"seq\":1}" );
            byte[] header = Encoding.ASCII.GetBytes(
                $"Content-Type: application/vscode-jsonrpc; charset=utf-8\r\ncontent-length:   {body.Length}  \r\n\r\n" );

            Assert.True( Extract( Pending( header, body ), out string json ) );
            Assert.Equal( "{\"seq\":1}", json );
        }

        [Fact]
        public void WillExtractAnEmptyBody()
        {
            MemoryStream pending = Pending( Encoding.ASCII.GetBytes( "Content-Length: 0\r\n\r\n" ),
                DapHarness.Frame( "{\"seq\":2}" ) );

            Assert.True( Extract( pending, out string empty ) );
            Assert.Equal( "", empty );
            Assert.True( Extract( pending, out string next ) );
            Assert.Equal( "{\"seq\":2}", next );
        }
    }
}
