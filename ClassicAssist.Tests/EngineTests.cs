using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Data;
using ClassicAssist.Data.Friends;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     The callbacks the game client's plugin makes into the engine over RPC
    ///     (<see cref="Engine.PluginMethods" />): packets in each direction reaching the packet path or
    ///     being swallowed by a filter, and connect/disconnect updating the engine's state and raising its
    ///     events. Plus party-invite auto-accept, the one place a received packet makes the assistant
    ///     answer the server on its own.
    ///     <para>
    ///         The verdict these return decides whether the client sees or sends the packet at all, so a
    ///         wrong answer either leaks a packet a filter was meant to hide or silently drops game
    ///         traffic.
    ///     </para>
    /// </summary>
    public class EngineTests : IDisposable
    {
        private const int TIMEOUT_MS = 5000;

        // 0xBF / 0x06 party / 0x07 invitation from leader 0x00aabbcc
        private static readonly byte[] _partyInvite = { 0xBF, 0x00, 0x0A, 0x00, 0x06, 0x07, 0x00, 0xAA, 0xBB, 0xCC };

        private readonly ConcurrentQueue<byte[]> _incoming = new ConcurrentQueue<byte[]>();
        private readonly ThreadQueue<Packet> _incomingQueue;
        private readonly ConcurrentQueue<byte[]> _outgoing = new ConcurrentQueue<byte[]>();
        private readonly ThreadQueue<Packet> _outgoingQueue;
        private readonly bool _savedAutoAccept;
        private readonly bool _savedAutoAcceptFriends;
        private readonly bool _savedIncludeParty;
        private readonly ThreadQueue<Packet> _savedIncoming;
        private readonly bool _savedInstalled;
        private readonly ThreadQueue<Packet> _savedOutgoing;

        public EngineTests()
        {
            EngineState.Reset();

            _savedIncoming = Engine.IncomingQueue;
            _savedOutgoing = Engine.OutgoingQueue;
            _savedInstalled = Engine.Installed;
            _savedAutoAccept = Options.CurrentOptions.AutoAcceptPartyInvite;
            _savedAutoAcceptFriends = Options.CurrentOptions.AutoAcceptPartyOnlyFromFriends;
            _savedIncludeParty = Options.CurrentOptions.IncludePartyMembersInFriends;

            // Stand-ins for the engine's packet workers, so the test sees exactly what reached them.
            _incomingQueue = new ThreadQueue<Packet>( p => _incoming.Enqueue( p.GetPacket() ) );
            _outgoingQueue = new ThreadQueue<Packet>( p => _outgoing.Enqueue( p.GetPacket() ) );

            Engine.IncomingQueue = _incomingQueue;
            Engine.OutgoingQueue = _outgoingQueue;
            Engine.Installed = true;
            Options.CurrentOptions.IncludePartyMembersInFriends = false;
        }

        public void Dispose()
        {
            Engine.ClearSendPreFilter();
            Engine.ClearReceiveFilter();

            Engine.IncomingQueue = _savedIncoming;
            Engine.OutgoingQueue = _savedOutgoing;
            Engine.Installed = _savedInstalled;
            Engine.Connected = false;

            Options.CurrentOptions.AutoAcceptPartyInvite = _savedAutoAccept;
            Options.CurrentOptions.AutoAcceptPartyOnlyFromFriends = _savedAutoAcceptFriends;
            Options.CurrentOptions.IncludePartyMembersInFriends = _savedIncludeParty;

            _incomingQueue.Dispose();
            _outgoingQueue.Dispose();
        }

        [Fact]
        public async Task WillPassAnUnfilteredSentPacketToTheClientAndThePacketPath()
        {
            byte[] packet = { 0x06, 0xAA, 0xBB, 0xCC, 0xDD };

            ( bool pass, byte[] _, int modifiedLength ) =
                await new Engine.PluginMethods().OnPacketSend( packet, packet.Length, 0 );

            Assert.True( pass );
            Assert.Equal( 0, modifiedLength );
            Assert.True( SpinWait.SpinUntil( () => !_outgoing.IsEmpty, TIMEOUT_MS ) );
            Assert.Equal( packet, _outgoing.Single() );
        }

        [Fact]
        public async Task WillSwallowASentPacketMatchedByAPreFilter()
        {
            byte[] packet = { 0x06, 0xAA, 0xBB, 0xCC, 0xDD };
            byte[] filtered = null;
            byte[] actioned = null;

            void OnFiltered( byte[] data, int length )
            {
                filtered = data.Take( length ).ToArray();
            }

            Engine.SentPacketFilteredEvent += OnFiltered;

            try
            {
                Engine.AddSendPreFilter( new PacketFilterInfo( 0x06, ( data, _ ) => actioned = data ) );

                ( bool pass, byte[] _, int _ ) = await new Engine.PluginMethods().OnPacketSend( packet, packet.Length, 0 );

                Assert.False( pass );
            }
            finally
            {
                Engine.SentPacketFilteredEvent -= OnFiltered;
            }

            Assert.Equal( packet, filtered );
            Assert.Equal( packet, actioned );

            // Filtered before the packet path: nothing downstream sees it.
            Thread.Sleep( 50 );
            Assert.Empty( _outgoing );
        }

        [Fact]
        public async Task WillPassAnUnfilteredReceivedPacketToTheClientAndThePacketPath()
        {
            // 0x2F swing: attacker 0x11223344, defender 0x55667788
            byte[] packet = { 0x2F, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };

            ( bool pass, byte[] _, int _ ) = await new Engine.PluginMethods().OnPacketReceive( packet, packet.Length, 0 );

            Assert.True( pass );
            Assert.True( SpinWait.SpinUntil( () => !_incoming.IsEmpty, TIMEOUT_MS ) );
            Assert.Equal( packet, _incoming.Single() );
        }

        [Fact]
        public async Task WillSwallowAReceivedPacketMatchedByAFilter()
        {
            byte[] packet = { 0x2F, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
            byte[] filtered = null;

            void OnFiltered( byte[] data, int length )
            {
                filtered = data.Take( length ).ToArray();
            }

            Engine.ReceivedPacketFilteredEvent += OnFiltered;

            try
            {
                Engine.AddReceiveFilter( new PacketFilterInfo( 0x2F ) );

                ( bool pass, byte[] _, int _ ) = await new Engine.PluginMethods().OnPacketReceive( packet, packet.Length, 0 );

                Assert.False( pass );
            }
            finally
            {
                Engine.ReceivedPacketFilteredEvent -= OnFiltered;
            }

            Assert.Equal( packet, filtered );

            Thread.Sleep( 50 );
            Assert.Empty( _incoming );
        }

        /// <summary>
        ///     Before install completes there is nothing to run a packet against: it must be let through
        ///     to the client untouched rather than dropped.
        /// </summary>
        [Fact]
        public async Task WillPassPacketsUntouchedBeforeInstall()
        {
            Engine.Installed = false;

            byte[] packet = { 0x2F, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
            Engine.AddReceiveFilter( new PacketFilterInfo( 0x2F ) );

            ( bool pass, byte[] _, int _ ) = await new Engine.PluginMethods().OnPacketReceive( packet, packet.Length, 0 );

            Assert.True( pass );
            Thread.Sleep( 50 );
            Assert.Empty( _incoming );
        }

        [Fact]
        public void OnConnectedWillSetConnectedAndRaiseConnectedEvent()
        {
            int raised = 0;
            bool connectedWhenRaised = false;

            void OnConnected()
            {
                raised++;
                connectedWhenRaised = Engine.Connected;
            }

            Engine.ConnectedEvent += OnConnected;

            try
            {
                new Engine.PluginMethods().OnConnected();
            }
            finally
            {
                Engine.ConnectedEvent -= OnConnected;
            }

            Assert.True( Engine.Connected );
            Assert.Equal( 1, raised );
            Assert.True( connectedWhenRaised );
        }

        /// <summary>
        ///     A disconnect ends the session: the world, the player and the connected flag are cleared
        ///     before anyone hears about it, so no subscriber acts on the old world.
        /// </summary>
        [Fact]
        public void OnDisconnectedWillClearTheWorldAndRaiseDisconnectedEvent()
        {
            EngineState.ResetWithPlayer();
            Engine.Mobiles.Add( new Mobile( 0x22 ) );
            Engine.Connected = true;

            int raised = 0;
            bool clearedWhenRaised = false;

            void OnDisconnected()
            {
                raised++;
                clearedWhenRaised = !Engine.Connected && Engine.Player == null && !Engine.Items.Any() &&
                                    !Engine.Mobiles.Any();
            }

            Engine.DisconnectedEvent += OnDisconnected;

            try
            {
                new Engine.PluginMethods().OnDisconnected();
            }
            finally
            {
                Engine.DisconnectedEvent -= OnDisconnected;
            }

            Assert.False( Engine.Connected );
            Assert.Null( Engine.Player );
            Assert.Equal( 1, raised );
            Assert.True( clearedWhenRaised );
        }

        [Fact]
        public void WontRaiseConnectionEventsBeforeInstall()
        {
            Engine.Installed = false;
            int raised = 0;

            void OnEvent()
            {
                raised++;
            }

            Engine.ConnectedEvent += OnEvent;
            Engine.DisconnectedEvent += OnEvent;

            try
            {
                Engine.PluginMethods plugin = new Engine.PluginMethods();
                plugin.OnConnected();
                plugin.OnDisconnected();
            }
            finally
            {
                Engine.ConnectedEvent -= OnEvent;
                Engine.DisconnectedEvent -= OnEvent;
            }

            Assert.Equal( 0, raised );
            Assert.False( Engine.Connected );
        }

        [Fact]
        public void WillAutoAcceptPartyInvite()
        {
            Options.CurrentOptions.AutoAcceptPartyInvite = true;
            Options.CurrentOptions.AutoAcceptPartyOnlyFromFriends = false;

            using PacketRecorder recorder = new PacketRecorder();

            EngineState.Receive( _partyInvite );

            // 0xBF / 0x06 party / 0x08 accept, leader serial
            byte[] accept = Assert.Single( recorder.SentWithId( 0xBF ) );
            Assert.Equal( new byte[] { 0xBF, 0x00, 0x0A, 0x00, 0x06, 0x08, 0x00, 0xAA, 0xBB, 0xCC }, accept );
        }

        [Fact]
        public void WontAutoAcceptPartyInviteWhenDisabled()
        {
            Options.CurrentOptions.AutoAcceptPartyInvite = false;
            Options.CurrentOptions.AutoAcceptPartyOnlyFromFriends = false;

            using PacketRecorder recorder = new PacketRecorder();

            EngineState.Receive( _partyInvite );

            Assert.Empty( recorder.Sent );
        }

        [Fact]
        public void WontAutoAcceptPartyInviteFromNonFriend()
        {
            Options.CurrentOptions.AutoAcceptPartyInvite = true;
            Options.CurrentOptions.AutoAcceptPartyOnlyFromFriends = true;

            using PacketRecorder recorder = new PacketRecorder();

            EngineState.Receive( _partyInvite );

            Assert.Empty( recorder.Sent );
        }

        [Fact]
        public void WillAutoAcceptPartyInviteFromFriend()
        {
            Options.CurrentOptions.AutoAcceptPartyInvite = true;
            Options.CurrentOptions.AutoAcceptPartyOnlyFromFriends = true;

            FriendEntry friend = new FriendEntry { Name = "Test", Serial = 0xAABBCC };
            Options.CurrentOptions.Friends.Add( friend );

            try
            {
                using PacketRecorder recorder = new PacketRecorder();

                EngineState.Receive( _partyInvite );

                Assert.Single( recorder.SentWithId( 0xBF ) );
            }
            finally
            {
                Options.CurrentOptions.Friends.Remove( friend );
            }
        }
    }
}
