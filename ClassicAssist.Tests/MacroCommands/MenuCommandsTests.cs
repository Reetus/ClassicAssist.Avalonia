using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.UO.Objects;
using System;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class MenuCommandsTests : IDisposable
    {
        public void Dispose()
        {
            Engine.Menus.Clear();
        }

        [Fact]
        public void MenuExistsWillFindRegisteredMenu()
        {
            Engine.Menus.Add( new Menu { ID = 0x3001, Title = "Test Menu" } );

            Assert.True( MenuCommands.MenuExists( 0x3001 ) );
            Assert.False( MenuCommands.MenuExists( 0x9999 ) );
        }

        [Fact]
        public void InMenuWillMatchTitle()
        {
            // Entries: [] - InMenu falls through to Entries.Any(...) with no null guard once the
            // title check misses, which throws for a menu built (as real ones from the server
            // always are) with entries.
            Engine.Menus.Add( new Menu { ID = 0x3002, Title = "Choose a Weapon", Entries = [] } );

            Assert.True( MenuCommands.InMenu( 0x3002, "weapon" ) );
            Assert.False( MenuCommands.InMenu( 0x3002, "armor" ) );
        }

        [Fact]
        public void InMenuWillMatchEntryTitle()
        {
            Engine.Menus.Add( new Menu
            {
                ID = 0x3003, Title = "Choose", Entries = [new MenuEntry { Title = "Longsword" }]
            } );

            Assert.True( MenuCommands.InMenu( 0x3003, "longsword" ) );
        }

        [Fact]
        public void InMenuWillBeFalseWhenMenuMissing()
        {
            Assert.False( MenuCommands.InMenu( 0x8888, "anything" ) );
        }

        [Fact]
        public void CloseMenuWillSendMenuButtonClickAndCloseClientGump()
        {
            Engine.Menus.Add( new Menu { ID = 0x3004, Serial = 0x40000001, Title = "Test" } );

            byte[] serverPacket = null;
            byte[] clientPacket = null;

            void OnSent( byte[] data, int length )
            {
                if ( data[0] == 0x7D )
                {
                    serverPacket = data;
                }
            }

            void OnReceived( byte[] data, int length )
            {
                if ( data[0] == 0xBF )
                {
                    clientPacket = data;
                }
            }

            Engine.InternalPacketSentEvent += OnSent;
            Engine.InternalPacketReceivedEvent += OnReceived;

            MenuCommands.CloseMenu( 0x3004 );

            Engine.InternalPacketSentEvent -= OnSent;
            Engine.InternalPacketReceivedEvent -= OnReceived;

            Assert.NotNull( serverPacket );
            Assert.NotNull( clientPacket );
        }

        [Fact]
        public void ReplyMenuWillSendCloseClientGumpEvenWithoutMenu()
        {
            byte[] clientPacket = null;

            void OnReceived( byte[] data, int length )
            {
                if ( data[0] == 0xBF )
                {
                    clientPacket = data;
                }
            }

            Engine.InternalPacketReceivedEvent += OnReceived;

            MenuCommands.ReplyMenu( 0x3005, 1 );

            Engine.InternalPacketReceivedEvent -= OnReceived;

            Assert.NotNull( clientPacket );
        }
    }
}
