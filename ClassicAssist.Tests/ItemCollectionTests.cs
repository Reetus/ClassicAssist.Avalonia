using System.Linq;
using ClassicAssist.Shared;
using ClassicAssist.UO.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Behavioural coverage for <see cref="ItemCollection" />, ported from CACL's suite alongside the
    ///     O(1) lookup / gated-notification changes. The serial registry is static and drifts high by
    ///     design, so these tests assert observable outcomes - finds still find, removals untrack, and
    ///     notifications fire once - rather than internal counts.
    /// </summary>
    [TestClass]
    public class ItemCollectionTests
    {
        private static Item MakeItem( int serial, int id = 0, int owner = 0 )
        {
            return new Item( serial ) { ID = id, Owner = owner };
        }

        private static ItemCollection MakeContainer( int serial )
        {
            return new ItemCollection( serial );
        }

        // --- Add / Remove basics ---

        [TestMethod]
        public void AddSingleItem()
        {
            ItemCollection col = MakeContainer( 1 );
            Item item = MakeItem( 100, id: 5 );

            bool result = col.Add( item );

            Assert.IsTrue( result );
            Assert.AreEqual( 1, col.GetItemCount() );
            Assert.IsTrue( col.GetItem( 100, out Item found ) );
            Assert.AreSame( item, found );
        }

        [TestMethod]
        public void AddMultipleItems()
        {
            ItemCollection col = MakeContainer( 1 );
            Item[] items = [MakeItem( 100 ), MakeItem( 101 ), MakeItem( 102 )];

            bool result = col.Add( items );

            Assert.IsTrue( result );
            Assert.AreEqual( 3, col.GetItemCount() );
            Assert.IsTrue( col.GetItem( 100, out _ ) );
            Assert.IsTrue( col.GetItem( 101, out _ ) );
            Assert.IsTrue( col.GetItem( 102, out _ ) );
        }

        [TestMethod]
        public void AddDuplicateSerialOverwrites()
        {
            ItemCollection col = MakeContainer( 1 );
            Item item1 = MakeItem( 100, id: 5 );
            Item item2 = MakeItem( 100, id: 10 );

            col.Add( item1 );
            col.Add( item2 );

            Assert.AreEqual( 1, col.GetItemCount() );
            Assert.IsTrue( col.GetItem( 100, out Item found ) );
            Assert.AreEqual( 10, found.ID );
        }

        [TestMethod]
        public void RemoveBySerial()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100 ) );
            col.Add( MakeItem( 101 ) );

            bool removed = col.Remove( 100 );

            Assert.IsTrue( removed );
            Assert.AreEqual( 1, col.GetItemCount() );
            Assert.IsFalse( col.GetItem( 100, out _ ) );
            Assert.IsTrue( col.GetItem( 101, out _ ) );
        }

        [TestMethod]
        public void RemoveNonexistentReturnsFalse()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100 ) );

            bool removed = col.Remove( 999 );

            Assert.IsFalse( removed );
            Assert.AreEqual( 1, col.GetItemCount() );
        }

        [TestMethod]
        public void RemoveOneItemLeavesOthersFindable()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100 ) );
            col.Add( MakeItem( 101 ) );

            col.Remove( 100 );

            Assert.IsTrue( col.GetItem( 101, out _ ), "untracking one serial must not affect the others" );
        }

        [TestMethod]
        public void GetItemFindsReAddedInstance()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100, id: 5 ) );
            col.Remove( 100 );

            Item replacement = MakeItem( 100, id: 7 );
            col.Add( replacement );

            Assert.IsTrue( col.GetItem( 100, out Item found ) );
            Assert.AreSame( replacement, found );
        }

        // --- Nested container search ---

        [TestMethod]
        public void GetItemFindsInRoot()
        {
            ItemCollection col = MakeContainer( 1 );
            Item item = MakeItem( 100 );
            col.Add( item );

            Assert.IsTrue( col.GetItem( 100, out Item found ) );
            Assert.AreSame( item, found );
        }

        [TestMethod]
        public void GetItemFindsInNestedContainer()
        {
            ItemCollection col = MakeContainer( 1 );

            Item bag = MakeItem( 200 );
            bag.Container = MakeContainer( 200 );
            col.Add( bag );

            Item innerItem = MakeItem( 300, id: 42 );
            bag.Container.Add( innerItem );

            Assert.IsTrue( col.GetItem( 300, out Item found ) );
            Assert.AreSame( innerItem, found );
            Assert.AreEqual( 42, found.ID );
        }

        [TestMethod]
        public void GetItemFindsInDeeplyNestedContainer()
        {
            ItemCollection col = MakeContainer( 1 );

            Item bag = MakeItem( 200 );
            bag.Container = MakeContainer( 200 );
            col.Add( bag );

            Item pouch = MakeItem( 300 );
            pouch.Container = MakeContainer( 300 );
            bag.Container.Add( pouch );

            Item deepItem = MakeItem( 400, id: 99 );
            pouch.Container.Add( deepItem );

            Assert.IsTrue( col.GetItem( 400, out Item found ) );
            Assert.AreSame( deepItem, found );
        }

        [TestMethod]
        public void GetItemAfterNestedContainerRemovedReturnsFalse()
        {
            ItemCollection col = MakeContainer( 1 );

            Item bag = MakeItem( 200 );
            bag.Container = MakeContainer( 200 );
            col.Add( bag );
            bag.Container.Add( MakeItem( 300 ) );

            col.Remove( 200 );

            Assert.IsFalse( col.GetItem( 300, out _ ) );
        }

        [TestMethod]
        public void RemoveNestedBySerialRemovesFromOwningContainer()
        {
            ItemCollection col = MakeContainer( 1 );

            Item bag = MakeItem( 200 );
            bag.Container = MakeContainer( 200 );
            col.Add( bag );
            bag.Container.Add( MakeItem( 300 ) );

            col.Remove( 300 );

            Assert.IsFalse( col.GetItem( 300, out _ ) );
            Assert.AreEqual( 0, bag.Container.GetItemCount() );
        }

        [TestMethod]
        public void ClearRemovesEverythingFromThisCollection()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100 ) );
            col.Add( MakeItem( 101 ) );

            col.Clear();

            Assert.AreEqual( 0, col.GetItemCount() );
            Assert.IsFalse( col.GetItem( 100, out _ ) );
            Assert.IsFalse( col.GetItem( 101, out _ ) );
        }

        [TestMethod]
        public void RemoveByOwnerRemovesOnlyOwnedItems()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100, owner: 5000 ) );
            col.Add( MakeItem( 101, owner: 5000 ) );
            col.Add( MakeItem( 102 ) );

            col.RemoveByOwner( 5000 );

            Assert.IsFalse( col.GetItem( 100, out _ ) );
            Assert.IsFalse( col.GetItem( 101, out _ ) );
            Assert.IsTrue( col.GetItem( 102, out _ ) );
        }

        // --- SelectEntity / SelectEntities (recursive) ---

        [TestMethod]
        public void SelectEntityFindsAtRoot()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100, id: 5 ) );
            col.Add( MakeItem( 101, id: 10 ) );

            Item found = col.SelectEntity( i => i.ID == 10 );

            Assert.IsNotNull( found );
            Assert.AreEqual( 101, found.Serial );
        }

        [TestMethod]
        public void SelectEntityFindsInNestedContainer()
        {
            ItemCollection col = MakeContainer( 1 );

            Item bag = MakeItem( 200 );
            bag.Container = MakeContainer( 200 );
            col.Add( bag );

            bag.Container.Add( MakeItem( 300, id: 42 ) );

            Item found = col.SelectEntity( i => i.ID == 42 );

            Assert.IsNotNull( found );
            Assert.AreEqual( 300, found.Serial );
        }

        [TestMethod]
        public void SelectEntityReturnsNullWhenNotFound()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100, id: 5 ) );

            Item found = col.SelectEntity( i => i.ID == 999 );

            Assert.IsNull( found );
        }

        [TestMethod]
        public void SelectEntitiesFindsAcrossContainers()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100, id: 5 ) );
            col.Add( MakeItem( 101, id: 10 ) );

            Item bag = MakeItem( 200 );
            bag.Container = MakeContainer( 200 );
            col.Add( bag );

            bag.Container.Add( MakeItem( 300, id: 5 ) );
            bag.Container.Add( MakeItem( 301, id: 10 ) );

            Item[] results = col.SelectEntities( i => i.ID == 5 );

            Assert.IsNotNull( results );
            Assert.AreEqual( 2, results.Length );
            Assert.IsTrue( results.Any( i => i.Serial == 100 ) );
            Assert.IsTrue( results.Any( i => i.Serial == 300 ) );
        }

        [TestMethod]
        public void SelectEntitiesReturnsNullWhenNoMatch()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100, id: 5 ) );

            Item[] results = col.SelectEntities( i => i.ID == 999 );

            Assert.IsNull( results );
        }

        [TestMethod]
        public void SelectEntitiesHasNoDuplicates()
        {
            ItemCollection col = MakeContainer( 1 );

            col.Add( MakeItem( 100, id: 5 ) );

            Item bag = MakeItem( 200 );
            bag.Container = MakeContainer( 200 );
            col.Add( bag );

            bag.Container.Add( MakeItem( 300, id: 5 ) );

            Item[] results = col.SelectEntities( i => i.ID == 5 );

            Assert.IsNotNull( results );
            Assert.AreEqual( 2, results.Length );
            Assert.AreEqual( results.Select( i => i.Serial ).Distinct().Count(), results.Length );
        }

        // --- FindItem / FindItems ---

        [TestMethod]
        public void FindItemById()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100, id: 5 ) );
            col.Add( MakeItem( 101, id: 10 ) );

            Assert.IsTrue( col.FindItem( 5, out Item item ) );
            Assert.AreEqual( 100, item.Serial );

            Assert.IsFalse( col.FindItem( 999, out _ ) );
        }

        [TestMethod]
        public void FindItemsById()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100, id: 5 ) );
            col.Add( MakeItem( 101, id: 5 ) );
            col.Add( MakeItem( 102, id: 10 ) );

            Assert.IsTrue( col.FindItems( 5, out Item[] items ) );
            Assert.AreEqual( 2, items.Length );
        }

        // --- GetItems / GetTotalItemCount ---

        [TestMethod]
        public void GetItemsReturnsSnapshot()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100 ) );
            col.Add( MakeItem( 101 ) );

            Item[] items = col.GetItems();

            Assert.AreEqual( 2, items.Length );

            col.Add( MakeItem( 102 ) );

            Assert.AreEqual( 2, items.Length );
        }

        [TestMethod]
        public void GetTotalItemCountIncludesNested()
        {
            ItemCollection col = MakeContainer( 1 );
            col.Add( MakeItem( 100 ) );

            Item bag = MakeItem( 200 );
            bag.Container = MakeContainer( 200 );
            bag.Container.Add( MakeItem( 300 ) );
            bag.Container.Add( MakeItem( 301 ) );
            col.Add( bag );

            Assert.AreEqual( 4, col.GetTotalItemCount() );
        }

        // --- GetAllItems (static, recursive flatten) ---

        [TestMethod]
        public void GetAllItemsFlattensHierarchy()
        {
            Item bag = MakeItem( 200 );
            bag.Container = MakeContainer( 200 );

            Item innerItem = MakeItem( 300, id: 5 );
            bag.Container.Add( innerItem );

            Item pouch = MakeItem( 400 );
            pouch.Container = MakeContainer( 400 );
            pouch.Container.Add( MakeItem( 500, id: 10 ) );
            bag.Container.Add( pouch );

            Item[] all = ItemCollection.GetAllItems( [bag] );

            Assert.IsNotNull( all );
            Assert.AreEqual( 4, all.Length );
            Assert.IsTrue( all.Any( i => i.Serial == 200 ) );
            Assert.IsTrue( all.Any( i => i.Serial == 300 ) );
            Assert.IsTrue( all.Any( i => i.Serial == 400 ) );
            Assert.IsTrue( all.Any( i => i.Serial == 500 ) );
        }

        [TestMethod]
        public void GetAllItemsReturnsEmptyForEmptyInput()
        {
            Item[] result = ItemCollection.GetAllItems( [] );

            Assert.IsNotNull( result );
            Assert.AreEqual( 0, result.Length );
        }

        // --- CollectionChanged gating / de-duplication ---

        [TestMethod]
        public void AddNotifiesLocalSubscribersOnce()
        {
            ItemCollection collection = new( 0x7F000001 );
            int notifications = 0;

            collection.CollectionChanged += ( count, added, entities ) => notifications++;

            collection.Add( MakeItem( 0x7F000002 ) );

            Assert.AreEqual( 1, notifications, "a single add must not raise the accidental duplicate" );
        }

        [TestMethod]
        public void RemoveNotifiesLocalSubscribersOnce()
        {
            ItemCollection collection = new( 0x7F000003 );
            Item item = MakeItem( 0x7F000004 );
            collection.Add( item );

            int notifications = 0;
            collection.CollectionChanged += ( count, added, entities ) => notifications++;

            collection.Remove( item );

            Assert.AreEqual( 1, notifications, "a single remove must not raise the accidental duplicate" );
        }

        [TestMethod]
        public void RemoveByDistanceNotifiesPerRemovedItem()
        {
            ItemCollection collection = new( 0x7F000005 );
            collection.Add( new Item( 0x7F000006 ) { X = 1000, Y = 1000 } );
            collection.Add( new Item( 0x7F000007 ) { X = 1000, Y = 1000 } );

            int notifications = 0;
            collection.CollectionChanged += ( count, added, entities ) => notifications++;

            collection.RemoveByDistance( 32, 1100, 1100 );

            Assert.AreEqual( 2, notifications );
            Assert.AreEqual( 0, collection.GetItemCount() );
        }

        [TestMethod]
        public void AddToNestedContainerNotifiesSubscribedParent()
        {
            ItemCollection root = Engine.Items;

            Item outer = MakeItem( 0x7F000010 );
            outer.Container = MakeContainer( outer.Serial );
            root.Add( outer );

            Item inner = MakeItem( 0x7F000011, owner: outer.Serial );
            outer.Container.Add( inner );
            inner.Container = MakeContainer( inner.Serial );

            int notifications = 0;
            outer.Container.CollectionChanged += ( count, added, entities ) => notifications++;

            try
            {
                inner.Container.Add( MakeItem( 0x7F000012 ) );

                Assert.AreEqual( 1, notifications,
                    "adding to a nested container should notify the subscribed parent container once" );
            }
            finally
            {
                root.Remove( outer.Serial );
            }
        }
    }
}
