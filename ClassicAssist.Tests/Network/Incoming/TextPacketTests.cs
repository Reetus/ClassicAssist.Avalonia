using System.Collections.Generic;
using System.Linq;
using System.Text;
using ClassicAssist.Data;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using Xunit;

namespace ClassicAssist.Tests.Network.Incoming
{
    /// <summary>
    ///     Server text into the journal: 0x1C (ASCII), 0xAE (big-endian unicode), 0xC1 (cliloc with
    ///     little-endian, tab-separated arguments) and 0xCC (cliloc with an ASCII affix before or after). The
    ///     journal is what InJournal / WaitForJournal and every "wait for the server to say X" macro read, and
    ///     each packet encodes its strings differently - a byte order or argument split gone wrong turns
    ///     every match into a miss.
    /// </summary>
    public class TextPacketTests : IncomingPacketTestBase
    {
        private const int SPEAKER = 0x00000601;

        [Fact]
        public void WillJournalAndRaiseEventOnAsciiText()
        {
            List<JournalEntry> raised = new List<JournalEntry>();

            void OnAdded( JournalEntry entry )
            {
                raised.Add( entry );
            }

            IncomingPacketHandlers.JournalEntryAddedEvent += OnAdded;

            try
            {
                Receive( Dynamic( 0x1C, w =>
                {
                    w.Write( SPEAKER );
                    w.Write( (short) 0x0190 );
                    w.Write( (byte) JournalSpeech.Yell );
                    w.Write( (short) 0x0035 );
                    w.Write( (short) 3 );
                    w.WriteAsciiFixed( "Shopkeeper", 30 );
                    w.WriteAsciiNull( "Vendor buy" );
                } ) );
            }
            finally
            {
                IncomingPacketHandlers.JournalEntryAddedEvent -= OnAdded;
            }

            JournalEntry entry = Assert.Single( Engine.Journal.GetEntireBuffer() );

            Assert.Same( entry, Assert.Single( raised ) );
            Assert.Equal( SPEAKER, entry.Serial );
            Assert.Equal( 0x0190, entry.ID );
            Assert.Equal( JournalSpeech.Yell, entry.SpeechType );
            Assert.Equal( 0x0035, entry.SpeechHue );
            Assert.Equal( 3, entry.SpeechFont );
            Assert.Equal( "Shopkeeper", entry.Name );
            Assert.Equal( "Vendor buy", entry.Text );
        }

        [Fact]
        public void WillDecodeBigEndianUnicodeOnUnicodeText()
        {
            Receive( Dynamic( 0xAE, w =>
            {
                w.Write( SPEAKER );
                w.Write( (short) 0x0190 );
                w.Write( (byte) JournalSpeech.Say );
                w.Write( (short) 0x03B2 );
                w.Write( (short) 3 );
                w.WriteAsciiFixed( "ENU", 4 );
                w.WriteAsciiFixed( "Erindel", 30 );
                w.WriteBigUniNull( "Héllo wörld ☺" );
            } ) );

            JournalEntry entry = Assert.Single( Engine.Journal.GetEntireBuffer() );

            Assert.Equal( "ENU", entry.SpeechLanguage );
            Assert.Equal( "Héllo wörld ☺", entry.Text );
            Assert.Equal( 0x03B2, entry.SpeechHue );
        }

        /// <summary>
        ///     0xC1 arguments are little-endian unicode separated by tabs and end with a two-byte terminator
        ///     that must not end up in the last argument.
        /// </summary>
        [Fact]
        public void WillSplitArgumentsAndFormatClilocOnLocalizedText()
        {
            Receive( Localized( SPEAKER, JournalSpeech.Say, 500001, "System", "Bob\t12" ) );

            JournalEntry entry = Assert.Single( Engine.Journal.GetEntireBuffer() );

            Assert.Equal( 500001, entry.Cliloc );
            Assert.Equal( new[] { "Bob", "12" }, entry.Arguments );
            Assert.Equal( "You see Bob with 12", entry.Text );
            Assert.Equal( "System", entry.Name );
        }

        [Fact]
        public void WillUseClilocAsIsWhenNoArgumentsOnLocalizedText()
        {
            Receive( Localized( -1, JournalSpeech.System, 500000, "System", "" ) );

            JournalEntry entry = Assert.Single( Engine.Journal.GetEntireBuffer() );

            Assert.Empty( entry.Arguments );
            Assert.Equal( "plain message", entry.Text );
        }

