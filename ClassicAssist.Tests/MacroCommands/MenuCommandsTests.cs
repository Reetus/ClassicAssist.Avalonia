using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.UO.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests.MacroCommands
{
    [TestClass]
    public class MenuCommandsTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            Engine.Menus.Clear();
        }

        [TestMethod]
        public void MenuExistsWillFindRegisteredMenu()
        {
            Engine.Menus.Add( new Menu { ID = 0x3001, Title = "Test Menu" } );

            Assert.IsTrue( MenuCommands.MenuExists( 0x3001 ) );
            Assert.IsFalse( MenuCommands.MenuExists( 0x9999 ) );
        }

        [TestMethod]
        public void InMenuWillMatchTitle()
        {
            // Entries: [] - InMenu falls through to Entries.Any(...) with no null guard once the
            // title check misses, which throws for a menu built (as real ones from the server
            // always are) with entries.
            Engine.Menus.Add( new Menu { ID = 0x3002, Title = "Choose a Weapon", Entries = [] } );

            Assert.IsTrue( MenuCommands.InMenu( 0x3002, "weapon" ) );
            Assert.IsFalse( MenuCommands.InMenu( 0x3002, "armor" ) );
        }

        [TestMethod]
        public void InMenuWillMatchEntryTitle()
        {
            Engine.Menus.Add( new Menu
            {
                ID = 0x3003, Title = "Choose", Entries = [new MenuEntry { Title = "Longsword" }]
            } );

            Assert.IsTrue( MenuCommands.InMenu( 0x3003, "longsword" ) );
        }

        [TestMethod]
        public void InMenuWillBeFalseWhenMenuMissing()
        {
            Assert.IsFalse( MenuCommands.InMenu( 0x8888, "anything" ) );
        }

        [TestMethod]
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

            Assert.IsNotNull( serverPacket );
            Assert.IsNotNull( clientPacket );
        }

        [TestMethod]
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

            Assert.IsNotNull( clientPacket );
        }
    }
}
