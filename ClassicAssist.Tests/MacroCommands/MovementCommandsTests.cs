using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests.MacroCommands
{
    [TestClass]
    public class MovementCommandsTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            Engine.Player = null;
            Engine.Journal.Clear();
        }

        [TestMethod]
        public void WalkWillReturnFalseForInvalidDirection()
        {
            // "Invalid" is itself a Direction member (Utility.GetEnumValueByName throws for a name
            // that matches none at all), and is exactly what Move()'s own guard checks for.
            Assert.IsFalse( MovementCommands.Walk( "Invalid" ) );
        }

        [TestMethod]
        public void RunWillReturnFalseForInvalidDirection()
        {
            Assert.IsFalse( MovementCommands.Run( "Invalid" ) );
        }

        [TestMethod]
        public void SetForceWalkWillSendPacketAndMessage()
        {
            byte[] sent = null;

            void OnReceived( byte[] data, int length )
            {
                if ( data[0] == 0xBF && data[4] == 0x26 )
                {
                    sent = data;
                }
            }

            Engine.InternalPacketReceivedEvent += OnReceived;

            MovementCommands.SetForceWalk( true );

            Engine.InternalPacketReceivedEvent -= OnReceived;

            Assert.IsNotNull( sent );
            Assert.AreEqual( 2, sent[5] );
            Assert.IsTrue( JournalCommands.InJournal( "Force Walk On", "system" ) );
        }

        [TestMethod]
        public void ToggleForceWalkWillFlip()
        {
            MovementCommands.ToggleForceWalk();

            bool afterFirstToggle = JournalCommands.InJournal( "Force Walk On", "system" );

            Engine.Journal.Clear();

            MovementCommands.ToggleForceWalk();

            bool afterSecondToggle = JournalCommands.InJournal( "Force Walk Off", "system" );

            Assert.IsTrue( afterFirstToggle );
            Assert.IsTrue( afterSecondToggle );
        }

        [TestMethod]
        public void PathfindWillFailWhenDistanceExceeded()
        {
            bool result = MovementCommands.Pathfind( 1000, 1000, 0 );

            Assert.IsFalse( result );
            Assert.IsTrue( JournalCommands.InJournal( "Maximum distance exceeded", "system" ) );
        }

        [TestMethod]
        public void PathfindObjectWillFailWhenEntityNotFound()
        {
            bool result = MovementCommands.Pathfind( "ThisAliasDoesNotExist" );

            Assert.IsFalse( result );
            Assert.IsTrue( JournalCommands.InJournal( "not found", "system" ) );
        }

        [TestMethod]
        public void PathfindMinusOneWillCancelAndReturnTrue()
        {
            Assert.IsTrue( MovementCommands.Pathfind( (object) -1 ) );
        }

    }
}
