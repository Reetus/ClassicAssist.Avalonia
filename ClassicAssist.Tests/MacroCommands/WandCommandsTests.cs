using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests.MacroCommands
{
    [TestClass]
    public class WandCommandsTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            Engine.Features = FeatureFlags.None;
            Engine.Journal.Clear();
        }

        [TestMethod]
        public void EquipWandWillMessageForInvalidWandName()
        {
            bool result = WandCommands.EquipWand( "ThisWandDoesNotExist" );

            Assert.IsFalse( result );
            Assert.IsTrue( JournalCommands.InJournal( "Invalid skill name", "system" ) );
        }

        [TestMethod]
        public void FindWandWillMessageForInvalidWandName()
        {
            bool result = WandCommands.FindWand( "ThisWandDoesNotExist" );

            Assert.IsFalse( result );
            Assert.IsTrue( JournalCommands.InJournal( "Invalid skill name", "system" ) );
        }

        [TestMethod]
        public void FindWandWillMessageWhenNoneFound()
        {
            // AOS set so FindWands skips its LookRequest/property-query loop (no server to answer
            // it) and goes straight to matching against already-known Item.Properties.
            Engine.Features = FeatureFlags.AOS;

            bool result = WandCommands.FindWand( "Clumsy" );

            Assert.IsFalse( result );
            Assert.IsTrue( JournalCommands.InJournal( "Cannot find item", "system" ) );
        }

        [TestMethod]
        public void FindWandWillSetFoundAliasOnMatch()
        {
            Engine.Features = FeatureFlags.AOS;

            Item wand = new Item( 0x40004001 )
            {
                ID = 0xDF2, Properties = [new Property { Cliloc = 3002011 }]
            };

            Engine.Items.Add( wand );

            bool result = WandCommands.FindWand( "Clumsy" );

            Assert.IsTrue( result );
            Assert.AreEqual( wand.Serial, AliasCommands.GetAlias( "found" ) );

            Engine.Items.Remove( wand.Serial );
        }
    }
}
