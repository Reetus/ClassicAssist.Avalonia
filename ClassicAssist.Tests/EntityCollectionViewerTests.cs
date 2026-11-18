using System.Collections.Generic;
using System.Linq;
using ClassicAssist.Misc;
using ClassicAssist.UI.Models;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests
{
    public class EntityCollectionViewerTests
    {
        private static ItemCollection MakeCollection( params (int serial, int id, int count)[] items )
        {
            ItemCollection collection = new ItemCollection( 0x40000000 );

            foreach ( (int serial, int id, int count) in items )
            {
                collection.Add( new Item( serial, 0x40000000 ) { ID = id, Count = count } );
            }

            return collection;
        }

        [Fact]
        public void WillNameItemsWithoutOne()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            TileData.Initialize( TestData.UOPath );

            // The table covers 0x0000 to 0xFFFF, so anything past that falls through to the empty entry
            // GetStaticTile hands back for an unknown ID.
            const int unknownId = 0x10000;

            // 0x1BF2 is an iron ingot, which tile data names; the other has nothing to fall back to but
            // the serial.
            List<EntityCollectionData> data =
                MakeCollection( ( 0x40000001, 0x1BF2, 1 ), ( 0x40000002, unknownId, 1 ) )
                    .ToEntityCollectionData( new SerialComparer(), new Dictionary<int, string>() );

            Assert.Equal( 2, data.Count );
            Assert.Equal( TileData.GetStaticTile( 0x1BF2 ).Name, data[0].Name );
            Assert.Equal( "0x40000002", data[1].Name );
        }

        [Fact]
        public void WillPreferNameOverride()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            TileData.Initialize( TestData.UOPath );

            Dictionary<int, string> overrides = new Dictionary<int, string> { { 0x40000001, "a shiny thing" } };

            List<EntityCollectionData> data = MakeCollection( ( 0x40000001, 0x1BF2, 1 ) )
                .ToEntityCollectionData( new IDThenHueThenSerialComparer(), overrides );

            Assert.Equal( "a shiny thing", data[0].Name );
        }

        [Fact]
        public void WillDrawCoinPileForStack()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            Art.Initialize( TestData.UOPath );

            // Gold draws as one coin, a small pile at more than one, and a large pile at more than five,
            // which is three different graphics off the same item ID.
            Pixmap single = new EntityCollectionData
            {
                Entity = new Item( 1 ) { ID = 0x0EED, Count = 1 }
            }.Pixmap;

            Pixmap small = new EntityCollectionData
            {
                Entity = new Item( 2 ) { ID = 0x0EED, Count = 3 }
            }.Pixmap;

            Pixmap large = new EntityCollectionData
            {
                Entity = new Item( 3 ) { ID = 0x0EED, Count = 500 }
            }.Pixmap;

            Assert.False( single.IsEmpty );
            Assert.False( small.IsEmpty );
            Assert.False( large.IsEmpty );

            Assert.NotEqual( single.Pixels, small.Pixels );
            Assert.NotEqual( small.Pixels, large.Pixels );
        }

        [Fact]
        public void WillSortByTheSelectedStyle()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            TileData.Initialize( TestData.UOPath );

            EntityCollectionViewerViewModel viewModel =
                new EntityCollectionViewerViewModel( MakeCollection( ( 0x40000003, 0x1BF2, 5 ),
                    ( 0x40000001, 0x13B9, 1 ), ( 0x40000002, 0x0F0E, 9 ) ) );

            viewModel.Options.SortStyle = EntityCollectionSortStyle.Serial;

            Assert.Equal( new[] { 0x40000001, 0x40000002, 0x40000003 },
                viewModel.Entities.Select( e => e.Entity.Serial ).ToArray() );

            viewModel.Options.SortStyle = EntityCollectionSortStyle.Quantity;

            Assert.Equal( new[] { 1, 5, 9 },
                viewModel.Entities.Cast<EntityCollectionData>().Select( e => ( (Item) e.Entity ).Count )
                    .ToArray() );

            viewModel.Cleanup();
        }

        [Fact]
        public void WillStopListeningAfterCleanup()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            TileData.Initialize( TestData.UOPath );

            ItemCollection collection = MakeCollection( ( 0x40000001, 0x1BF2, 1 ) );

            EntityCollectionViewerViewModel viewModel = new EntityCollectionViewerViewModel( collection );

            collection.Add( new Item( 0x40000002, 0x40000000 ) { ID = 0x13B9, Count = 1 } );

            Assert.Equal( 2, viewModel.Entities.Count ); // "the viewer should track the collection"

            viewModel.Cleanup();

            // The collection outlives the window, so a closed viewer must not keep rebuilding itself.
            collection.Add( new Item( 0x40000003, 0x40000000 ) { ID = 0x0F0E, Count = 1 } );

            Assert.Equal( 2, viewModel.Entities.Count );
        }
    }
}
