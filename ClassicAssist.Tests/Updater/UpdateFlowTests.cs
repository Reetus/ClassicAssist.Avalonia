#region License

// Copyright (C) 2026 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Tests.UOData;
using ClassicAssist.Updater;
using ClassicAssist.Updater.Models;
using ClassicAssist.Updater.Services;
using ClassicAssist.Updater.ViewModels;
using Xunit;

namespace ClassicAssist.Tests.Updater
{
    /// <summary>
    ///     A minimal HTTP/1.1 server on loopback serving fixed responses by path, one request per connection.
    /// </summary>
    internal sealed class LoopbackHttpServer : IDisposable
    {
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly TcpListener _listener = new TcpListener( IPAddress.Loopback, 0 );
        private readonly ConcurrentDictionary<string, (int Status, byte[] Body)> _routes =
            new ConcurrentDictionary<string, (int, byte[])>();

        private readonly Task _serve;

        public LoopbackHttpServer()
        {
            _listener.Start();
            _serve = Task.Run( ServeAsync );
        }

        public string BaseUrl => $"http://127.0.0.1:{( (IPEndPoint) _listener.LocalEndpoint ).Port}";

        public ConcurrentQueue<string> Requests { get; } = new ConcurrentQueue<string>();

        public void Route( string path, int status, byte[] body )
        {
            _routes[path] = ( status, body );
        }

        public void Dispose()
        {
            _cancel.Cancel();
            _listener.Stop();

            try
            {
                _serve.Wait( TimeSpan.FromSeconds( 2 ) );
            }
            catch ( AggregateException )
            {
                // Accept was cancelled.
            }

            _cancel.Dispose();
        }

        private async Task ServeAsync()
        {
            while ( !_cancel.IsCancellationRequested )
            {
                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync( _cancel.Token );
                }
                catch ( Exception )
                {
                    return;
                }

                _ = Task.Run( () => HandleAsync( client ) );
            }
        }

