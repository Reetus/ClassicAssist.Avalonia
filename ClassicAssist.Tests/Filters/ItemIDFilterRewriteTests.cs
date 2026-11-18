using System;
using System.Linq;
using ClassicAssist.Data.Filters;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.UO.Network.PacketFilter;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.Filters
{
    /// <summary>
    ///     <see cref="ItemIDFilter" /> rewrites item graphics (and optionally hues) in place in four server
    ///     packets whose layouts all differ: 0xF3, 0x25 (old and new length), 0x3C (19- or 20-byte item
    ///     records) and 0x1A, where the hue field is optional and may have to be inserted. An off-by-one
    ///     here corrupts the neighbouring field - often the next item's serial - so each layout's offsets
    ///     are pinned on hand-built packets.
    /// </summary>
    public class ItemIDFilterRewriteTests : IDisposable
    {
        private const int SOURCE = 0x0EED;
        private const int DESTINATION = 0x0F7A;
        private readonly ItemIDFilter _filter;

        public ItemIDFilterRewriteTests()
        {
            _filter = new ItemIDFilter { Enabled = true };
        }

        public void Dispose()
        {
            // Every filter instance registers itself in the static list; take this one back out.
            DynamicFilterEntry.Filters.Remove( _filter );
        }

        private void AddEntry( int hue = -1, bool enabled = true, int source = SOURCE, int destination = DESTINATION )
        {
            _filter.Items.Add( new ItemIDFilterEntry { SourceID = source, DestinationID = destination, Hue = hue, Enabled = enabled } );
        }

        private bool Check( ref byte[] packet, PacketDirection direction = PacketDirection.Incoming )
        {
            int length = packet.Length;
            bool result = _filter.CheckPacket( ref packet, ref length, direction );

            Assert.Equal( packet.Length, length );

            return result;
        }

        private static int Short( byte[] packet, int offset )
        {
            return ( packet[offset] << 8 ) | packet[offset + 1];
        }

        private static void PutShort( byte[] packet, int offset, int value )
        {
            packet[offset] = (byte) ( value >> 8 );
            packet[offset + 1] = (byte) value;
        }

        /// <summary>0xF3 world item (SA): graphic at 8, hue at 21.</summary>
        private static byte[] WorldItemF3( int graphic, int hue )
        {
            byte[] packet = new byte[26];
            packet[0] = 0xF3;
            PutShort( packet, 1, 1 );
            PutShort( packet, 8, graphic );
            PutShort( packet, 21, hue );

            return packet;
        }

        /// <summary>0x3C container contents: 5-byte header, then 20-byte (grid) or 19-byte records.</summary>
        private static byte[] ContainerContents( bool grid, params (int Graphic, int Hue)[] items )
        {
            int recordSize = grid ? 20 : 19;
            byte[] packet = new byte[5 + items.Length * recordSize];
            packet[0] = 0x3C;
            PutShort( packet, 1, packet.Length );
            PutShort( packet, 3, items.Length );

            for ( int i = 0; i < items.Length; i++ )
            {
                int offset = 5 + i * recordSize;
                packet[offset] = 0x40;
                packet[offset + 3] = (byte) ( i + 1 );
                PutShort( packet, offset + 4, items[i].Graphic );
                PutShort( packet, offset + recordSize - 2, items[i].Hue );
            }

            return packet;
        }

        [Fact]
        public void WillLeavePacketsAloneWhenDisabledOrOutgoing()
        {
            AddEntry( 0x21 );
            byte[] packet = WorldItemF3( SOURCE, 0 );

            Check( ref packet, PacketDirection.Outgoing );
            Assert.Equal( SOURCE, Short( packet, 8 ) );

            _filter.Enabled = false;
            Check( ref packet );
            Assert.Equal( SOURCE, Short( packet, 8 ) );
        }

        [Fact]
        public void WillRewriteTheGraphicOfAnSAWorldItemAndKeepItsHueByDefault()
        {
            AddEntry();
            byte[] packet = WorldItemF3( SOURCE, 0x0455 );

            Assert.False( Check( ref packet ) );

            Assert.Equal( DESTINATION, Short( packet, 8 ) );
            Assert.Equal( 0x0455, Short( packet, 21 ) );
        }

        [Fact]
        public void WillRewriteTheHueOfAnSAWorldItemWhenTheEntryHasOne()
        {
            AddEntry( 0x0021 );
            byte[] packet = WorldItemF3( SOURCE, 0x0455 );

            Check( ref packet );

            Assert.Equal( 0x0021, Short( packet, 21 ) );
        }

        [Fact]
        public void WillSkipDisabledEntriesAndUseTheFirstEnabledOne()
        {
            AddEntry( -1, false, SOURCE, 0x1111 );
            AddEntry( -1, true, SOURCE, 0x2222 );
            AddEntry( -1, true, SOURCE, 0x3333 );
            byte[] packet = WorldItemF3( SOURCE, 0 );

            Check( ref packet );

            Assert.Equal( 0x2222, Short( packet, 8 ) );
        }

        [Fact]
        public void WillLeaveOtherGraphicsAlone()
        {
            AddEntry( 0x21 );
            byte[] packet = WorldItemF3( 0x1234, 0x0455 );

            Check( ref packet );

            Assert.Equal( 0x1234, Short( packet, 8 ) );
            Assert.Equal( 0x0455, Short( packet, 21 ) );
        }

        [Fact]
        public void WillRewriteEveryMatchingRecordInGridContainerContents()
        {
            AddEntry( 0x0021 );
            byte[] packet = ContainerContents( true, ( SOURCE, 5 ), ( 0x1234, 6 ), ( SOURCE, 7 ) );

            Check( ref packet );

            Assert.Equal( DESTINATION, Short( packet, 5 + 4 ) );
            Assert.Equal( 0x0021, Short( packet, 5 + 18 ) );
            Assert.Equal( 0x1234, Short( packet, 25 + 4 ) );
            Assert.Equal( 6, Short( packet, 25 + 18 ) );
            Assert.Equal( DESTINATION, Short( packet, 45 + 4 ) );
            Assert.Equal( 0x0021, Short( packet, 45 + 18 ) );

            // The serial of each record is untouched.
            Assert.Equal( 2, packet[25 + 3] );
            Assert.Equal( 3, packet[45 + 3] );
        }

        [Fact]
        public void WillUseNineteenByteRecordsForLargeOldStyleContainerContents()
        {
            AddEntry( 0x0021 );
            (int, int)[] items = Enumerable.Range( 0, 7 ).Select( i => ( i == 6 ? SOURCE : 0x1234, 1 ) ).ToArray();
            byte[] packet = ContainerContents( false, items );

            Check( ref packet );

            int last = 5 + 6 * 19;
            Assert.Equal( DESTINATION, Short( packet, last + 4 ) );
            Assert.Equal( 0x0021, Short( packet, last + 17 ) );
            Assert.Equal( 7, packet[last + 3] );
        }

        [Fact]
        public void WillUseNineteenByteRecordsForSmallOldStyleContainerContents()
        {
            AddEntry( 0x0021 );
            byte[] packet = ContainerContents( false, ( 0x1234, 1 ), ( SOURCE, 2 ) );

            Check( ref packet );

            Assert.Equal( DESTINATION, Short( packet, 24 + 4 ) );
            Assert.Equal( 0x0021, Short( packet, 24 + 17 ) );
            Assert.Equal( 1, Short( packet, 5 + 17 ) );
        }

        [Theory]
        [InlineData( 21, 19 )]
        [InlineData( 20, 18 )]
        public void WillRewriteAddItemToContainerAtTheRightHueOffset( int length, int hueOffset )
        {
            AddEntry( 0x0021 );
            byte[] packet = new byte[length];
            packet[0] = 0x25;
            PutShort( packet, 5, SOURCE );
            PutShort( packet, hueOffset, 0x0455 );

            Check( ref packet );

            Assert.Equal( DESTINATION, Short( packet, 5 ) );
            Assert.Equal( 0x0021, Short( packet, hueOffset ) );
        }

        [Fact]
        public void WillRewriteTheExistingHueOfAWorldItem1A()
        {
            AddEntry( 0x0021 );

            // 0x1A: serial with the amount bit, graphic, amount, x (light bit), y (hue + flags bits),
            // direction, z, hue, flags.
            byte[] packet = new byte[20];
            packet[0] = 0x1A;
            PutShort( packet, 1, packet.Length );
            packet[3] = 0xC0;
            PutShort( packet, 7, SOURCE );
            PutShort( packet, 9, 5 );
            PutShort( packet, 11, 0x8000 | 100 );
            PutShort( packet, 13, 0xC000 | 200 );
            packet[15] = 2;
            packet[16] = 10;
            PutShort( packet, 17, 0x0455 );
            packet[19] = 0x20;

            Check( ref packet );

            Assert.Equal( 20, packet.Length );
            Assert.Equal( DESTINATION, Short( packet, 7 ) );
            Assert.Equal( 0x0021, Short( packet, 17 ) );
            Assert.Equal( 0x20, packet[19] );
            Assert.Equal( 10, packet[16] );
        }

        [Fact]
        public void WillInsertAHueIntoAWorldItem1AThatHadNone()
        {
            AddEntry( 0x0021 );

            // No amount, no light, flags but no hue: serial, graphic, x, y (flags bit), z, flags.
            byte[] packet = new byte[15];
            packet[0] = 0x1A;
            PutShort( packet, 1, packet.Length );
            packet[3] = 0x40;
            PutShort( packet, 7, SOURCE );
            PutShort( packet, 9, 100 );
            PutShort( packet, 11, 0x4000 | 200 );
            packet[13] = 10;
            packet[14] = 0x20;

            Check( ref packet );

            Assert.Equal( 17, packet.Length );
            Assert.Equal( 0xC000 | 200, Short( packet, 11 ) );
            Assert.Equal( 10, packet[13] );
            Assert.Equal( 0x0021, Short( packet, 14 ) );
            Assert.Equal( 0x20, packet[16] );
        }

        [Fact]
        public void WillOnlyRewriteTheGraphicOfAWorldItem1AWithoutAHueInTheEntry()
        {
            AddEntry();

            byte[] packet = new byte[15];
            packet[0] = 0x1A;
            packet[3] = 0x40;
            PutShort( packet, 7, SOURCE );
            PutShort( packet, 11, 0x4000 | 200 );

            Check( ref packet );

            Assert.Equal( 15, packet.Length );
            Assert.Equal( DESTINATION, Short( packet, 7 ) );
        }

        [Fact]
        public void WaitRulesWillCoverEachEnabledSourceAndContainerContents()
        {
            Assert.Empty( _filter.GetWaitRules() );

            AddEntry();
            AddEntry( 0x21 );
            AddEntry( -1, true, 0x1234 );
            AddEntry( -1, false, 0x5555 );

            PacketWaitRule[] rules = _filter.GetWaitRules().ToArray();

            Assert.Equal( 7, rules.Length );
            Assert.Single( rules, r => r.PacketId == 0x3C && ( r.Conditions == null || r.Conditions.Length == 0 ) );
            Assert.Contains( rules, r => r.PacketId == 0xF3 && r.Conditions.Single().Position == 8 &&
                                         r.Conditions.Single().Bytes.SequenceEqual( new byte[] { 0x12, 0x34 } ) );
            Assert.DoesNotContain( rules, r => r.Conditions?.Any( c => c.Bytes.SequenceEqual( new byte[] { 0x55, 0x55 } ) ) ?? false );
        }

        [Fact]
        public void SerializeAndDeserializeWillRoundTripTheEntries()
        {
            AddEntry( 0x21 );
            AddEntry( -1, false, 0x1234, 0x4321 );

            JObject config = _filter.Serialize();

            ItemIDFilter copy = new ItemIDFilter();

            try
            {
                copy.Deserialize( config );
                copy.Deserialize( null );
                copy.Deserialize( new JObject() );

                Assert.Equal( 2, copy.Items.Count );
                Assert.Equal( 0x21, copy.Items[0].Hue );
                Assert.True( copy.Items[0].Enabled );
                Assert.Equal( 0x1234, copy.Items[1].SourceID );
                Assert.Equal( 0x4321, copy.Items[1].DestinationID );
                Assert.False( copy.Items[1].Enabled );
                Assert.Equal( -1, copy.Items[1].Hue );

                copy.ResetOptions();

                Assert.Empty( copy.Items );
            }
            finally
            {
                DynamicFilterEntry.Filters.Remove( copy );
            }
        }

        [Fact]
        public void DeserializeWillDefaultMissingFields()
        {
            JObject config = JObject.Parse( "{ \"Items\": [ { \"SourceID\": 3822 } ] }" );

            _filter.Deserialize( config );

            ItemIDFilterEntry entry = Assert.Single( _filter.Items );
            Assert.Equal( 3822, entry.SourceID );
            Assert.Equal( 0, entry.DestinationID );
            Assert.Equal( -1, entry.Hue );
            Assert.False( entry.Enabled );
        }
    }
}
