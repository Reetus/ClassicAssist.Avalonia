using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClassicAssist.Data;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests.Network.Incoming
{
    /// <summary>
    ///     World items, equipment, deletion and properties: 0x1A (the old layout, where flag bits in the
    ///     serial, graphic, x and y say which optional fields follow), 0xF3, 0x2E, 0x1D, 0xD6 and the 0xBF 0x10
    ///     equipment info. A wrong flag check in 0x1A reads every later field from the wrong offset, and the
    ///     equip / delete handlers keep the layer table, the equipment list and the item store in step - if
    ///     one of them is missed, a removed weapon is still "equipped" for every macro that asks.
    /// </summary>
    public class ItemPacketTests : IncomingPacketTestBase
    {
        private const int ITEM = 0x40000401;
        private const int MOBILE = 0x00000402;

        [Fact]
        public void WillParseOldWorldItemWithNoOptionalFields()
        {
            Receive( WorldItem( ITEM, 0x0EED, 1500, 1600, 5 ) );

            Item item = Engine.Items.GetItem( ITEM );

            Assert.NotNull( item );
            Assert.Equal( 0x0EED, item.ID );
            Assert.Equal( 1, item.Count );
            Assert.Equal( 1500, item.X );
            Assert.Equal( 1600, item.Y );
            Assert.Equal( 5, item.Z );
            Assert.Equal( 0, item.Hue );
            Assert.Equal( 0, item.ArtDataID );
            Assert.Equal( 0, item.Owner );
        }

        /// <summary>
        ///     Every optional field at once: amount (serial bit 31), stack increment (graphic 0x8000),
        ///     direction (x 0x8000), hue (y 0x8000) and flags (y 0x4000). Each one present shifts the offset of
        ///     every field after it.
        /// </summary>
        [Fact]
        public void WillParseOldWorldItemWithEveryOptionalField()
        {
            Receive( WorldItem( ITEM, 0x0EED, 1500, 1600, -3, amount: 750, increment: 2,
                direction: Direction.West, hue: 0x0481, flags: 0x20 ) );

            Item item = Engine.Items.GetItem( ITEM );

            Assert.NotNull( item );
            Assert.Equal( 0x0EED + 2, item.ID );
            Assert.Equal( 750, item.Count );
            Assert.Equal( 1500, item.X );
            Assert.Equal( 1600, item.Y );
            Assert.Equal( -3, item.Z );
            Assert.Equal( Direction.West, item.Direction );
            Assert.Equal( 0x0481, item.Hue );
            Assert.Equal( 0x20, item.Flags );
        }

        [Theory]
        [InlineData( true, false, false, false )]
        [InlineData( false, true, false, false )]
        [InlineData( false, false, true, false )]
        [InlineData( false, false, false, true )]
        [InlineData( true, false, true, false )]
        [InlineData( false, true, false, true )]
        public void WillParseOldWorldItemWithAnyCombinationOfOptionalFields( bool withAmount, bool withDirection,
            bool withHue, bool withFlags )
        {
            Receive( WorldItem( ITEM, 0x1BF2, 2000, 3000, 20, amount: withAmount ? 42 : (int?) null,
                direction: withDirection ? Direction.South : (Direction?) null, hue: withHue ? 0x0026 : (int?) null,
                flags: withFlags ? 0x80 : (int?) null ) );

            Item item = Engine.Items.GetItem( ITEM );

            Assert.Equal( 0x1BF2, item.ID );
            Assert.Equal( withAmount ? 42 : 1, item.Count );
            Assert.Equal( 2000, item.X );
            Assert.Equal( 3000, item.Y );
            Assert.Equal( 20, item.Z );
            Assert.Equal( withDirection ? Direction.South : Direction.North, item.Direction );
            Assert.Equal( withHue ? 0x0026 : 0, item.Hue );
            Assert.Equal( withFlags ? 0x80 : 0, item.Flags );
        }

        /// <summary>An old-layout multi: graphic 0x4000 and up is a multi id, not a tile id.</summary>
        [Fact]
        public void WillMarkMultiOnOldWorldItemWithMultiGraphic()
        {
            Receive( WorldItem( ITEM, 0x4000 | 0x0064, 1500, 1600, 0 ) );

            Item item = Engine.Items.GetItem( ITEM );

            Assert.Equal( 0x0064, item.ID );
            Assert.Equal( 2, item.ArtDataID );
        }

        [Fact]
        public void WillRaiseNewWorldItemEventForBothWorldItemLayouts()
        {
            List<Item> raised = new List<Item>();

            void OnNewWorldItem( Item item )
            {
                raised.Add( item );
            }

            IncomingPacketHandlers.NewWorldItemEvent += OnNewWorldItem;

            try
            {
                Receive( WorldItem( ITEM, 0x0EED, 1500, 1600, 0 ) );
                Receive( SAWorldItem( ITEM + 1, 2, 0x0064, 1, 1510, 1610, 0, 0 ) );
            }
            finally
            {
                IncomingPacketHandlers.NewWorldItemEvent -= OnNewWorldItem;
            }

            Assert.Equal( new[] { ITEM, ITEM + 1 }, raised.Select( i => i.Serial ).ToArray() );
        }

        [Fact]
        public void WillParseEveryFieldOnSAWorldItem()
        {
            Receive( SAWorldItem( ITEM, 0, 0x0F3F, 250, 1234, 2345, -7, 0x0035, Direction.East, 0x1F, 0x08 ) );

            Item item = Engine.Items.GetItem( ITEM );

            Assert.NotNull( item );
            Assert.Equal( 0, item.ArtDataID );
            Assert.Equal( 0x0F3F, item.ID );
            Assert.Equal( 250, item.Count );
            Assert.Equal( 1234, item.X );
            Assert.Equal( 2345, item.Y );
            Assert.Equal( -7, item.Z );
            Assert.Equal( 0x0035, item.Hue );
            Assert.Equal( Direction.East, item.Direction );
            Assert.Equal( 0x1F, item.Light );
            Assert.Equal( 0x08, item.Flags );
            Assert.Equal( 0, item.Owner );
        }

        [Fact]
        public void WillEquipItemOnPlayerAndSetLayer()
        {
            Receive( EquipItem( ITEM, 0x13B9, Layer.OneHanded, PLAYER, 0x0455 ) );

            Item item = Engine.Items.GetItem( ITEM );

            Assert.NotNull( item );
            Assert.Equal( 0x13B9, item.ID );
            Assert.Equal( 0x0455, item.Hue );
            Assert.Equal( Layer.OneHanded, item.Layer );
            Assert.Equal( PLAYER, item.Owner );
            Assert.Equal( ITEM, Player.GetLayer( Layer.OneHanded ) );
            Assert.NotNull( Player.Equipment.GetItem( ITEM ) );
        }

        /// <summary>
        ///     Equipping onto an occupied layer replaces what was there in the equipment list; the old item
        ///     must not stay listed as worn.
        /// </summary>
        [Fact]
        public void WillReplacePreviousItemOnSameLayerOnEquip()
        {
            Receive( EquipItem( ITEM, 0x13B9, Layer.OneHanded, PLAYER ) );
            Receive( EquipItem( ITEM + 1, 0x0F5E, Layer.OneHanded, PLAYER ) );

            Assert.Equal( ITEM + 1, Player.GetLayer( Layer.OneHanded ) );
            Assert.Null( Player.Equipment.GetItem( ITEM ) );
            Assert.NotNull( Player.Equipment.GetItem( ITEM + 1 ) );
        }

        [Fact]
        public void WillApplyCachedPropertiesOnEquip()
        {
            Property[] cached = { new Property { Cliloc = 1060000, Text = "Item Name" } };
            IncomingPacketHandlers.PropertyCache[ITEM] = cached;

            Receive( EquipItem( ITEM, 0x13B9, Layer.OneHanded, PLAYER ) );

            Assert.Same( cached, Engine.Items.GetItem( ITEM ).Properties );
        }

        /// <summary>
        ///     A 0x1D for a worn item must clear the wearer's layer and equipment entry as well as the item
        ///     store, or the mobile still reports it as worn.
        /// </summary>
        [Fact]
        public void WillClearLayerAndEquipmentOnDeletingWornItem()
        {
            AddMobile( MOBILE );
            Receive( EquipItem( ITEM, 0x13B9, Layer.OneHanded, MOBILE ) );

            Mobile mobile = Engine.Mobiles.GetMobile( MOBILE );

            Assert.Equal( ITEM, mobile.GetLayer( Layer.OneHanded ) );

            Receive( DeleteObject( ITEM ) );

            Assert.Equal( 0, mobile.GetLayer( Layer.OneHanded ) );
            Assert.Null( mobile.Equipment.GetItem( ITEM ) );
            Assert.Null( Engine.Items.GetItem( ITEM ) );
        }

        [Fact]
        public void WillRemoveMobileAndItsEquipmentOnDeletingMobile()
        {
            Receive( MobileIncoming( MOBILE, 0x190, 1001, 1001, 0, 0, 0, MobileStatus.None, Notoriety.Innocent, false,
                ( ITEM, 0x1415, Layer.InnerTorso, 0 ), ( ITEM + 1, 0x0E75, Layer.Backpack, 0 ) ) );

            Assert.NotNull( Engine.Items.GetItem( ITEM ) );

            Receive( DeleteObject( MOBILE ) );

            Assert.Null( Engine.Mobiles.GetMobile( MOBILE ) );
            Assert.Null( Engine.Items.GetItem( ITEM ) );
            Assert.Null( Engine.Items.GetItem( ITEM + 1 ) );
        }

        [Fact]
        public void WillRemoveNestedItemOnDelete()
        {
            Receive( AddToContainer( ITEM, 0x0E79, 1, BACKPACK ) ); // pouch
            Receive( AddToContainer( ITEM + 1, 0x0F7A, 10, ITEM ) ); // pearls in the pouch

            Assert.NotNull( Engine.Items.GetItem( ITEM + 1 ) );

            Receive( DeleteObject( ITEM + 1 ) );

            Assert.Null( Engine.Items.GetItem( ITEM + 1 ) );
            Assert.NotNull( Engine.Items.GetItem( ITEM ) );
            Assert.Empty( Engine.Items.GetItem( ITEM ).Container.GetItems() );
        }

        [Fact]
        public void WillIgnoreDeleteOfUnknownSerial()
        {
            int before = Engine.Items.GetItemCount();

            Receive( DeleteObject( 0x40009999 ) );
            Receive( DeleteObject( 0x00009999 ) );

            Assert.Equal( before, Engine.Items.GetItemCount() );
            Assert.NotNull( Engine.Player.Backpack );
        }

        /// <summary>
        ///     0xD6 names the entity after its first property and caches the list, so an item that arrives
        ///     later (equipped, or put in a container) starts with its properties.
        /// </summary>
        [Fact]
        public void WillNameItemCacheAndRaiseEventOnProperties()
        {
            Receive( AddToContainer( ITEM, 0x13B9, 1, BACKPACK ) );

            List<Item> raised = new List<Item>();

            void OnUpdated( Item item )
            {
                raised.Add( item );
            }

            IncomingPacketHandlers.ItemPropertiesUpdatedEvent += OnUpdated;

            try
            {
                Receive( Properties( ITEM, ( 1060000, "" ), ( 500001, "Bob\t5" ), ( 1060002, "" ) ) );
            }
            finally
            {
                IncomingPacketHandlers.ItemPropertiesUpdatedEvent -= OnUpdated;
            }

            Item item = Engine.Items.GetItem( ITEM );

            Assert.Same( item, Assert.Single( raised ) );
            Assert.Equal( "Item Name", item.Name );
            Assert.Equal( 3, item.Properties.Length );
            Assert.Equal( new[] { "Bob", "5" }, item.Properties[1].Arguments );
            Assert.Equal( "You see Bob with 5", item.Properties[1].Text );
            Assert.Same( item.Properties, IncomingPacketHandlers.PropertyCache[ITEM] );
        }

        [Fact]
        public void WillSetPlayerNameFromFirstPropertyOnProperties()
        {
            Receive( Properties( PLAYER, ( 500000, "" ) ) );

            Assert.Equal( "plain message", Player.Name );
            Assert.Single( Player.Properties );
        }

        [Fact]
        public void WillListAttributesAndCrafterOnEquipmentInfo()
        {
            Receive( AddToContainer( ITEM, 0x13B9, 1, BACKPACK ) );

            Receive( Extended( 0x10, w =>
            {
                w.Write( ITEM );
                w.Write( 1060000 ); // item name cliloc
                w.Write( -3 ); // crafted by
                w.Write( (short) 4 );
                w.WriteAsciiFixed( "Mike", 4 );
                w.Write( 1060001 );
                w.Write( (short) 12 ); // charges
                w.Write( 1060002 );
                w.Write( (short) 0 );
                w.Write( -1 );
            } ) );

            Item item = Engine.Items.GetItem( ITEM );

            Assert.Equal( new[] { 1060000, -3, 1060001, 1060002 }, item.Properties.Select( p => p.Cliloc ).ToArray() );
            Assert.Equal( "Mike", item.Properties[1].Text );
            Assert.Equal( new[] { "12" }, item.Properties[2].Arguments );

            JournalEntry entry = Engine.Journal.GetEntireBuffer().Last();

            Assert.Equal( "Item Name Mike charges slaying", entry.Text );
            Assert.Equal( JournalSpeech.Label, entry.SpeechType );
        }

        /// <summary>
        ///     -4 (0xFFFFFFFC) marks an unidentified item and has no data after it (RunUO / ServUO
        ///     EquipmentInfo writes the bare marker). The handler reads an attribute number and charges after
        ///     it, which here consumes the -1 terminator; every read after that is past the end and returns 0,
        ///     so the loop never sees -1 again and never ends - hanging the packet thread while the journal
        ///     text grows without bound. Pre-AOS shards send this for a single click on an unidentified
        ///     magic weapon.
        /// </summary>
        [Fact( Skip = "BUG: 0xBF 0x10 with the unidentified marker (-4) loops forever (IncomingPacketHandlers.OnDisplayEquipmentInfo)" )]
        public async Task WillFinishEquipmentInfoForUnidentifiedItem()
        {
            Receive( AddToContainer( ITEM, 0x13B9, 1, BACKPACK ) );

            byte[] packet = Extended( 0x10, w =>
            {
                w.Write( ITEM );
                w.Write( 1060000 );
                w.Write( -4 ); // unidentified, no data follows
                w.Write( -1 );
            } );

            Task handler = Task.Run( () => Receive( packet ), TestContext.Current.CancellationToken );

            Task finished = await Task.WhenAny( handler, Task.Delay( TimeSpan.FromSeconds( 2 ), TestContext.Current.CancellationToken ) );

            Assert.Same( handler, finished ); // the handler must return
            Assert.Equal( 1060000, Engine.Items.GetItem( ITEM ).Properties[0].Cliloc );
        }

        private static byte[] Properties( int serial, params (int Cliloc, string Arguments)[] properties )
        {
            return Dynamic( 0xD6, w =>
            {
                w.Write( (short) 1 );
                w.Write( serial );
                w.Write( (short) 0 );
                w.Write( 0x12345678 ); // hash

                foreach ( (int cliloc, string arguments) in properties )
                {
                    w.Write( cliloc );
                    w.Write( (short) ( arguments.Length * 2 ) );

                    byte[] bytes = System.Text.Encoding.Unicode.GetBytes( arguments );
                    w.Write( bytes, 0, bytes.Length );
                }

                w.Write( 0 );
            } );
        }

        /// <summary>0x1A in the old variable layout, setting a flag bit for each optional field given.</summary>
        private static byte[] WorldItem( int serial, int graphic, int x, int y, int z, int? amount = null,
            int? increment = null, Direction? direction = null, int? hue = null, int? flags = null )
        {
            return Dynamic( 0x1A, w =>
            {
                w.Write( amount.HasValue ? (int) ( (uint) serial | 0x80000000 ) : serial );
                w.Write( (ushort) ( increment.HasValue ? graphic | 0x8000 : graphic ) );

                if ( amount.HasValue )
                {
                    w.Write( (short) amount.Value );
                }

                if ( increment.HasValue )
                {
                    w.Write( (byte) increment.Value );
                }

                w.Write( (ushort) ( direction.HasValue ? x | 0x8000 : x ) );
                w.Write( (ushort) ( y | ( hue.HasValue ? 0x8000 : 0 ) | ( flags.HasValue ? 0x4000 : 0 ) ) );

                if ( direction.HasValue )
                {
                    w.Write( (byte) direction.Value );
                }

                w.Write( (sbyte) z );

                if ( hue.HasValue )
                {
                    w.Write( (short) hue.Value );
                }

                if ( flags.HasValue )
                {
                    w.Write( (byte) flags.Value );
                }
            } );
        }

        private static byte[] SAWorldItem( int serial, byte type, int graphic, int amount, int x, int y, int z, int hue,
            Direction direction = Direction.North, int light = 0, int flags = 0 )
        {
            return Fixed( 0xF3, w =>
            {
                w.Write( (short) 1 );
                w.Write( type );
                w.Write( serial );
                w.Write( (short) graphic );
                w.Write( (byte) direction );
                w.Write( (short) amount );
                w.Write( (short) amount );
                w.Write( (short) x );
                w.Write( (short) y );
                w.Write( (sbyte) z );
                w.Write( (byte) light );
                w.Write( (short) hue );
                w.Write( (byte) flags );
                w.Write( (short) 0 ); // access
            } );
        }
    }
}
