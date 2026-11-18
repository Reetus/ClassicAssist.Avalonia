using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Regions;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using System;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class RegionCommandsTests : IDisposable
    {
        public void Dispose()
        {
            Engine.Player = null;
            Engine.Journal.Clear();
        }

        [Fact]
        public void InRegionWillMessageForInvalidSerial()
        {
            // Passing the int 0 directly resolves through AliasCommands.ResolveSerial without
            // side effects (an unresolvable string alias would fire its own "unknown alias"
            // message first), landing cleanly on RegionCommands' own "invalid object id" branch.
            bool result = RegionCommands.InRegion( "Guarded", 0 );

            Assert.False( result );
            Assert.True( JournalCommands.InJournal( "Invalid or unknown object id", "system" ) );
        }

        [Fact]
        public void InRegionWillMessageWhenEntityNotFound()
        {
            bool result = RegionCommands.InRegion( "Guarded", 0x40001234 );

            Assert.False( result );
            Assert.True( JournalCommands.InJournal( "Cannot find item", "system" ) );
        }

        [Fact]
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

            Assert.True( RegionCommands.InRegion( "Guarded", item.Serial ) );
            Assert.False( RegionCommands.InRegion( "Wilderness", item.Serial ) );

            Regions.Remove( region );
            Engine.Items.Remove( item.Serial );
        }

        [Fact]
        public void InRegionWillBeFalseOutsideAnyRegion()
        {
            Engine.Player = new PlayerMobile( 0x01 ) { Map = Map.Felucca };

            Item item = new Item( 0x40002001 ) { X = -5000, Y = -5000 };
            Engine.Items.Add( item );

            Assert.False( RegionCommands.InRegion( "Guarded", item.Serial ) );

            Engine.Items.Remove( item.Serial );
        }
    }
}
