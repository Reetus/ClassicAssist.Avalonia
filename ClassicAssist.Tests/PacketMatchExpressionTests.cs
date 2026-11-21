using System;
using System.Linq;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UI.ViewModels;
using ClassicAssist.UI.Misc;
using ClassicAssist.UO.Network.PacketFilter;
using Xunit;

namespace ClassicAssist.Tests
{
    public class PacketMatchExpressionTests
    {
        // 0xF3 world item: cmd, 0x0001, type, serial 0x4001ABCD, graphic 0x0EED (gold), ...
        private static readonly byte[] _worldItem =
        [
            0xF3, 0x00, 0x01, 0x00, 0x40, 0x01, 0xAB, 0xCD, 0x0E, 0xED, 0x00, 0x01, 0xF4, 0x01, 0xF4
        ];

        // 0x25 add to container: cmd, serial 0x4001ABCD, ...
        private static readonly byte[] _addToContainer = [0x25, 0x40, 0x01, 0xAB, 0xCD, 0x0E, 0xED];

        private static bool Matches( string filter, byte[] data )
        {
            return PacketMatchExpression.Parse( filter ).IsMatch( data );
        }

        [Theory]
        [InlineData( "" )]
        [InlineData( "   " )]
        [InlineData( null )]
        public void EmptyFilterMatchesEverything( string filter )
        {
            Assert.True( Matches( filter, _worldItem ) );
        }

        [Theory]
        [InlineData( "[0] = 0xF3", true )]
        [InlineData( "[0]=0xf3", true )]
        [InlineData( "[0] == 243", true )]
        [InlineData( "[0] = 0x25", false )]
        [InlineData( "[4..7] = 0x4001ABCD", true )]
        [InlineData( "[4..7] = 0x4001ABCE", false )]
        [InlineData( "[0] = 0xF3 && [4..7] = 0x4001abcd", true )]
        [InlineData( "[0] = 0xF3 and [8..9] = 0x0EED", true )]
        [InlineData( "[0] = 0xF3 && [8..9] = 0x0EEE", false )]
        [InlineData( "[5] = !0x01", false )]
        [InlineData( "[5]=!0xAA", true )]
        [InlineData( "[5] != 0xAA", true )]
        [InlineData( "[5] != 0x01", false )]
        [InlineData( "[0] = 0x25 || [0] = 0xF3", true )]
        [InlineData( "[0] = 0x25 or [0] = 0x1A", false )]
        [InlineData( "![0] = 0x73", true )]
        [InlineData( "not [0] = 0xF3", false )]
        [InlineData( "!([0] = 0x25 || [0] = 0xF3)", false )]
        [InlineData( "[4..7] > 0x40000000", true )]
        [InlineData( "[4..7] < 0x40000000", false )]
        [InlineData( "[1..2] >= 1 && [1..2] <= 1", true )]
        [InlineData( "len = 15", true )]
        [InlineData( "LEN > 20", false )]
        [InlineData( "contains 0x4001ABCD", true )]
        [InlineData( "contains 0x4001ABCE", false )]
        [InlineData( "contains 0x0EED", true )]
        [InlineData( "contains 0xF4", true )]
        public void MatchesWorldItem( string filter, bool expected )
        {
            Assert.Equal( expected, Matches( filter, _worldItem ) );
        }

        [Fact]
        public void AndBindsTighterThanOr()
        {
            // [0]=0xF3 || ( [0]=0x25 && [1]=0x01 ) is true for the world item; read left to right,
            // ( [0]=0xF3 || [0]=0x25 ) && [1]=0x01, it would be false.
            Assert.True( Matches( "[0] = 0xF3 || [0] = 0x25 && [1] = 0x01", _worldItem ) );
        }

        [Fact]
        public void FindsOneSerialAcrossPacketTypes()
        {
            // The serial sits at a different offset in each packet; contains finds it in both.
            Assert.True( Matches( "contains 0x4001ABCD", _addToContainer ) );
            Assert.True( Matches( "contains 0x4001ABCD", _worldItem ) );
            Assert.True( Matches( "[0] = 0x25 && [1..4] = 0x4001ABCD", _addToContainer ) );
        }

        [Fact]
        public void ByteTermNeverMatchesTooShortPacket()
        {
            byte[] shortPacket = [0xF3, 0x00];

            Assert.False( Matches( "[5] = 0xAA", shortPacket ) );
            Assert.False( Matches( "[5] != 0xAA", shortPacket ) );
            Assert.True( Matches( "!([5] = 0xAA)", shortPacket ) );
            Assert.False( Matches( "[1..2] = 0", shortPacket ) );
        }

        [Fact]
        public void HexContainsKeepsWrittenWidth()
        {
            byte[] data = [0x01, 0x34];

            Assert.True( Matches( "contains 0x34", data ) );
            Assert.False( Matches( "contains 0x0034", data ) );
            Assert.True( Matches( "contains 0x134", data ) );
        }

