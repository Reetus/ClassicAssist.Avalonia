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
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Launcher.Models;
using ClassicAssist.Launcher.ViewModels;
using Xunit;

namespace ClassicAssist.Tests.Launcher
{
    /// <summary>
    ///     The shard list dialog. Removing a preset must only hide it (so the hidden state is saved and the
    ///     remote list does not bring it back), while removing a user shard deletes it; and the status column
    ///     comes from the RunUO status protocol, read over TCP and cut down to the client count by the shard's
    ///     regex. Every test starts from an empty shard list, because the constructor refreshes every shard -
    ///     with the built-in presets that would mean connecting to and pinging real servers.
    /// </summary>
    public class ShardsViewModelTests : IDisposable
    {
        private readonly ShardManagerScope _shards = new ShardManagerScope( true );

        public void Dispose()
        {
            _shards.Dispose();
        }

        [Fact]
        public void WillHideAPresetButDeleteAUserShard()
        {
            ShardEntry preset = new ShardEntry { Name = "Preset", IsPreset = true };
            ShardEntry user = new ShardEntry { Name = "Mine" };

            ShardsViewModel vm = new ShardsViewModel();

            _shards.Manager.Shards.Add( preset );
            _shards.Manager.Shards.Add( user );

            vm.RemoveCommand.Execute( preset );
            vm.RemoveCommand.Execute( user );

            Assert.True( preset.Deleted );
            Assert.Contains( preset, _shards.Manager.Shards );
            Assert.DoesNotContain( user, _shards.Manager.Shards );
            Assert.Empty( _shards.Manager.VisibleShards );
        }

        [Fact]
        public void WillAddAPlaceholderUserShard()
        {
            ShardsViewModel vm = new ShardsViewModel();

            vm.AddCommand.Execute( null );

            ShardEntry added = Assert.Single( _shards.Manager.Shards );
            Assert.False( added.IsPreset );
            Assert.Equal( "localhost", added.Address );
            Assert.Equal( 2593, added.Port );
        }

        [Fact]
        public void WillOnlyRemoveWithASelection()
        {
            ShardsViewModel vm = new ShardsViewModel();

            Assert.False( vm.RemoveCommand.CanExecute( null ) );

            vm.SelectedShard = new ShardEntry();

            Assert.True( vm.RemoveCommand.CanExecute( null ) );
        }

        /// <summary>
        ///     The result is set before the close is requested, so whoever awaits the dialog reads the final
        ///     answer.
        /// </summary>
        [Fact]
        public void WillSetTheResultBeforeAskingToClose()
        {
            ShardsViewModel vm = new ShardsViewModel();
            bool? resultAtClose = null;

            vm.CloseRequested += () => resultAtClose = vm.DialogResult;

            vm.OKCommand.Execute( null );
            Assert.True( resultAtClose );

            vm.CancelCommand.Execute( null );
            Assert.False( resultAtClose );
        }

        [Fact]
        public async Task WillReadTheStatusReplyFromTheShard()
        {
            using StatusServer server = new StatusServer( "Name=Test, Age=12, Clients=42, Items=100, Chars=5, Mem=10K\0\0" );

            string status = await new ShardsViewModel().GetStatus( server.Shard() );

            Assert.Equal( "Name=Test, Age=12, Clients=42, Items=100, Chars=5, Mem=10K", status );
            Assert.Equal( new byte[] { 0x7F, 0x00, 0x00, 0x7F, 0xF1, 0x00, 0x04, 0xFF }, server.Request );
        }

        [Fact]
        public async Task WillReportUnknownWithoutAStatusProtocolOrAConnection()
        {
            ShardsViewModel vm = new ShardsViewModel();

            Assert.Equal( "Unknown",
                await vm.GetStatus( new ShardEntry { Address = "127.0.0.1", Port = 1, HasStatusProtocol = false } ) );

            // A port nothing listens on: refused straight away.
            TcpListener closed = new TcpListener( IPAddress.Loopback, 0 );
            closed.Start();
            int port = ( (IPEndPoint) closed.LocalEndpoint ).Port;
            closed.Stop();

            Assert.Equal( "Unknown", await vm.GetStatus( new ShardEntry { Address = "127.0.0.1", Port = port } ) );
        }

        /// <summary>
        ///     Refresh shows just the client count for a shard that answers, and "-" for one with no status
        ///     protocol.
        /// </summary>
        [Fact]
        public void WillShowTheClientCountAfterRefresh()
        {
            using StatusServer server = new StatusServer( "Name=Test, Age=12, Clients=42, Items=100" );

            ShardEntry live = server.Shard();
            ShardEntry silent = new ShardEntry { Name = "silent", Address = "127.0.0.1", Port = 1, HasStatusProtocol = false };

            _shards.Manager.Shards.Add( live );
            _shards.Manager.Shards.Add( silent );

            Assert.True( WaitForStatus( () => new ShardsViewModel(), live, silent ) );

            Assert.Equal( "42", live.Status );
            Assert.Equal( "-", silent.Status );
        }

        private static bool WaitForStatus( Func<ShardsViewModel> create, params ShardEntry[] shards )
        {
            using CountdownEvent remaining = new CountdownEvent( shards.Length );

            void OnChanged( object sender, PropertyChangedEventArgs args )
            {
                if ( args.PropertyName == nameof( ShardEntry.Status ) )
                {
                    ( (ShardEntry) sender ).PropertyChanged -= OnChanged;
                    remaining.Signal();
                }
            }

            foreach ( ShardEntry shard in shards )
            {
                shard.PropertyChanged += OnChanged;
            }

            try
            {
                create();

                return remaining.Wait( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );
            }
            finally
            {
                foreach ( ShardEntry shard in shards )
                {
                    shard.PropertyChanged -= OnChanged;
                }
            }
        }

        /// <summary>
        ///     Answers one status request on loopback with a fixed reply.
        /// </summary>
        private sealed class StatusServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener( IPAddress.Loopback, 0 );
            private readonly Task _serve;

            public StatusServer( string reply )
            {
                _listener.Start();
                _serve = Task.Run( async () =>
                {
                    using TcpClient client = await _listener.AcceptTcpClientAsync();
                    NetworkStream stream = client.GetStream();
                    byte[] request = new byte[8];
                    int read = 0;

                    while ( read < request.Length )
                    {
                        int count = await stream.ReadAsync( request.AsMemory( read ) );

                        if ( count == 0 )
                        {
                            break;
                        }

                        read += count;
                    }

                    Request = request;

                    byte[] bytes = Encoding.ASCII.GetBytes( reply );
                    await stream.WriteAsync( bytes );
                } );
            }

            public byte[] Request { get; private set; }

            public ShardEntry Shard()
            {
                return new ShardEntry
                {
                    Name = "live", Address = "127.0.0.1", Port = ( (IPEndPoint) _listener.LocalEndpoint ).Port
                };
            }

            public void Dispose()
            {
                _listener.Stop();

                try
                {
                    _serve.Wait( TimeSpan.FromSeconds( 1 ) );
                }
                catch ( AggregateException )
                {
                    // Stopped before a client connected.
                }
            }
        }
    }
}
