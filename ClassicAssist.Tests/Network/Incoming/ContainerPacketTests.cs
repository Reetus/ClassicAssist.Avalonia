using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ClassicAssist.Data.Vendors;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests.Network.Incoming
{
    /// <summary>
    ///     Containers and vendors: 0x25 (both the pre-6.0.1.7 layout without the grid byte and the current
    ///     one), 0x3C, 0x24 container display (corpse and vendor buy), 0x74 shop list and 0x9E sell list.
    ///     0x25 also has to take an item off whichever mobile layer it was worn on, and its "added to the
    ///     backpack" event drives the rehue list and the backup agent; the vendor packets pair prices with
    ///     container items by position, so the order they are matched in is the whole of the buy agent.
    /// </summary>
    public class ContainerPacketTests : IncomingPacketTestBase
    {
        private const int ITEM = 0x40000501;
        private const int POUCH = 0x40000502;
        private const int CHEST = 0x40000503;
        private const int VENDOR = 0x00000504;
        private const int BUY_CONTAINER = 0x40000505;

        [Fact]
        public void WillAddItemToBackpackOnAddToContainer()
        {
            Receive( AddToContainer( ITEM, 0x0F7A, 25, BACKPACK, 0x0455, 50, 60 ) );

            Item item = Backpack.Container.GetItem( ITEM );

            Assert.NotNull( item );
            Assert.Equal( 0x0F7A, item.ID );
            Assert.Equal( 25, item.Count );
            Assert.Equal( 0x0455, item.Hue );
            Assert.Equal( 50, item.X );
            Assert.Equal( 60, item.Y );
            Assert.Equal( BACKPACK, item.Owner );
        }

        /// <summary>
        ///     Clients before 6.0.1.7 send 0x25 without the grid byte. Reading one anyway would take the
        ///     first byte of the container serial.
        /// </summary>
        [Fact]
        public void WillReadContainerSerialWithoutGridOnOldAddToContainer()
        {
            Engine.ClientVersion = new Version( 5, 0, 9, 1 );

            Receive( Fixed( 0x25,
                w => WriteContainerEntry( w, ITEM, 0x0F7A, 3, 10, 20, BACKPACK, 0x0021, withGrid: false ) ) );

            Item item = Backpack.Container.GetItem( ITEM );

            Assert.NotNull( item );
            Assert.Equal( 0x0021, item.Hue );
            Assert.Equal( 0, item.Grid );
        }

        [Fact]
        public void WillCreateContainerForUnseenParentOnAddToContainer()
        {
            Receive( AddToContainer( POUCH, 0x0E79, 1, BACKPACK ) );
            Receive( AddToContainer( ITEM, 0x0F7A, 5, POUCH ) );

            Item pouch = Engine.Items.GetItem( POUCH );

            Assert.NotNull( pouch.Container );
            Assert.Same( Engine.Items.GetItem( ITEM ), pouch.Container.GetItem( ITEM ) );
            Assert.True( Engine.Items.GetItem( ITEM ).IsDescendantOf( BACKPACK ) );
        }

        /// <summary>
        ///     Zero is how some servers send a single item; the store keeps counts of at least one so that
        ///     the counters and agents never see an empty stack.
        /// </summary>
        [Fact]
        public void WillCountZeroAmountAsOneOnAddToContainer()
        {
            Receive( AddToContainer( ITEM, 0x0F7A, 0, BACKPACK ) );

            Assert.Equal( 1, Engine.Items.GetItem( ITEM ).Count );
        }

        /// <summary>
        ///     An item taken off and put in the backpack arrives as a 0x25. The player must not keep reporting
        ///     it on the layer it was worn on.
        /// </summary>
        [Fact]
        public void WillClearPlayerLayerWhenWornItemGoesIntoContainer()
        {
            Receive( EquipItem( ITEM, 0x13B9, Layer.OneHanded, PLAYER ) );

            Assert.Equal( ITEM, Player.GetLayer( Layer.OneHanded ) );

            Receive( AddToContainer( ITEM, 0x13B9, 1, BACKPACK ) );

            Assert.Equal( 0, Player.GetLayer( Layer.OneHanded ) );
            Assert.NotNull( Backpack.Container.GetItem( ITEM ) );
            Assert.Equal( BACKPACK, Engine.Items.GetItem( ITEM ).Owner );
        }

        [Fact]
        public void WillClearOtherMobileLayerWhenWornItemGoesIntoContainer()
        {
            Receive( MobileIncoming( VENDOR, 0x190, 1001, 1001, 0, 0, 0, MobileStatus.None, Notoriety.Innocent, false,
                ( ITEM, 0x13B9, Layer.OneHanded, 0 ) ) );

            Receive( AddToContainer( ITEM, 0x13B9, 1, BACKPACK ) );

            Assert.Equal( 0, Engine.Mobiles.GetMobile( VENDOR ).GetLayer( Layer.OneHanded ) );
            Assert.NotNull( Backpack.Container.GetItem( ITEM ) );
        }

        [Fact]
        public void WillRaiseBackupItemAddedOnlyForBackpackDescendants()
        {
            Engine.Items.Add( new Item( CHEST ) { ID = 0x0E40, X = 1001, Y = 1001 } );

            List<int> raised = new List<int>();

            void OnAdded( Item item )
            {
                raised.Add( item.Serial );
            }

            IncomingPacketHandlers.BackupItemAddedUpdateEvent += OnAdded;

            try
            {
                Receive( AddToContainer( POUCH, 0x0E79, 1, BACKPACK ) );
                Receive( AddToContainer( ITEM, 0x0F7A, 5, POUCH ) );
                Receive( AddToContainer( ITEM + 10, 0x0F7A, 5, CHEST ) );
            }
            finally
            {
                IncomingPacketHandlers.BackupItemAddedUpdateEvent -= OnAdded;
            }

            Assert.Equal( new[] { POUCH, ITEM }, raised.ToArray() );
        }

        [Fact]
        public void WillIgnoreEmptyContainerContents()
        {
            Receive( new byte[] { 0x3C, 0x00, 0x05, 0x00, 0x00 } );

            Assert.Empty( Backpack.Container.GetItems() );
        }

        [Fact]
        public void WillRaiseContainerContentsEventPerContainer()
        {
            using ManualResetEventSlim done = new ManualResetEventSlim();
            List<(int Serial, int Count)> raised = new List<(int, int)>();

            void OnContents( int serial, ItemCollection container )
            {
                lock ( raised )
                {
                    raised.Add( ( serial, container.GetItemCount() ) );

                    if ( raised.Count == 2 )
                    {
                        done.Set();
                    }
                }
            }

            IncomingPacketHandlers.ContainerContentsEvent += OnContents;

            try
            {
                Receive( ContainerContents( ( POUCH, 0x0E79, 1, BACKPACK ), ( ITEM, 0x0F7A, 5, BACKPACK ),
                    ( ITEM + 1, 0x0F7B, 6, POUCH ) ) );

                Assert.True( done.Wait( TimeSpan.FromSeconds( 5 ), TestContext.Current.CancellationToken ) );
            }
            finally
            {
                IncomingPacketHandlers.ContainerContentsEvent -= OnContents;
            }

            lock ( raised )
            {
                Assert.Equal( new[] { ( BACKPACK, 2 ), ( POUCH, 1 ) }, raised.ToArray() );
            }
        }

        /// <summary>
        ///     A corpse opens with gump 0x09. Clients before the container-type word send a 7-byte 0x24;
        ///     newer ones mark containers with a non-zero type. Either is a corpse for autoloot, and the event
        ///     is raised off the packet thread.
        /// </summary>
        [Theory]
        [InlineData( true )]
        [InlineData( false )]
        public void WillRaiseCorpseDisplayForCorpseGump( bool oldClient )
        {
            using ManualResetEventSlim done = new ManualResetEventSlim();
            List<(int Serial, bool Force)> raised = new List<(int, bool)>();

            void OnCorpse( int serial, bool force )
            {
                lock ( raised )
                {
                    raised.Add( ( serial, force ) );
                }

                done.Set();
            }

            IncomingPacketHandlers.CorpseContainerDisplayEvent += OnCorpse;

            try
            {
                // A backpack opening is not a corpse, whatever the client version.
                Receive( ContainerDisplay( CHEST, 0x3C, oldClient ? (int?) null : 0x7D ) );
                Receive( ContainerDisplay( ITEM, 0x09, oldClient ? (int?) null : 0x7D ) );

                Assert.True( done.Wait( TimeSpan.FromSeconds( 5 ), TestContext.Current.CancellationToken ) );
            }
            finally
            {
                IncomingPacketHandlers.CorpseContainerDisplayEvent -= OnCorpse;
            }

            lock ( raised )
            {
                Assert.Equal( ( ITEM, false ), Assert.Single( raised ) );
            }
        }

        /// <summary>
        ///     The buy flow: the vendor's buy container is filled by 0x3C, 0x74 lists one price per item, and
        ///     0x24 on the vendor with container type 0 opens the buy window. Prices pair with the items in
        ///     x-then-y order, as the client lays them out.
        /// </summary>
        [Fact]
        public void WillPairShopListWithContainerItemsInXYOrderOnVendorBuy()
        {
            Receive( MobileIncoming( VENDOR, 0x190, 1001, 1001, 0, 0, 0, MobileStatus.None, Notoriety.Invulnerable,
                false, ( BUY_CONTAINER, 0x0E75, Layer.ShopBuy, 0 ) ) );

            Receive( Dynamic( 0x3C, w =>
            {
                w.Write( (short) 3 );
                WriteContainerEntry( w, ITEM + 2, 0x0F7B, 20, 90, 10, BUY_CONTAINER, 0 ); // rightmost
                WriteContainerEntry( w, ITEM, 0x0F7A, 999, 10, 50, BUY_CONTAINER, 0 ); // leftmost, lower
                WriteContainerEntry( w, ITEM + 1, 0x0F84, 5, 10, 20, BUY_CONTAINER, 0 ); // leftmost, upper
            } ) );

            Receive( Dynamic( 0x74, w =>
            {
                w.Write( BUY_CONTAINER );
                w.Write( (byte) 3 );

                foreach ( (int price, string name) in new[] { ( 3, "garlic" ), ( 5, "pearl" ), ( 7, "moss" ) } )
                {
                    w.Write( price );
                    w.Write( (byte) ( name.Length + 1 ) );
                    w.WriteAsciiNull( name );
                }
            } ) );

            List<(int Serial, ShopListEntry[] Entries)> raised = new List<(int, ShopListEntry[])>();

            void OnBuy( int serial, ShopListEntry[] entries )
            {
                raised.Add( ( serial, entries ) );
            }

            IncomingPacketHandlers.VendorBuyDisplayEvent += OnBuy;

            try
            {
                Receive( ContainerDisplay( VENDOR, 0x30, 0 ) );
            }
            finally
            {
                IncomingPacketHandlers.VendorBuyDisplayEvent -= OnBuy;
            }

            (int serial, ShopListEntry[] shop) = Assert.Single( raised );

            Assert.Equal( VENDOR, serial );
            Assert.Equal( new[] { "garlic", "pearl", "moss" }, shop.Select( e => e.Name ).ToArray() );
            Assert.Equal( new[] { ITEM + 1, ITEM, ITEM + 2 }, shop.Select( e => e.Item.Serial ).ToArray() );
            Assert.Equal( new[] { 5, 999, 20 }, shop.Select( e => e.Amount ).ToArray() );
            Assert.All( shop, e => Assert.Equal( VENDOR, e.VendorSerial ) );
        }

        [Fact]
        public void WontRaiseVendorBuyForMobileWithoutShopList()
        {
            AddMobile( VENDOR );

            int raised = 0;

            void OnBuy( int serial, ShopListEntry[] entries )
            {
                raised++;
            }

            IncomingPacketHandlers.VendorBuyDisplayEvent += OnBuy;

            try
            {
                Receive( ContainerDisplay( VENDOR, 0x30, 0 ) );
            }
            finally
            {
                IncomingPacketHandlers.VendorBuyDisplayEvent -= OnBuy;
            }

            Assert.Equal( 0, raised );
        }

        [Fact]
        public void WillRaiseVendorSellOnlyWhenListHasEntries()
        {
            List<(int Serial, SellListEntry[] Entries)> raised = new List<(int, SellListEntry[])>();

            void OnSell( int serial, SellListEntry[] entries )
            {
                raised.Add( ( serial, entries ) );
            }

            IncomingPacketHandlers.VendorSellDisplayEvent += OnSell;

            try
            {
                Receive( SellList( VENDOR ) );
                Receive( SellList( VENDOR, ( ITEM, 0x0F7A, 0x0021, 50, 3, "black pearl" ),
                    ( ITEM + 1, 0x0F84, 0, 12, 2, "garlic" ) ) );
            }
            finally
            {
                IncomingPacketHandlers.VendorSellDisplayEvent -= OnSell;
            }

            (int serial, SellListEntry[] entries) = Assert.Single( raised );

            Assert.Equal( VENDOR, serial );
            Assert.Equal( 2, entries.Length );
            Assert.Equal( ITEM, entries[0].Serial );
            Assert.Equal( 0x0F7A, entries[0].ID );
            Assert.Equal( 0x0021, entries[0].Hue );
            Assert.Equal( 50, entries[0].Amount );
            Assert.Equal( 3, entries[0].Price );
            Assert.Equal( "black pearl", entries[0].Name );
            Assert.Equal( "garlic", entries[1].Name );
        }

        private static byte[] ContainerDisplay( int serial, int gump, int? containerType )
        {
            return Fixed( 0x24, w =>
            {
                w.Write( serial );
                w.Write( (short) gump );

                if ( containerType.HasValue )
                {
                    w.Write( (short) containerType.Value );
                }
            } );
        }

        private static byte[] SellList( int vendor, params (int Serial, int ID, int Hue, int Amount, int Price, string Name)[] entries )
        {
            return Dynamic( 0x9E, w =>
            {
                w.Write( vendor );
                w.Write( (short) entries.Length );

                foreach ( (int serial, int id, int hue, int amount, int price, string name) in entries )
                {
                    w.Write( serial );
                    w.Write( (short) id );
                    w.Write( (short) hue );
                    w.Write( (short) amount );
                    w.Write( (short) price );
                    w.Write( (short) name.Length );
                    w.WriteAsciiFixed( name, name.Length );
                }
            } );
        }
    }
}
