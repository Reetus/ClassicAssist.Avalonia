using System;
using ClassicAssist.Data.Macros.Commands;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class TimerTests
    {
        [Fact]
        public void WillAddOffset()
        {
            TimerCommands.CreateTimer( "shmoo" );
            TimerCommands.SetTimer( "shmoo", (int) TimeSpan.FromMinutes( 2 ).TotalMilliseconds );

            long timerValue = TimerCommands.Timer( "shmoo" );

            TimeSpan timespan = TimeSpan.FromMilliseconds( timerValue );

            Assert.Equal( 2, (int) timespan.TotalMinutes );

            TimerCommands.RemoveTimer( "shmoo" );
        }

        [Fact]
        public void WillCreateTimer()
        {
            TimerCommands.CreateTimer( "shmoo2" );

            Assert.True( TimerCommands.TimerExists( "shmoo2" ) );

            TimerCommands.RemoveTimer( "shmoo2" );
        }

        [Fact]
        public void WillRemoveTimer()
        {
            TimerCommands.CreateTimer( "shmoo3" );
            TimerCommands.RemoveTimer( "shmoo3" );

            Assert.False( TimerCommands.TimerExists( "shmoo3" ) );
        }

        [Fact]
        public void WillCreateTimerSetTimer()
        {
            TimerCommands.SetTimer( "shmoo4" );
            Assert.True( TimerCommands.TimerExists( "shmoo4" ) );
        }
    }
}