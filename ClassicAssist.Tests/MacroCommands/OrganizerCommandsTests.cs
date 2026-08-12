using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Organizer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests.MacroCommands
{
    [TestClass]
    public class OrganizerCommandsTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            Engine.Journal.Clear();
        }

        [TestMethod]
        public void OrganizingWillBeFalseWithNoRunningEntries()
        {
            OrganizerEntry entry = new OrganizerEntry { Name = "OrganizingFalseTest", IsRunning = () => false };

            OrganizerManager manager = OrganizerManager.GetInstance();
            manager.Items.Add( entry );

            Assert.IsFalse( OrganizerCommands.Organizing() );

            manager.Items.Remove( entry );
        }

        [TestMethod]
        public void OrganizingWillBeTrueWithARunningEntry()
        {
            OrganizerEntry entry = new OrganizerEntry { Name = "OrganizingTrueTest", IsRunning = () => true };

            OrganizerManager manager = OrganizerManager.GetInstance();
            manager.Items.Add( entry );

            Assert.IsTrue( OrganizerCommands.Organizing() );

            manager.Items.Remove( entry );
        }

        [TestMethod]
        public void OrganizerWillMessageForUnknownEntry()
        {
            OrganizerCommands.Organizer( "ThisOrganizerDoesNotExist" );

            Assert.IsTrue( JournalCommands.InJournal( "not found", "system" ) );
        }

        [TestMethod]
        public void StopOrganizerWontThrow()
        {
            OrganizerCommands.StopOrganizer();
        }
    }
}
