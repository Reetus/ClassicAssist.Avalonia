using System;
using System.Collections.Generic;
using System.Linq;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Puts the process-wide game state the tests share back to a known, empty world.
    ///     <para>
    ///         Call <see cref="Reset" /> from a test class constructor (and, where the class changes
    ///         anything else global, restore that in Dispose). Starting from a known state rather than
    ///         from whatever the previous test left is what keeps results independent of run order.
    ///     </para>
    /// </summary>
    internal static class EngineState
    {
        /// <summary>The client version most tests assume: modern packet formats, pre-7.0.104 data.</summary>
        public static readonly Version DefaultClientVersion = new Version( 7, 0, 45, 1 );

        public static void Reset()
        {
            Engine.Items = new ItemCollection( 0 );
            Engine.Mobiles = new MobileCollection( Engine.Items );
            Engine.Player = null;
            Engine.Journal.Clear();
            Engine.Gumps = new GumpCollection();
            Engine.Menus = new MenuCollection();
            Engine.Trade = new Trade();
            Engine.PacketWaitEntries = new PacketWaitEntries();
            Engine.Dispatcher = new InlineDispatcher();

            Engine.ClientVersion = DefaultClientVersion;
            Engine.Features = FeatureFlags.None;
            Engine.Connected = false;

            Engine.TargetExists = false;
            Engine.TargetSerial = 0;
            Engine.TargetType = default;
            Engine.TargetFlags = default;
            Engine.WaitingForTarget = false;
            Engine.InternalTarget = false;
            Engine.InternalTargetSerial = 0;
            Engine.LastTargetQueue.Clear();

            Engine.LastActionPacket = DateTime.MinValue;

            AliasCommands._aliases.Clear();
            AliasCommands._playerAliases.Clear();

            ActionPacketQueue.Clear();

            IncomingPacketHandlers.Initialize();
            OutgoingPacketHandlers.Initialize();
            IncomingPacketFilters.Initialize();
        }

        /// <summary>
        ///     Resets the world and installs a player with an empty backpack - the setup most command and
        ///     agent tests need.
        /// </summary>
        public static PlayerMobile ResetWithPlayer( int serial = 0x01, int backpackSerial = 0x40000001 )
        {
            Reset();

            PlayerMobile player = new PlayerMobile( serial );
            Item backpack = new Item( backpackSerial, serial )
            {
                Layer = Layer.Backpack, Container = new ItemCollection( backpackSerial )
            };

            player.SetLayer( Layer.Backpack, backpack.Serial );

            Engine.Items.Add( backpack );
            Engine.Mobiles.Add( player );
            Engine.Player = player;

            return player;
        }

        /// <summary>
        ///     Feeds a server-to-client packet through the real incoming handler table, as if it had just
        ///     arrived from the server.
        /// </summary>
        public static void Receive( byte[] packet )
        {
            PacketHandler handler = IncomingPacketHandlers.GetHandler( packet[0] ) ??
                                    throw new InvalidOperationException( $"No incoming handler for 0x{packet[0]:X2}" );

            handler.OnReceive( new PacketReader( packet, packet.Length, handler.Length > 0 ) );
        }

        /// <summary>
        ///     Feeds a client-to-server packet through the real outgoing handler table.
        /// </summary>
        public static void Send( byte[] packet )
        {
            PacketHandler handler = OutgoingPacketHandlers.GetHandler( packet[0] ) ??
                                    throw new InvalidOperationException( $"No outgoing handler for 0x{packet[0]:X2}" );

            handler.OnReceive( new PacketReader( packet, packet.Length, handler.Length > 0 ) );
        }
    }

    /// <summary>
    ///     Records every packet the assistant sends to the server or injects into the client for as long
    ///     as it lives. Use it with <c>using</c> instead of attaching handlers to the Engine events by hand:
    ///     the handler is removed even when an assert throws, so nothing leaks into later tests.
    /// </summary>
    internal sealed class PacketRecorder : IDisposable
    {
        private readonly List<byte[]> _sent = new List<byte[]>();
        private readonly List<byte[]> _received = new List<byte[]>();

        public PacketRecorder()
        {
            Engine.InternalPacketSentEvent += OnSent;
            Engine.InternalPacketReceivedEvent += OnReceived;
        }

        /// <summary>Packets sent to the server, oldest first.</summary>
        public IReadOnlyList<byte[]> Sent
        {
            get
            {
                lock ( _sent )
                {
                    return _sent.ToList();
                }
            }
        }

        /// <summary>Packets injected into the client, oldest first.</summary>
        public IReadOnlyList<byte[]> Received
        {
            get
            {
                lock ( _received )
                {
                    return _received.ToList();
                }
            }
        }

        public void Dispose()
        {
            Engine.InternalPacketSentEvent -= OnSent;
            Engine.InternalPacketReceivedEvent -= OnReceived;
        }

        /// <summary>Sent packets with the given id.</summary>
        public IReadOnlyList<byte[]> SentWithId( byte id )
        {
            return Sent.Where( p => p.Length > 0 && p[0] == id ).ToList();
        }

        /// <summary>Injected packets with the given id.</summary>
        public IReadOnlyList<byte[]> ReceivedWithId( byte id )
        {
            return Received.Where( p => p.Length > 0 && p[0] == id ).ToList();
        }

        private void OnSent( byte[] data, int length )
        {
            lock ( _sent )
            {
                _sent.Add( data.Take( length ).ToArray() );
            }
        }

        private void OnReceived( byte[] data, int length )
        {
            lock ( _received )
            {
                _received.Add( data.Take( length ).ToArray() );
            }
        }
    }
}
