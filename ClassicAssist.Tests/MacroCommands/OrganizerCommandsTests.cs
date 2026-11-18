using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Organizer;
using System;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class OrganizerCommandsTests : IDisposable
    {
        public void Dispose()
        {
            Engine.Journal.Clear();
        }

        [Fact]
        public void OrganizingWillBeFalseWithNoRunningEntries()
        {
            OrganizerEntry entry = new OrganizerEntry { Name = "OrganizingFalseTest", IsRunning = () => false };

            OrganizerManager manager = OrganizerManager.GetInstance();
            manager.Items.Add( entry );

            Assert.False( OrganizerCommands.Organizing() );

            manager.Items.Remove( entry );
        }

        [Fact]
        public void OrganizingWillBeTrueWithARunningEntry()
        {
            OrganizerEntry entry = new OrganizerEntry { Name = "OrganizingTrueTest", IsRunning = () => true };

            OrganizerManager manager = OrganizerManager.GetInstance();
            manager.Items.Add( entry );

            Assert.True( OrganizerCommands.Organizing() );

            manager.Items.Remove( entry );
        }

        [Fact]
        public void OrganizerWillMessageForUnknownEntry()
        {
            OrganizerCommands.Organizer( "ThisOrganizerDoesNotExist" );

            Assert.True( JournalCommands.InJournal( "not found", "system" ) );
        }

        [Fact]
        public void StopOrganizerWontThrow()
        {
            OrganizerCommands.StopOrganizer();
        }
    }
}
