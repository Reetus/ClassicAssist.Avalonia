using ClassicAssist.UO.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Multis are only swept out of the collection on a facet change: Trammel and Felucca share
    ///     coordinates, so crossing between them doesn't move the player and the usual distance sweep
    ///     never fires. Without <see cref="ItemCollection.ClearMultis" /> the previous facet's houses
    ///     stay in the collection, matching different geometry at the same x/y.
    /// </summary>
    [TestClass]
    public class ItemCollectionMultiTests
    {
        private const int MULTI_ART_DATA_ID = 2;

        [TestMethod]
        public void ClearMultisRemovesOnlyMultis()
        {
            ItemCollection collection = new ItemCollection( 0 );

            Item house = new Item( 0x40000001 ) { ArtDataID = MULTI_ART_DATA_ID, X = 100, Y = 100 };
            Item boat = new Item( 0x40000002 ) { ArtDataID = MULTI_ART_DATA_ID, X = 200, Y = 200 };
            Item groundItem = new Item( 0x40000003 ) { X = 100, Y = 100 };

            collection.Add( house );
            collection.Add( boat );
            collection.Add( groundItem );

            collection.ClearMultis();

            Assert.IsNull( collection.GetItem( 0x40000001 ), "house should be removed" );
            Assert.IsNull( collection.GetItem( 0x40000002 ), "boat should be removed" );
            Assert.IsNotNull( collection.GetItem( 0x40000003 ), "non-multi should be left alone" );
        }

        [TestMethod]
        public void ClearMultisNotifiesOnlyWhenSomethingWasRemoved()
        {
            ItemCollection collection = new ItemCollection( 0 );

            collection.Add( new Item( 0x40000003 ) { X = 100, Y = 100 } );

            int notifications = 0;
            collection.CollectionChanged += ( count, added, entities ) => notifications++;

            // Nothing to remove - must not raise, or every facet change would churn the ECV.
            collection.ClearMultis();
            Assert.AreEqual( 0, notifications );

            collection.Add( new Item( 0x40000001 ) { ArtDataID = MULTI_ART_DATA_ID, X = 100, Y = 100 } );
            notifications = 0;

            // Only that it notified - Remove raises both per-item and per-batch, and pinning the exact
            // count here would freeze that implementation detail rather than the contract.
            collection.ClearMultis();
            Assert.IsTrue( notifications > 0 );
        }

        /// <summary>
        ///     The case the facet sweep exists for: same coordinates, so a distance sweep is a no-op.
        /// </summary>
        [TestMethod]
        public void DistanceSweepLeavesCoLocatedMultiButClearMultisRemovesIt()
        {
            ItemCollection collection = new ItemCollection( 0 );

            collection.Add( new Item( 0x40000001 ) { ArtDataID = MULTI_ART_DATA_ID, X = 1000, Y = 1000 } );

            collection.RemoveByDistance( 32, 1000, 1000 );

            Assert.IsNotNull( collection.GetItem( 0x40000001 ),
                "distance sweep can't help when the facet change doesn't move the player" );

            collection.ClearMultis();

            Assert.IsNull( collection.GetItem( 0x40000001 ) );
        }

        /// <summary>
        ///     Distance is measured to a multi's origin tile, not its footprint, so a keep or castle can be
        ///     far out of range while the player stands inside it. Sweeping it would drop the structure the
        ///     player is currently in, so multis are exempt.
        /// </summary>
        [TestMethod]
        public void DistanceSweepExemptsOutOfRangeMultis()
        {
            ItemCollection collection = new ItemCollection( 0 );

            collection.Add( new Item( 0x40000001 ) { ArtDataID = MULTI_ART_DATA_ID, X = 1000, Y = 1000 } );
            collection.Add( new Item( 0x40000002 ) { X = 1000, Y = 1000 } );

            collection.RemoveByDistance( 32, 1100, 1100 );

            Assert.IsNotNull( collection.GetItem( 0x40000001 ), "multi should survive the distance sweep" );
            Assert.IsNull( collection.GetItem( 0x40000002 ), "non-multi out of range should still be swept" );
        }
    }
}
