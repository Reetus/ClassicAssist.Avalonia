using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ClassicAssist.Data;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.Tests.Network.Incoming
{
    /// <summary>
    ///     Common setup for the server-to-client packet tests: a known world with a player and an empty
    ///     backpack, a fresh <see cref="Options" /> instance, a small fixed cliloc table, and no leftovers in
    ///     the static stores the handlers write to but <see cref="EngineState.Reset" /> does not clear
    ///     (the property cache, quest pointers, prompts, shards). Everything is put back in Dispose.
    /// </summary>
    public abstract class IncomingPacketTestBase : IDisposable
    {
        protected const int PLAYER = 0x00000101;
        protected const int BACKPACK = 0x40000101;

        protected static readonly Dictionary<int, string> TestClilocs = new Dictionary<int, string>
        {
            { 500000, "plain message" },
            { 500001, "You see ~1_NAME~ with ~2_COUNT~" },
            { 1050045, "~1_PREFIX~~2_NAME~~3_SUFFIX~" },
            { 1060000, "Item Name" },
            { 1060001, "charges" },
            { 1060002, "slaying" }
        };

        private static readonly FieldInfo _clilocField =
            typeof( Cliloc ).GetField( "_lazyClilocList", BindingFlags.NonPublic | BindingFlags.Static );

        private readonly object _originalClilocs;
        private readonly Options _originalOptions;
        private readonly List<ShardEntry> _originalShards;

        protected IncomingPacketTestBase()
        {
            _originalOptions = Options.CurrentOptions;
            Options.CurrentOptions = new Options();

            _originalClilocs = _clilocField?.GetValue( null );
            Cliloc.Initialize( () => new Dictionary<int, string>( TestClilocs ) );

            _originalShards = Engine.Shards;

            IncomingPacketHandlers.PropertyCache.Clear();
            Engine.QuestPointers.Clear();
            Engine.LastPromptID = 0;
            Engine.LastPromptSerial = 0;
            Engine.LastPromptType = 0;
            Engine.GumpList.Clear();

            Player = EngineState.ResetWithPlayer( PLAYER, BACKPACK );
            Player.Name = "Player";
            Player.X = 1000;
            Player.Y = 1000;
        }

        protected PlayerMobile Player { get; }

        protected Item Backpack => Engine.Items.GetItem( BACKPACK );

        public virtual void Dispose()
        {
            Options.CurrentOptions = _originalOptions;

            if ( _originalClilocs != null )
            {
                _clilocField.SetValue( null, _originalClilocs );
            }

            Engine.Shards = _originalShards;

            IncomingPacketHandlers.PropertyCache.Clear();
            Engine.QuestPointers.Clear();
            Engine.GumpList.Clear();

            EngineState.Reset();
        }

        /// <summary>A fixed-length packet: the id, then whatever <paramref name="body" /> writes.</summary>
        protected static byte[] Fixed( byte id, Action<PacketWriter> body )
        {
            using PacketWriter writer = new PacketWriter();

            writer.Write( id );
            body( writer );

            return writer.ToArray();
        }

        /// <summary>A variable-length packet: the id, its total length, then whatever <paramref name="body" /> writes.</summary>
        protected static byte[] Dynamic( byte id, Action<PacketWriter> body )
        {
            using PacketWriter writer = new PacketWriter();

            writer.Write( id );
            writer.Write( (short) 0 );
            body( writer );

            writer.Seek( 1, SeekOrigin.Begin );
            writer.Write( (short) writer.Length );
            writer.Seek( 0, SeekOrigin.End );

            return writer.ToArray();
        }

        /// <summary>A 0xBF extended command with the given sub-command.</summary>
        protected static byte[] Extended( int subCommand, Action<PacketWriter> body )
        {
            return Dynamic( 0xBF, w =>
            {
                w.Write( (short) subCommand );
                body( w );
            } );
        }

        protected static void Receive( byte[] packet )
        {
            EngineState.Receive( packet );
        }

        /// <summary>The 20 bytes an entry takes in 0x25 (and in 0x3C), in the form with the grid index.</summary>
        protected static void WriteContainerEntry( PacketWriter w, int serial, int id, int amount, int x, int y,
            int container, int hue, int grid = 0, bool withGrid = true )
        {
            w.Write( serial );
            w.Write( (short) id );
            w.Write( (byte) 0 ); // item id offset
            w.Write( (short) amount );
            w.Write( (short) x );
            w.Write( (short) y );

            if ( withGrid )
            {
                w.Write( (byte) grid );
            }

            w.Write( container );
            w.Write( (short) hue );
        }

        protected static byte[] AddToContainer( int serial, int id, int amount, int container, int hue = 0, int x = 44,
            int y = 65 )
        {
            return Fixed( 0x25, w => WriteContainerEntry( w, serial, id, amount, x, y, container, hue ) );
        }

        protected static byte[] ContainerContents( params (int Serial, int ID, int Amount, int Container)[] entries )
        {
            return Dynamic( 0x3C, w =>
            {
                w.Write( (short) entries.Length );

                foreach ( (int serial, int id, int amount, int container) in entries )
                {
                    WriteContainerEntry( w, serial, id, amount, 44, 65, container, 0 );
                }
            } );
        }

        protected static byte[] EquipItem( int serial, int id, Layer layer, int mobile, int hue = 0 )
        {
            return Fixed( 0x2E, w =>
            {
                w.Write( serial );
                w.Write( (short) id );
                w.Write( (byte) 0 );
                w.Write( (byte) layer );
                w.Write( mobile );
                w.Write( (short) hue );
            } );
        }

        protected static byte[] DeleteObject( int serial )
        {
            return Fixed( 0x1D, w => w.Write( serial ) );
        }

        /// <summary>
        ///     0x78 in the 7.0.33.1+ form: every equipment entry carries its hue. <paramref name="oldStyle" />
        ///     writes the older form, where the hue is present only when the id has its 0x8000 bit set.
        /// </summary>
        protected static byte[] MobileIncoming( int serial, int body, int x, int y, int z, int direction, int hue,
            MobileStatus status, Notoriety notoriety, bool oldStyle,
            params (int Serial, int ID, Layer Layer, int Hue)[] equipment )
        {
            return Dynamic( 0x78, w =>
            {
                w.Write( serial );
                w.Write( (short) body );
                w.Write( (short) x );
                w.Write( (short) y );
                w.Write( (sbyte) z );
                w.Write( (byte) direction );
                w.Write( (short) hue );
                w.Write( (byte) status );
                w.Write( (byte) notoriety );

                foreach ( (int itemSerial, int id, Layer layer, int itemHue) in equipment )
                {
                    w.Write( itemSerial );

                    if ( oldStyle )
                    {
                        w.Write( (short) ( itemHue != 0 ? id | 0x8000 : id ) );
                        w.Write( (byte) layer );

                        if ( itemHue != 0 )
                        {
                            w.Write( (short) itemHue );
                        }
                    }
                    else
                    {
                        w.Write( (short) id );
                        w.Write( (byte) layer );
                        w.Write( (short) itemHue );
                    }
                }

                w.Write( 0 );
            } );
        }

        protected static Mobile AddMobile( int serial, string name = "Mobile" )
        {
            Mobile mobile = new Mobile( serial ) { Name = name, ID = 0x190, X = 1001, Y = 1001 };
            Engine.Mobiles.Add( mobile );

            return mobile;
        }
    }
}
