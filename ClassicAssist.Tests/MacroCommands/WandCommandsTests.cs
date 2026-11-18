using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Objects;
using System;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class WandCommandsTests : IDisposable
    {
        public void Dispose()
        {
            Engine.Features = FeatureFlags.None;
            Engine.Journal.Clear();
        }

        [Fact]
        public void EquipWandWillMessageForInvalidWandName()
        {
            bool result = WandCommands.EquipWand( "ThisWandDoesNotExist" );

            Assert.False( result );
            Assert.True( JournalCommands.InJournal( "Invalid skill name", "system" ) );
        }

        [Fact]
        public void FindWandWillMessageForInvalidWandName()
        {
            bool result = WandCommands.FindWand( "ThisWandDoesNotExist" );

            Assert.False( result );
            Assert.True( JournalCommands.InJournal( "Invalid skill name", "system" ) );
        }

        [Fact]
        public void FindWandWillMessageWhenNoneFound()
        {
            // AOS set so FindWands skips its LookRequest/property-query loop (no server to answer
            // it) and goes straight to matching against already-known Item.Properties.
            Engine.Features = FeatureFlags.AOS;

            bool result = WandCommands.FindWand( "Clumsy" );

            Assert.False( result );
            Assert.True( JournalCommands.InJournal( "Cannot find item", "system" ) );
        }

        [Fact]
        public void FindWandWillSetFoundAliasOnMatch()
        {
            Engine.Features = FeatureFlags.AOS;

            Item wand = new Item( 0x40004001 )
            {
                ID = 0xDF2, Properties = [new Property { Cliloc = 3002011 }]
            };

            Engine.Items.Add( wand );

            bool result = WandCommands.FindWand( "Clumsy" );

            Assert.True( result );
            Assert.Equal( wand.Serial, AliasCommands.GetAlias( "found" ) );

            Engine.Items.Remove( wand.Serial );
        }
    }
}
