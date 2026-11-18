using System;
using System.Linq;
using System.Text;
using ClassicAssist.Data;
using ClassicAssist.Data.Filters;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.Filters
{
    /// <summary>
    ///     <see cref="ClilocFilter" /> swaps a localized server message for the user's own text. These pin
    ///     the decision (enabled, a cliloc, a matching entry) and what goes back to the client: the
    ///     replacement text, the entry's hue or the original one, the speaker, the affix placement, and the
    ///     re-attribution of a system message to the player when it is to be shown overhead. Unlike
    ///     ClilocFilterTests these need no client files: the journal entry is built directly.
    /// </summary>
    public class ClilocFilterReplacementTests : IDisposable
    {
        private const int CLILOC = 500118;
        private readonly LockedObservableCollection<FilterClilocEntry> _originalFilters;
        private readonly bool _originalEnabled;

        public ClilocFilterReplacementTests()
        {
            EngineState.ResetWithPlayer( 0x00000005 ).ID = 0x0190;

            _originalFilters = ClilocFilter.Filters;
            _originalEnabled = ClilocFilter.IsEnabled;
            ClilocFilter.Filters = new LockedObservableCollection<FilterClilocEntry>();
            ClilocFilter.IsEnabled = true;
        }

        public void Dispose()
        {
            ClilocFilter.Filters = _originalFilters;
            ClilocFilter.IsEnabled = _originalEnabled;
            EngineState.Reset();
        }

        private static JournalEntry Message( int cliloc = CLILOC, int serial = -1 )
        {
            return new JournalEntry
            {
                Cliloc = cliloc,
                Serial = serial,
                ID = serial == -1 ? -1 : 0x0191,
                Name = "System",
                SpeechHue = 0x03B2,
                SpeechFont = 3,
                SpeechType = JournalSpeech.System
            };
        }

        private static string Text( byte[] packet )
        {
            return Encoding.BigEndianUnicode.GetString( packet, 48, packet.Length - 48 ).TrimEnd( '\0' );
        }

        private static int Int( byte[] packet, int offset )
        {
            return ( packet[offset] << 24 ) | ( packet[offset + 1] << 16 ) | ( packet[offset + 2] << 8 ) | packet[offset + 3];
        }

        private static int Short( byte[] packet, int offset )
        {
            return ( packet[offset] << 8 ) | packet[offset + 1];
        }

        [Fact]
        public void WontReplaceWhenDisabledWithoutAClilocOrWithoutAnEntry()
        {
            ClilocFilter.Filters.Add( new FilterClilocEntry { Cliloc = CLILOC, Replacement = "x" } );

            using PacketRecorder recorder = new PacketRecorder();

            Assert.False( ClilocFilter.CheckMessage( Message( 0 ) ) );
            Assert.False( ClilocFilter.CheckMessage( Message( 500119 ) ) );

            ClilocFilter.IsEnabled = false;

            Assert.False( ClilocFilter.CheckMessage( Message() ) );
            Assert.False( ClilocFilter.CheckMessageAffix( Message(), MessageAffixType.Append, "!" ) );
            Assert.Empty( recorder.Received );
        }

        [Fact]
        public void WillSendTheReplacementWithTheOriginalHueAndSpeaker()
        {
            ClilocFilter.Filters.Add( new FilterClilocEntry { Cliloc = CLILOC, Replacement = "Out of reach" } );

            using PacketRecorder recorder = new PacketRecorder();

            Assert.True( ClilocFilter.CheckMessage( Message( CLILOC, 0x00000009 ) ) );

            byte[] packet = Assert.Single( recorder.ReceivedWithId( 0xAE ) );
            Assert.Equal( "Out of reach", Text( packet ) );
            Assert.Equal( 0x00000009, Int( packet, 3 ) );
            Assert.Equal( 0x0191, Short( packet, 7 ) );
            Assert.Equal( 0x03B2, Short( packet, 10 ) );
            Assert.Equal( 3, Short( packet, 12 ) );
        }

        [Fact]
        public void WillUseTheEntrysHueWhenItHasOne()
        {
            ClilocFilter.Filters.Add( new FilterClilocEntry { Cliloc = CLILOC, Replacement = "x", Hue = 0x0021 } );

            using PacketRecorder recorder = new PacketRecorder();

            ClilocFilter.CheckMessage( Message() );

            Assert.Equal( 0x0021, Short( Assert.Single( recorder.ReceivedWithId( 0xAE ) ), 10 ) );
        }

        [Fact]
        public void WillShowASystemMessageOverThePlayerWhenAskedTo()
        {
            ClilocFilter.Filters.Add( new FilterClilocEntry { Cliloc = CLILOC, Replacement = "x", ShowOverhead = true } );
            ClilocFilter.Filters.Add( new FilterClilocEntry { Cliloc = 500119, Replacement = "y" } );

            using PacketRecorder recorder = new PacketRecorder();

            ClilocFilter.CheckMessage( Message() );
            ClilocFilter.CheckMessage( Message( 500119 ) );

            byte[][] packets = recorder.ReceivedWithId( 0xAE ).ToArray();
            Assert.Equal( 0x00000005, Int( packets[0], 3 ) );
            Assert.Equal( 0x0190, Short( packets[0], 7 ) );

            // Without ShowOverhead it stays a system message with no source.
            Assert.Equal( -1, Int( packets[1], 3 ) );
        }

        [Theory]
        [InlineData( MessageAffixType.Append, "Gold: 15" )]
        [InlineData( MessageAffixType.Prepend, "15Gold: " )]
        [InlineData( MessageAffixType.Prepend | MessageAffixType.System, "15Gold: " )]
        public void WillPlaceTheAffixOnTheRequestedSide( MessageAffixType affixType, string expected )
        {
            ClilocFilter.Filters.Add( new FilterClilocEntry { Cliloc = CLILOC, Replacement = "Gold: " } );

            using PacketRecorder recorder = new PacketRecorder();

            Assert.True( ClilocFilter.CheckMessageAffix( Message(), affixType, "15" ) );

            Assert.Equal( expected, Text( Assert.Single( recorder.ReceivedWithId( 0xAE ) ) ) );
        }

        [Fact]
        public void DeserializeWillKeepTheFirstEntryForACliloc()
        {
            ClilocFilter filter = new ClilocFilter();

            filter.Deserialize( JObject.Parse(
                "{ \"Filters\": [ { \"Key\": 1, \"Value\": \"a\", \"Hue\": 33, \"ShowOverhead\": true }," +
                " { \"Key\": 1, \"Value\": \"b\" }, { \"Key\": 2 } ] }" ) );
            filter.Deserialize( null );

            Assert.Equal( 2, ClilocFilter.Filters.Count );
            FilterClilocEntry first = ClilocFilter.Filters[0];
            Assert.Equal( "a", first.Replacement );
            Assert.Equal( 33, first.Hue );
            Assert.True( first.ShowOverhead );

            FilterClilocEntry second = ClilocFilter.Filters[1];
            Assert.Equal( -1, second.Hue );
            Assert.Null( second.Replacement );

            JObject saved = filter.Serialize();

            filter.ResetOptions();
            Assert.Empty( ClilocFilter.Filters );

            filter.Deserialize( saved );
            Assert.Equal( 2, ClilocFilter.Filters.Count );
            Assert.Equal( "a", ClilocFilter.Filters[0].Replacement );
        }
    }
}
