using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Regions;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests.MacroCommands
{
    [TestClass]
    public class RegionCommandsTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            Engine.Player = null;
            Engine.Journal.Clear();
        }

        [TestMethod]
        public void InRegionWillMessageForInvalidSerial()
        {
            // Passing the int 0 directly resolves through AliasCommands.ResolveSerial without
            // side effects (an unresolvable string alias would fire its own "unknown alias"
            // message first), landing cleanly on RegionCommands' own "invalid object id" branch.
            bool result = RegionCommands.InRegion( "Guarded", 0 );

            Assert.IsFalse( result );
            Assert.IsTrue( JournalCommands.InJournal( "Invalid or unknown object id", "system" ) );
        }

        [TestMethod]
        public void InRegionWillMessageWhenEntityNotFound()
        {
            bool result = RegionCommands.InRegion( "Guarded", 0x40001234 );

            Assert.IsFalse( result );
            Assert.IsTrue( JournalCommands.InJournal( "Cannot find item", "system" ) );
        }

        [TestMethod]
        public void InRegionWillMatchAttributeInsideBounds()
        {
            Engine.Player = new PlayerMobile( 0x01 ) { Map = Map.Felucca };

            Item item = new Item( 0x40002000 ) { X = 100, Y = 100 };
            Engine.Items.Add( item );

            // Priority above the real "Felucca" catch-all region (Data/Regions.json ships one at
            // priority -1 covering the whole map) so this test region wins the overlap instead of
            // depending on real region data's own attributes.
            Region region = new Region
            {
                Name = "RegionCommandsTests Guarded Zone",
                Attributes = RegionAttributes.Guarded,
                Map = (int) Map.Felucca,
                Priority = 100,
                X1 = 0,
                Y1 = 0,
                X2 = 200,
                Y2 = 200
            };

            Regions.Add( region );

            Assert.IsTrue( RegionCommands.InRegion( "Guarded", item.Serial ) );
            Assert.IsFalse( RegionCommands.InRegion( "Wilderness", item.Serial ) );

            Regions.Remove( region );
            Engine.Items.Remove( item.Serial );
        }

        [TestMethod]
        public void InRegionWillBeFalseOutsideAnyRegion()
        {
            Engine.Player = new PlayerMobile( 0x01 ) { Map = Map.Felucca };

            Item item = new Item( 0x40002001 ) { X = -5000, Y = -5000 };
            Engine.Items.Add( item );

            Assert.IsFalse( RegionCommands.InRegion( "Guarded", item.Serial ) );

            Engine.Items.Remove( item.Serial );
        }
    }
}