        /// <summary>
        ///     A label (the name shown on a single click) for the last, enemy or friend target gets the
        ///     configured head message, so a player can see which mobile a hotkey will hit.
        /// </summary>
        [Fact]
        public void WillShowLastTargetHeadMessageOnLabelForLastTarget()
        {
            AddMobile( SPEAKER, "a mongbat" );
            Player.LastTargetSerial = SPEAKER;
            Options.CurrentOptions.LastTargetMessage = "[LT]";

            using PacketRecorder recorder = new PacketRecorder();

            Receive( Localized( SPEAKER, JournalSpeech.Label, 500000, "a mongbat", "" ) );
            Receive( Localized( SPEAKER, JournalSpeech.Say, 500000, "a mongbat", "" ) );

            byte[] headMessage = Assert.Single( recorder.ReceivedWithId( 0xAE ) );

            Assert.Contains( "[LT]", Encoding.BigEndianUnicode.GetString( headMessage, 48, headMessage.Length - 48 ) );
            Assert.Equal( 2, Engine.Journal.GetEntireBuffer().Length );
        }

        [Fact]
        public void WillPrependAffixOnLocalizedTextAffix()
        {
            Receive( LocalizedAffix( MessageAffixType.Prepend, 500001, "Ahoy! ", "Bob\t3" ) );

            JournalEntry entry = Assert.Single( Engine.Journal.GetEntireBuffer() );

            Assert.Equal( "Ahoy! You see Bob with 3", entry.Text );
            Assert.Equal( new[] { "Bob", "3" }, entry.Arguments );
        }

        /// <summary>
        ///     Append is the zero value of the [Flags] enum, so it is the fall-through: plain append and the
        ///     system flag without prepend both put the affix after the text.
        /// </summary>
        [Theory]
        [InlineData( MessageAffixType.Append )]
        [InlineData( MessageAffixType.System )]
        public void WillAppendAffixUnlessPrependOnLocalizedTextAffix( MessageAffixType affixType )
        {
            Receive( LocalizedAffix( affixType, 500000, " (x2)", "" ) );

            JournalEntry entry = Assert.Single( Engine.Journal.GetEntireBuffer() );

            Assert.Equal( "plain message (x2)", entry.Text );
        }

        [Fact]
        public void WillPrependWhenSystemAndPrependOnLocalizedTextAffix()
        {
            Receive( LocalizedAffix( MessageAffixType.Prepend | MessageAffixType.System, 500000, "> ", "" ) );

            Assert.Equal( "> plain message", Engine.Journal.GetEntireBuffer().Single().Text );
        }

        [Fact]
        public void WillWriteSystemEntryOnCAASystemMessageToJournal()
        {
            IncomingPacketHandlers.CAASystemMessageToJournal( "hello" );

            JournalEntry entry = Assert.Single( Engine.Journal.GetEntireBuffer() );

            Assert.Equal( -1, entry.Serial );
            Assert.Equal( "System", entry.Name );
            Assert.Equal( JournalSpeech.System, entry.SpeechType );
            Assert.Equal( "hello", entry.Text );
        }

        private static byte[] Localized( int serial, JournalSpeech type, int cliloc, string name, string arguments )
        {
            return Dynamic( 0xC1, w =>
            {
                w.Write( serial );
                w.Write( (short) 0x0190 );
                w.Write( (byte) type );
                w.Write( (short) 0x03B2 );
                w.Write( (short) 3 );
                w.Write( cliloc );
                w.WriteAsciiFixed( name, 30 );
                w.WriteLittleUniNull( arguments );
            } );
        }

        private static byte[] LocalizedAffix( MessageAffixType affixType, int cliloc, string affix, string arguments )
        {
            return Dynamic( 0xCC, w =>
            {
                w.Write( SPEAKER );
                w.Write( (short) 0x0190 );
                w.Write( (byte) JournalSpeech.Say );
                w.Write( (short) 0x03B2 );
                w.Write( (short) 3 );
                w.Write( cliloc );
                w.Write( (byte) affixType );
                w.WriteAsciiFixed( "Speaker", 30 );
                w.WriteAsciiNull( affix );
                w.WriteBigUniNull( arguments );
            } );
        }
    }
}