        [Fact]
        public void ContainsAcceptsLongSequences()
        {
            Assert.True( Matches( "contains 0x004001ABCD0EED0001F401F4", _worldItem ) );
        }

        [Fact]
        public void EightByteFieldCompares()
        {
            byte[] data = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE];

            Assert.True( Matches( "[0..7] = 0xFFFFFFFFFFFFFFFE", data ) );
        }

        [Theory]
        [InlineData( "[0]", "Expected '='" )]
        [InlineData( "[0] = ", "Expected a value" )]
        [InlineData( "[0 = 1", "Expected ']'" )]
        [InlineData( "[0] = 0x1FF", "does not fit in 1 byte" )]
        [InlineData( "[1..2] = 0x10000", "does not fit in 2 bytes" )]
        [InlineData( "[4..1] = 1", "ends before it starts" )]
        [InlineData( "[0..8] = 1", "at most 8" )]
        [InlineData( "[0] = 0x", "Expected hex digits" )]
        [InlineData( "[0] = 0xZZ", "Expected hex digits" )]
        [InlineData( "[0] = 12ab", "Invalid number" )]
        [InlineData( "[0] = 1 &&", "Expected '['" )]
        [InlineData( "[0] = 1 [1] = 2", "Expected '&&' or '||'" )]
        [InlineData( "([0] = 1", "Expected ')'" )]
        [InlineData( "foo = 1", "Unknown keyword 'foo'" )]
        [InlineData( "[0] = 1 & [1] = 2", "Unexpected '&'" )]
        [InlineData( "[0] = 0x10000000000000000", "larger than 8 bytes" )]
        [InlineData( "[70000] = 1", "past the largest packet size" )]
        public void RejectsInvalidFilter( string filter, string message )
        {
            Assert.False( PacketMatchExpression.TryParse( filter, out PacketMatchExpression expression, out string error ) );
            Assert.Null( expression );
            Assert.Contains( message, error );
        }

        [Fact]
        public void ErrorsReportColumn()
        {
            PacketMatchExpression.TryParse( "[0] = 0xF3 && [1] ? 2", out _, out string error );

            Assert.Equal( "Unexpected '?' at column 19", error );
        }

        [Fact]
        public void LeadingZerosDoNotCountTowardsWidth()
        {
            Assert.True( Matches( "[0] = 0x00F3", _worldItem ) );
        }
    }

    /// <summary>
    ///     The Debug window's filter is a view over the capture: it hides packets, never drops them, so
    ///     changing or clearing it shows what was captured before.
    /// </summary>
    public class DebugViewModelFilterTests : IDisposable
    {
        private readonly IDispatcher _originalDispatcher;
        private readonly DebugViewModel _viewModel;

        public DebugViewModelFilterTests()
        {
            _originalDispatcher = Engine.Dispatcher;
            Engine.Dispatcher = new InlineDispatcher();

            _viewModel = new DebugViewModel();
        }

        public void Dispose()
        {
            _viewModel.Dispose();
            Engine.Dispatcher = _originalDispatcher;
        }

        private void Capture( params byte[] data )
        {
            _viewModel.ProcessQueue( new PacketEntry { Data = data, Direction = PacketDirection.Incoming } );
        }

        private byte[] Commands()
        {
            return _viewModel.Items.Select( e => e.Data[0] ).ToArray();
        }

        [Fact]
        public void FilterAppliesToNewAndExistingPackets()
        {
            Capture( 0x1A, 0x01 );
            Capture( 0x25, 0x02 );

            _viewModel.Filter = "[0] = 0x25 || [0] = 0x1D";

            Assert.Equal( new byte[] { 0x25 }, Commands() );

            Capture( 0x1D, 0x03 );
            Capture( 0x73, 0x04 );

            Assert.Equal( new byte[] { 0x25, 0x1D }, Commands() );

            _viewModel.Filter = string.Empty;

            Assert.Equal( new byte[] { 0x1A, 0x25, 0x1D, 0x73 }, Commands() );
            Assert.Null( _viewModel.FilterError );
        }

        [Fact]
        public void InvalidFilterKeepsLastValidOneAndReportsError()
        {
            Capture( 0x1A );
            Capture( 0x25 );

            _viewModel.Filter = "[0] = 0x25";
            _viewModel.Filter = "[0] = 0x";

            Assert.NotNull( _viewModel.FilterError );
            Assert.Equal( new byte[] { 0x25 }, Commands() );

            _viewModel.Filter = "[0] = 0x1A";

            Assert.Null( _viewModel.FilterError );
            Assert.Equal( new byte[] { 0x1A }, Commands() );
        }

        [Fact]
        public void ClearDropsHiddenPacketsToo()
        {
            Capture( 0x1A );
            _viewModel.Filter = "[0] = 0x25";

            _viewModel.ClearCommand.Execute( null );
            _viewModel.Filter = string.Empty;

            Assert.Empty( _viewModel.Items );
        }
    }
}
