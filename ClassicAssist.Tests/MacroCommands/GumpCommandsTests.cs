using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.UO.Objects;
using ClassicAssist.UO.Objects.Gumps;
using System;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class GumpCommandsTests : IDisposable
    {
        public void Dispose()
        {
            Engine.Player = null;
            Engine.Gumps.Clear();
            Engine.Journal.Clear();
        }

        [Fact]
        public void GumpExistsWillFindRegisteredGump()
        {
            Engine.GumpList[0x1234] = 1;

            Assert.True( GumpCommands.GumpExists( 0x1234 ) );
            Assert.False( GumpCommands.GumpExists( 0x9999 ) );

            Engine.GumpList.TryRemove( 0x1234, out _ );
        }

        [Fact]
        public void InGumpWillMatchElementText()
        {
            Gump gump = new Gump( 0, 0, 1, 0x2001 ) { GumpElements = [new GumpElement { Text = "Hello World" }] };

            Engine.Gumps.Add( gump );

            Assert.True( GumpCommands.InGump( 0x2001, "hello" ) );
            Assert.False( GumpCommands.InGump( 0x2001, "goodbye" ) );

            Engine.Gumps.Remove( 0x2001 );
        }

        [Fact]
        public void InGumpWillMessageWhenGumpMissing()
        {
            bool result = GumpCommands.InGump( 0x7777, "anything" );

            Assert.False( result );
            Assert.True( JournalCommands.InJournal( "Invalid gump", "system" ) );
        }

        [Fact]
        public void CloseGumpWillSendCloseClientGumpPacket()
        {
            Gump gump = new Gump( 0, 0, 2, 0x2002 );

            Engine.Gumps.Add( gump );

            byte[] received = null;

            void OnReceived( byte[] data, int length )
            {
                // byte[1..2]: short packet length (13). byte[3..4]: short subcommand (0x04).
                if ( data[0] == 0xBF && data[4] == 0x04 )
                {
                    received = data;
                }
            }

            Engine.InternalPacketReceivedEvent += OnReceived;

            GumpCommands.CloseGump( 2 );

            Engine.InternalPacketReceivedEvent -= OnReceived;

            Assert.NotNull( received );

            Engine.Gumps.Remove( 0x2002 );
        }

        [Fact]
        public void OpenGuildGumpWillSendPacket()
        {
            Engine.Player = new PlayerMobile( 0x01 );

            byte[] sent = null;

            void OnSent( byte[] data, int length )
            {
                sent = data;
            }

            Engine.InternalPacketSentEvent += OnSent;

            GumpCommands.OpenGuildGump();

            Engine.InternalPacketSentEvent -= OnSent;

            Assert.NotNull( sent );
            Assert.Equal( 0xD7, sent[0] );
            Assert.Equal( 0x28, sent[8] );
        }

        [Fact]
        public void OpenQuestsGumpWillSendPacket()
        {
            Engine.Player = new PlayerMobile( 0x01 );

            byte[] sent = null;

            void OnSent( byte[] data, int length )
            {
                sent = data;
            }

            Engine.InternalPacketSentEvent += OnSent;

            GumpCommands.OpenQuestsGump();

            Engine.InternalPacketSentEvent -= OnSent;

            Assert.NotNull( sent );
            Assert.Equal( 0xD7, sent[0] );
            Assert.Equal( 0x32, sent[8] );
        }

        [Fact]
        public void OpenHelpGumpWillSendPacket()
        {
            Engine.Player = new PlayerMobile( 0x01 );

            byte[] sent = null;

            void OnSent( byte[] data, int length )
            {
                sent = data;
            }

            Engine.InternalPacketSentEvent += OnSent;

            GumpCommands.OpenHelpGump();

            Engine.InternalPacketSentEvent -= OnSent;

            Assert.NotNull( sent );
            Assert.Equal( 0x9B, sent[0] );
        }

        [Fact]
        public void OpenVirtueGumpWillMessageWhenSerialInvalid()
        {
            // obj: null resolves through AliasCommands.ResolveSerial to Engine.Player's serial,
            // or 0 with no player set - the cleanest way to hit the "not found" branch without
            // also triggering AliasCommands' own "unknown alias" message for a string alias miss.
            Engine.Player = null;

            bool result = false;

            void OnSent( byte[] data, int length )
            {
                result = true;
            }

            Engine.InternalPacketSentEvent += OnSent;

            GumpCommands.OpenVirtueGump();

            Engine.InternalPacketSentEvent -= OnSent;

            Assert.False( result );
            Assert.True( JournalCommands.InJournal( "Mobile not found", "system" ) );
        }
    }
}
