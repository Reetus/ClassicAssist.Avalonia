using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Shared;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class JournalCommandsTests
    {
        [Fact]
        public void WillMatchSystem()
        {
            Engine.Journal.Write( new JournalEntry
            {
                Text = "You are now under the protection of town guards.", SpeechType = JournalSpeech.System
            } );

            Assert.True( JournalCommands.InJournal( "town guards", "system" ) );

            Engine.Journal.Clear();
        }

        [Fact]
        public void WillMatchSystemRegular()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            Cliloc.Initialize( TestData.UOPath );

            byte[] packet =
            {
                0xC1, 0x00, 0x32, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x03, 0xB2, 0x00, 0x03, 0x00, 0x07,
                0xA4, 0xE1, 0x53, 0x79, 0x73, 0x74, 0x65, 0x6D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00
            };

            IncomingPacketHandlers.Initialize();

            PacketHandler handler = IncomingPacketHandlers.GetHandler( 0xc1 );

            Assert.NotNull( handler );

            handler.OnReceive( new PacketReader( packet, packet.Length, false ) );

            Assert.True( JournalCommands.InJournal( "fingers slip!", "system" ) );
        }

        [Fact]
        public void WillMatchAuthor()
        {
            Engine.Journal.Write( new JournalEntry
            {
                Text = "The quick brown bow jumped over the lazy dog.",
                SpeechType = JournalSpeech.Say,
                Name = "Tony"
            } );

            Assert.True( JournalCommands.InJournal( "lazy dog", "tony" ) );

            Engine.Journal.Clear();
        }

        [Fact]
        public void WillMatchAny()
        {
            Engine.Journal.Write( new JournalEntry
            {
                Text = "You are now under the protection of town guards.", SpeechType = JournalSpeech.System
            } );

            Engine.Journal.Write( new JournalEntry
            {
                Text = "The quick brown bow jumped over the lazy dog.",
                SpeechType = JournalSpeech.Say,
                Name = "Tony"
            } );

            Assert.True( JournalCommands.InJournal( "town guards" ) );

            Assert.True( JournalCommands.InJournal( "lazy dog" ) );

            Engine.Journal.Clear();
        }

        [Fact]
        public void WillClearJournal()
        {
            Engine.Journal.Write( new JournalEntry
            {
                Text = "The quick brown bow jumped over the lazy dog.",
                SpeechType = JournalSpeech.Say,
                Name = "Tony"
            } );

            // ClearJournal advances the "main" buffer past everything written so far, so the
            // pre-existing entry is no longer matched...
            JournalCommands.ClearJournal();

            Assert.False( JournalCommands.InJournal( "lazy dog" ) );

            // ...but entries written afterwards are.
            Engine.Journal.Write( new JournalEntry
            {
                Text = "The quick brown bow jumped over the lazy dog.",
                SpeechType = JournalSpeech.Say,
                Name = "Tony"
            } );

            Assert.True( JournalCommands.InJournal( "lazy dog" ) );

            Engine.Journal.Clear();
        }

        /// <summary>
        ///     WaitForJournal returns as soon as a matching system message arrives as a real 0xAE packet,
        ///     long before its timeout.
        /// </summary>
        [Fact]
        public async Task WaitForJournalWillMatch()
        {
            EngineState.Reset();

            const string text = "You are now under the protection of town guards.";

            // Big-endian UTF-16, NUL terminated.
            byte[] textBytes = Encoding.BigEndianUnicode.GetBytes( text + '\0' );
            int length = 48 + textBytes.Length;

            PacketWriter pw = new PacketWriter( length );
            pw.Write( (byte) 0xAE );
            pw.Write( (short) length );
            pw.Write( -1 ); // serial
            pw.Write( (short) -1 ); // graphic
            pw.Write( (byte) JournalSpeech.System );
            pw.Write( (short) 0x3B2 ); // hue
            pw.Write( (short) 3 ); // font
            pw.Write( new byte[4], 0, 4 ); // language
            pw.Write( new byte[30], 0, 30 ); // name
            pw.Write( textBytes, 0, textBytes.Length );
            byte[] packet = pw.ToArray();

            FieldInfo field = typeof( IncomingPacketHandlers ).GetField( "JournalEntryAddedEvent",
                BindingFlags.Static | BindingFlags.NonPublic );

            int Handlers()
            {
                return ( (Delegate) field?.GetValue( null ) )?.GetInvocationList().Length ?? 0;
            }

            int before = Handlers();

            Stopwatch stopwatch = Stopwatch.StartNew();

            Task<bool> wait = Task.Run( () => JournalCommands.WaitForJournal( "town guards", 20000, "system" ),
                TestContext.Current.CancellationToken );

            // Only send once the wait is listening; a message that arrives first is (by design) not seen.
            Assert.True( SpinWait.SpinUntil( () => Handlers() > before, 5000 ) );

            EngineState.Receive( packet );

            Assert.True( await wait.WaitAsync( TimeSpan.FromSeconds( 5 ), TestContext.Current.CancellationToken ) );
            Assert.True( stopwatch.ElapsedMilliseconds < 5000 );
            Assert.Equal( before, Handlers() );

            Engine.Journal.Clear();
        }
    }
}