using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using System;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class MovementCommandsTests : IDisposable
    {
        public void Dispose()
        {
            Engine.Player = null;
            Engine.Journal.Clear();
        }

        [Fact]
        public void WalkWillReturnFalseForInvalidDirection()
        {
            // "Invalid" is itself a Direction member (Utility.GetEnumValueByName throws for a name
            // that matches none at all), and is exactly what Move()'s own guard checks for.
            Assert.False( MovementCommands.Walk( "Invalid" ) );
        }

        [Fact]
        public void RunWillReturnFalseForInvalidDirection()
        {
            Assert.False( MovementCommands.Run( "Invalid" ) );
        }

        [Fact]
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

            Assert.NotNull( sent );
            Assert.Equal( 2, sent[5] );
            Assert.True( JournalCommands.InJournal( "Force Walk On", "system" ) );
        }

        [Fact]
        public void ToggleForceWalkWillFlip()
        {
            MovementCommands.ToggleForceWalk();

            bool afterFirstToggle = JournalCommands.InJournal( "Force Walk On", "system" );

            Engine.Journal.Clear();

            MovementCommands.ToggleForceWalk();

            bool afterSecondToggle = JournalCommands.InJournal( "Force Walk Off", "system" );

            Assert.True( afterFirstToggle );
            Assert.True( afterSecondToggle );
        }

        [Fact]
        public void PathfindWillFailWhenDistanceExceeded()
        {
            bool result = MovementCommands.Pathfind( 1000, 1000, 0 );

            Assert.False( result );
            Assert.True( JournalCommands.InJournal( "Maximum distance exceeded", "system" ) );
        }

        [Fact]
        public void PathfindObjectWillFailWhenEntityNotFound()
        {
            bool result = MovementCommands.Pathfind( "ThisAliasDoesNotExist" );

            Assert.False( result );
            Assert.True( JournalCommands.InJournal( "not found", "system" ) );
        }

        [Fact]
        public void PathfindMinusOneWillCancelAndReturnTrue()
        {
            Assert.True( MovementCommands.Pathfind( (object) -1 ) );
        }

    }
}