        private async Task HandleAsync( TcpClient client )
        {
            using ( client )
            {
                NetworkStream stream = client.GetStream();
                StringBuilder header = new StringBuilder();
                byte[] buffer = new byte[1];

                while ( !header.ToString().EndsWith( "\r\n\r\n", StringComparison.Ordinal ) )
                {
                    if ( await stream.ReadAsync( buffer ) == 0 )
                    {
                        return;
                    }

                    header.Append( (char) buffer[0] );
                }

                string path = header.ToString().Split( ' ' )[1];
                Requests.Enqueue( path );

                ( int status, byte[] body ) = _routes.TryGetValue( path, out ( int, byte[] ) route )
                    ? route
                    : ( 404, Array.Empty<byte>() );

                byte[] head = Encoding.ASCII.GetBytes( $"HTTP/1.1 {status} X\r\nContent-Length: {body.Length}\r\n" +
                                                       "Content-Type: application/octet-stream\r\nConnection: close\r\n\r\n" );

                await stream.WriteAsync( head );
                await stream.WriteAsync( body );
            }
        }
    }

    /// <summary>
    ///     The first stage of an update end to end against a loopback release feed: list releases, pick the
    ///     newest one for this platform, compare it with the installed version, download it, check its size and
    ///     extract it (zip or tar.gz) to a temp folder. The release feed URL comes from the settings, which is
    ///     the seam that makes this reachable; the HttpClients themselves are created inline. Also the second
    ///     stage's copy over the install, which must refuse to start while a file it would overwrite is in use
    ///     rather than leave a half-updated install.
    ///     <para>
    ///         Not covered: closing running clients (it kills processes and shows a dialog) and handing over to
    ///         the updater inside the package (Process.Start) - packages here never contain one.
    ///     </para>
    /// </summary>
    public class UpdateFlowTests : IDisposable
    {
        private readonly TempDirectory _directory = new TempDirectory();
        private readonly CommandLineOptions _originalOptions;
        private readonly LoopbackHttpServer _server = new LoopbackHttpServer();
        private readonly string _version;

        public UpdateFlowTests()
        {
            _originalOptions = App.CurrentOptions;

            App.CurrentOptions = new CommandLineOptions
            {
                CurrentVersion = "0.5.100.0",

                // Nothing runs from a path that does not exist, so no client is asked about or closed.
                Path = _directory.File( "no-install" )
            };

            // Unique per run: the extract folder is %TEMP%\CAUpdate-<version>.
            _version = $"0.5.{new Random().Next( 1000, 60000 )}.0";
        }

        public void Dispose()
        {
            App.CurrentOptions = _originalOptions;
            _server.Dispose();
            _directory.Dispose();

            TryDelete( Path.Combine( Path.GetTempPath(), $"CAUpdate-{_version}" ) );
            TryDelete( Path.Combine( Path.GetTempPath(), "ClassicAssist-Update.zip" ) );
            TryDelete( Path.Combine( Path.GetTempPath(), "ClassicAssist-Update.tar.gz" ) );
        }

        private static void TryDelete( string path )
        {
            try
            {
                if ( Directory.Exists( path ) )
                {
                    Directory.Delete( path, true );
                }
                else
                {
                    File.Delete( path );
                }
            }
            catch ( IOException )
            {
            }
        }

        private string ExtractPath => Path.Combine( Path.GetTempPath(), $"CAUpdate-{_version}" );

        private static byte[] ZipPackage()
        {
            using MemoryStream stream = new MemoryStream();

            using ( ZipArchive zip = new ZipArchive( stream, ZipArchiveMode.Create, true ) )
            {
                using ( StreamWriter writer = new StreamWriter( zip.CreateEntry( "ClassicAssist.dll" ).Open() ) )
                {
                    writer.Write( "new build" );
                }

                using ( StreamWriter writer = new StreamWriter( zip.CreateEntry( "ui/readme.txt" ).Open() ) )
                {
                    writer.Write( "nested" );
                }
            }

            return stream.ToArray();
        }

        private static byte[] TarGzPackage()
        {
            using MemoryStream stream = new MemoryStream();

            using ( GZipStream gzip = new GZipStream( stream, CompressionMode.Compress, true ) )
            using ( TarWriter writer = new TarWriter( gzip ) )
            {
                writer.WriteEntry( new PaxTarEntry( TarEntryType.RegularFile, "ClassicAssist.dll" )
                {
                    DataStream = new MemoryStream( Encoding.UTF8.GetBytes( "tar build" ) )
                } );
            }

            return stream.ToArray();
        }

        /// <summary>
        ///     A release list, newest first, each with a package for this platform at /pkg/&lt;tag&gt;&lt;ext&gt;.
        /// </summary>
        private void Publish( params (string Tag, bool Prerelease, byte[] Package, long Size, string Extension)[] releases )
        {
            StringBuilder json = new StringBuilder( "[" );

            foreach ( (string tag, bool prerelease, byte[] package, long size, string extension) in releases )
            {
                string path = $"/pkg/{tag}{extension}";
                _server.Route( path, 200, package );

                if ( json.Length > 1 )
                {
                    json.Append( ',' );
                }

                json.Append( $$"""
                    { "tag_name": "{{tag}}", "prerelease": {{( prerelease ? "true" : "false" )}}, "draft": false,
                      "assets": [ { "name": "ClassicAssist-{{tag}}-{{PlatformPackage.RuntimeIdentifier}}{{extension}}",
                                    "size": {{size}}, "browser_download_url": "{{_server.BaseUrl}}{{path}}" } ] }
                    """ );
            }

            json.Append( ']' );
            _server.Route( "/releases", 200, Encoding.UTF8.GetBytes( json.ToString() ) );
        }

        private async Task<string> CheckForUpdate( bool prereleases = false )
        {
            MainViewModel vm = new MainViewModel( true )
            {
                UpdaterSettings = new UpdaterSettings
                {
                    ReleasesURL = _server.BaseUrl + "/releases", InstallPrereleases = prereleases
                }
            };

            Task<string> task = (Task<string>) typeof( MainViewModel )
                .GetMethod( "CheckForUpdate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( vm, null );

            string result = await task;

            Assert.False( vm.IsUpdating );

            return result;
        }

        [Fact]
        public async Task WillDownloadAndExtractANewerRelease()
        {
            byte[] package = ZipPackage();
            Publish( ( _version, false, package, package.Length, ".zip" ) );

            string path = await CheckForUpdate();

            Assert.Equal( ExtractPath, path );
            Assert.Equal( "new build", File.ReadAllText( Path.Combine( path, "ClassicAssist.dll" ) ) );
            Assert.Equal( "nested", File.ReadAllText( Path.Combine( path, "ui", "readme.txt" ) ) );
        }

        /// <summary>
        ///     The package format is taken from the asset's extension, since the download is saved under a fixed
        ///     name.
        /// </summary>
        [Fact]
        public async Task WillExtractATarGzPackage()
        {
            byte[] package = TarGzPackage();
            Publish( ( _version, false, package, package.Length, ".tar.gz" ) );

            string path = await CheckForUpdate();

            Assert.Equal( "tar build", File.ReadAllText( Path.Combine( path, "ClassicAssist.dll" ) ) );
        }

        [Fact]
        public async Task WillNotDownloadWhenAlreadyUpToDate()
        {
            byte[] package = ZipPackage();
            Publish( ( "0.5.100.0", false, package, package.Length, ".zip" ) );

            Assert.Null( await CheckForUpdate() );
            Assert.Equal( new[] { "/releases" }, _server.Requests.ToArray() );
        }

        /// <summary>
        ///     --force reinstalls the current version.
        /// </summary>
        [Fact]
        public async Task WillReinstallTheSameVersionWhenForced()
        {
            App.CurrentOptions.CurrentVersion = _version;
            App.CurrentOptions.Force = true;

            byte[] package = ZipPackage();
            Publish( ( _version, false, package, package.Length, ".zip" ) );

            Assert.Equal( ExtractPath, await CheckForUpdate() );
        }

        /// <summary>
        ///     The newest release is a prerelease: skipped by default, taken when prereleases are enabled.
        /// </summary>
        [Fact]
        public async Task WillOnlyTakeAPrereleaseWhenEnabled()
        {
            byte[] package = ZipPackage();
            Publish( ( _version, true, package, package.Length, ".zip" ), ( "0.5.99.0", false, package, package.Length, ".zip" ) );

            Assert.Null( await CheckForUpdate() );
            Assert.DoesNotContain( $"/pkg/{_version}.zip", _server.Requests );

            Assert.Equal( ExtractPath, await CheckForUpdate( true ) );
        }

        /// <summary>
        ///     A truncated download must not be extracted over a working install.
        /// </summary>
        [Fact]
        public async Task WillRejectADownloadOfTheWrongSize()
        {
            byte[] package = ZipPackage();
            Publish( ( _version, false, package, package.Length + 10, ".zip" ) );

            Assert.Null( await CheckForUpdate() );
            Assert.Contains( $"/pkg/{_version}.zip", _server.Requests );
            Assert.False( Directory.Exists( ExtractPath ) );
        }

        [Fact]
        public async Task WillGiveUpQuietlyWhenTheFeedFails()
        {
            _server.Route( "/releases", 500, Encoding.UTF8.GetBytes( "boom" ) );

            Assert.Null( await CheckForUpdate() );

            _server.Route( "/releases", 200, Encoding.UTF8.GetBytes( "[]" ) );

            Assert.Null( await CheckForUpdate() );
        }

        /// <summary>
        ///     The second stage copies the package over the install, including subfolders, and unpacks the
        ///     nested Modules.zip into Modules.
        /// </summary>
        [Fact]
        public void WillCopyThePackageOverTheInstall()
        {
            string update = Directory.CreateDirectory( _directory.File( "update" ) ).FullName;
            string install = Directory.CreateDirectory( _directory.File( "install" ) ).FullName;

            File.WriteAllText( Path.Combine( update, "ClassicAssist.dll" ), "new" );
            Directory.CreateDirectory( Path.Combine( update, "ui" ) );
            File.WriteAllText( Path.Combine( update, "ui", "ClassicAssist.Shared.dll" ), "new shared" );
            File.WriteAllText( Path.Combine( install, "ClassicAssist.dll" ), "old" );
            File.WriteAllText( Path.Combine( install, "settings.json" ), "keep me" );

            using ( ZipArchive zip = ZipFile.Open( Path.Combine( update, "Modules.zip" ), ZipArchiveMode.Create ) )
            using ( StreamWriter writer = new StreamWriter( zip.CreateEntry( "lib/module.py" ).Open() ) )
            {
                writer.Write( "module" );
            }

            Install( update, install );

            Assert.Equal( "new", File.ReadAllText( Path.Combine( install, "ClassicAssist.dll" ) ) );
            Assert.Equal( "new shared", File.ReadAllText( Path.Combine( install, "ui", "ClassicAssist.Shared.dll" ) ) );
            Assert.Equal( "keep me", File.ReadAllText( Path.Combine( install, "settings.json" ) ) );
            Assert.Equal( "module", File.ReadAllText( Path.Combine( install, "Modules", "lib", "module.py" ) ) );
        }

        /// <summary>
        ///     One file in use means nothing is copied at all - not the files before it either.
        /// </summary>
        [Fact]
        public void WillCopyNothingWhileAnInstalledFileIsInUse()
        {
            if ( !OperatingSystem.IsWindows() )
            {
                // Only Windows refuses to overwrite an open file.
                return;
            }

            string update = Directory.CreateDirectory( _directory.File( "update" ) ).FullName;
            string install = Directory.CreateDirectory( _directory.File( "install" ) ).FullName;

            File.WriteAllText( Path.Combine( update, "A.dll" ), "new a" );
            File.WriteAllText( Path.Combine( update, "B.dll" ), "new b" );
            File.WriteAllText( Path.Combine( install, "A.dll" ), "old a" );
            File.WriteAllText( Path.Combine( install, "B.dll" ), "old b" );

            using ( new FileStream( Path.Combine( install, "B.dll" ), FileMode.Open, FileAccess.Read, FileShare.None ) )
            {
                Install( update, install );
            }

            Assert.Equal( "old a", File.ReadAllText( Path.Combine( install, "A.dll" ) ) );
            Assert.Equal( "old b", File.ReadAllText( Path.Combine( install, "B.dll" ) ) );
        }

        private static void Install( string update, string install )
        {
            typeof( MainViewModel ).GetMethod( "Install", BindingFlags.Instance | BindingFlags.NonPublic )!
                .Invoke( new MainViewModel( true ), new object[] { update, install } );
        }
    }
}
