using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using System;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class ConsumeCommandsTests : IDisposable
    {
        public void Dispose()
        {
            Engine.Player = null;
            Engine.Journal.Clear();
        }

        [Fact]
        public void BandageSelfWillMessageWithNoPlayer()
        {
            Engine.Player = null;

            bool result = ConsumeCommands.BandageSelf();

            Assert.False( result );
            Assert.True( JournalCommands.InJournal( "No Player", "system" ) );
        }

        [Fact]
        public void BandageSelfWillMessageWithNoBackpack()
        {
            Engine.Player = new PlayerMobile( 0x01 );

            bool result = ConsumeCommands.BandageSelf();

            Assert.False( result );
            Assert.True( JournalCommands.InJournal( "Cannot find player backpack", "system" ) );
        }

        [Fact]
        public void BandageSelfWillMessageWithNoBandageInBackpack()
        {
            Engine.Player = new PlayerMobile( 0x01 );

            Item backpack = new Item( 0x40005000, Engine.Player.Serial )
            {
                Container = new ItemCollection( 0x40005000 )
            };

            Engine.Items.Add( backpack );
            Engine.Player.SetLayer( Layer.Backpack, backpack.Serial );

            // Deliberately not a bandage (0xe21) ID, so the backpack search comes up empty.
            Item notABandage = new Item( 0x40005001 ) { ID = 0x1234 };
            backpack.Container.Add( notABandage );

            bool result = ConsumeCommands.BandageSelf();

            Assert.False( result );
            Assert.True( JournalCommands.InJournal( "Cannot find type", "system" ) );

            Engine.Items.Remove( backpack.Serial );
        }
    }
}
