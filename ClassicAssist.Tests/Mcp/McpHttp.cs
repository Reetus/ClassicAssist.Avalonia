using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClassicAssist.Tests.Mcp
{
    /// <summary>
    ///     A raw HTTP/1.1 client for the MCP server. Raw rather than HttpClient so a test can send exactly
    ///     the bytes it means to - a body split across writes, a missing header, a wrong verb - and read
    ///     back the status line and body as the server wrote them.
    /// </summary>
    internal static class McpHttp
    {
        internal sealed class Response
        {
            public int Status { get; set; }
            public string Headers { get; set; }
            public string Body { get; set; }
        }

        public static int FreePort()
        {
            TcpListener listener = new TcpListener( IPAddress.Loopback, 0 );

            listener.Start();

            try
            {
                return ( (IPEndPoint) listener.LocalEndpoint ).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        public static string PostRequest( string json, string contentType = "application/json", string extraHeaders = "" )
        {
            byte[] body = Encoding.UTF8.GetBytes( json );

            return "POST /mcp HTTP/1.1\r\nHost: localhost\r\n" +
                   ( contentType != null ? $"Content-Type: {contentType}\r\n" : "" ) + extraHeaders +
                   $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n" + json;
        }

        public static Task<Response> PostJson( int port, string json, CancellationToken cancellationToken )
        {
            return SendRaw( port, cancellationToken, PostRequest( json ) );
        }

        /// <summary>
        ///     Sends each part as a separate write (with a flush between), then reads the response to EOF.
        /// </summary>
        public static async Task<Response> SendRaw( int port, CancellationToken cancellationToken, params string[] parts )
        {
            using TcpClient client = new TcpClient();

            await client.ConnectAsync( IPAddress.Loopback, port, cancellationToken );

            using NetworkStream stream = client.GetStream();

            for ( int i = 0; i < parts.Length; i++ )
            {
                byte[] bytes = Encoding.UTF8.GetBytes( parts[i] );

                await stream.WriteAsync( bytes, cancellationToken );
                await stream.FlushAsync( cancellationToken );

                if ( i < parts.Length - 1 )
                {
                    // Let the server's first read complete on the partial request.
                    await Task.Delay( 50, cancellationToken );
                }
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );

            timeout.CancelAfter( TimeSpan.FromSeconds( 30 ) );

            MemoryStream received = new MemoryStream();
            byte[] buffer = new byte[16 * 1024];

            while ( true )
            {
                int read = await stream.ReadAsync( buffer, timeout.Token );

                if ( read == 0 )
                {
                    break;
                }

                received.Write( buffer, 0, read );
            }

            string text = Encoding.UTF8.GetString( received.ToArray() );
            int headerEnd = text.IndexOf( "\r\n\r\n", StringComparison.Ordinal );
            string head = headerEnd >= 0 ? text.Substring( 0, headerEnd ) : text;
            string[] statusLine = head.Split( "\r\n" )[0].Split( ' ' );

            return new Response
            {
                Status = statusLine.Length > 1 && int.TryParse( statusLine[1], out int status ) ? status : -1,
                Headers = head,
                Body = headerEnd >= 0 ? text.Substring( headerEnd + 4 ) : string.Empty
            };
        }
    }
}
