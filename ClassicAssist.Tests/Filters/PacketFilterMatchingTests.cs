using System.Collections.Generic;
using ClassicAssist.UO.Network.PacketFilter;
using Xunit;

namespace ClassicAssist.Tests.Filters
{
    /// <summary>
    ///     <see cref="PacketFilter" /> is the runtime filter list macros register with
    ///     <c>Engine.AddReceiveFilter</c> and friends: a match withholds the packet from the client and
    ///     runs the filter's action. These pin which packets a filter claims (id only, byte conditions,
    ///     negation, short packets), how filters are found again for removal, and the change notification
    ///     <see cref="ClassicAssist.UO.Network.PacketWaitRegistry" /> depends on. Built on a private
    ///     instance, so nothing global is touched.
    /// </summary>
    public class PacketFilterMatchingTests
    {
        private static readonly byte[] ContextMenuFor2 =
        {
            0xBF, 0x00, 0x0C, 0x00, 0x14, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02, 0x00
        };

        private static PacketFilterInfo ContextMenuFilter( int serial )
        {
            return new PacketFilterInfo( 0xBF,
                new[]
                {
                    PacketFilterConditions.ShortAtPositionCondition( 0x14, 3 ),
                    PacketFilterConditions.IntAtPositionCondition( serial, 7 )
                } );
        }

        [Fact]
        public void ConditionHelpersWillEncodeBigEndian()
        {
            Assert.Equal( new byte[] { 0x12, 0x34, 0x56, 0x78 }, PacketFilterConditions.IntAtPositionCondition( 0x12345678, 1 ).GetBytes() );
            Assert.Equal( new byte[] { 0xFF, 0xFF, 0xFF, 0xFE }, PacketFilterConditions.UIntAtPositionCondition( 0xFFFFFFFE, 1 ).GetBytes() );
            Assert.Equal( new byte[] { 0xAB, 0xCD }, PacketFilterConditions.ShortAtPositionCondition( 0xABCD, 1 ).GetBytes() );
            Assert.Equal( new byte[] { 0x7F }, PacketFilterConditions.ByteAtPositionCondition( 0x17F, 1 ).GetBytes() );
            Assert.Equal( 2, PacketFilterConditions.ShortAtPositionCondition( 1, 9 ).Length );
        }

        [Fact]
        public void AFilterWithoutConditionsWillClaimEveryPacketWithItsId()
        {
            PacketFilter filter = new PacketFilter();
            PacketFilterInfo pfi = new PacketFilterInfo( 0x95 );
            filter.Add( pfi );

            Assert.Equal( 1, filter.MatchFilterAll( new byte[] { 0x95, 1, 2 }, out PacketFilterInfo[] matched ) );
            Assert.Same( pfi, Assert.Single( matched ) );

            Assert.Equal( 0, filter.MatchFilterAll( new byte[] { 0x96, 1, 2 }, out matched ) );
            Assert.Empty( matched );

            Assert.True( filter.MatchFilter( new byte[] { 0x95 }, out PacketFilterInfo single ) );
            Assert.Same( pfi, single );
        }

        [Fact]
        public void AConditionWillOnlyClaimPacketsWithThoseBytes()
        {
            PacketFilter filter = new PacketFilter();
            filter.Add( new PacketFilterInfo( 0x1D, new[] { PacketFilterConditions.IntAtPositionCondition( 0x40000001, 1 ) } ) );

            Assert.Equal( 1, filter.MatchFilterAll( new byte[] { 0x1D, 0x40, 0x00, 0x00, 0x01 }, out _ ) );
            Assert.Equal( 0, filter.MatchFilterAll( new byte[] { 0x1D, 0x40, 0x00, 0x00, 0x02 }, out _ ) );
        }

        [Fact]
        public void ANegatedConditionWillClaimPacketsWithoutThoseBytes()
        {
            PacketFilter filter = new PacketFilter();
            filter.Add( new PacketFilterInfo( 0x6C, new[] { PacketFilterConditions.ByteAtPositionCondition( 3, 6, true ) } ) );

            Assert.Equal( 1, filter.MatchFilterAll( new byte[] { 0x6C, 0, 0, 0, 0, 0, 0x01 }, out _ ) );
            Assert.Equal( 0, filter.MatchFilterAll( new byte[] { 0x6C, 0, 0, 0, 0, 0, 0x03 }, out _ ) );
        }

        [Fact]
        public void AConditionPastTheEndOfThePacketWontClaimIt()
        {
            PacketFilter filter = new PacketFilter();
            filter.Add( new PacketFilterInfo( 0x1D, new[] { PacketFilterConditions.IntAtPositionCondition( 0x40000001, 1 ) } ) );

            Assert.Equal( 0, filter.MatchFilterAll( new byte[] { 0x1D, 0x40, 0x00 }, out _ ) );
            Assert.False( filter.MatchFilter( new byte[] { 0x1D, 0x40, 0x00 } ) );
        }

        [Fact]
        public void MatchFilterAllWillCollectEveryMatchingFilter()
        {
            PacketFilter filter = new PacketFilter();
            PacketFilterInfo first = new PacketFilterInfo( 0x95 );
            PacketFilterInfo second = new PacketFilterInfo( 0x95, new[] { PacketFilterConditions.ByteAtPositionCondition( 7, 1 ) } );
            PacketFilterInfo other = new PacketFilterInfo( 0x95, new[] { PacketFilterConditions.ByteAtPositionCondition( 8, 1 ) } );
            filter.Add( first );
            filter.Add( second );
            filter.Add( other );

            Assert.Equal( 2, filter.MatchFilterAll( new byte[] { 0x95, 7 }, out PacketFilterInfo[] matched ) );
            Assert.Equal( new[] { first, second }, matched );
        }

        [Fact]
        public void MatchFilterAllWillIgnoreANullPacket()
        {
            PacketFilter filter = new PacketFilter();
            filter.Add( new PacketFilterInfo( 0x95 ) );

            Assert.Equal( 0, filter.MatchFilterAll( null, out PacketFilterInfo[] matched ) );
            Assert.Null( matched );
        }

        [Fact]
        public void MatchFilterWillNeedEveryCondition()
        {
            PacketFilter filter = new PacketFilter();
            filter.Add( ContextMenuFilter( 2 ) );

            Assert.True( filter.MatchFilter( ContextMenuFor2 ) );

            byte[] forThree = (byte[]) ContextMenuFor2.Clone();
            forThree[10] = 3;

            Assert.False( filter.MatchFilter( forThree ) );
        }

        [Fact( Skip = "BUG: MatchFilterAll claims a packet when any one condition holds; WaitForContext relies on all" )]
        public void MatchFilterAllWillNeedEveryCondition()
        {
            PacketFilter filter = new PacketFilter();
            filter.Add( ContextMenuFilter( 2 ) );

            byte[] forThree = (byte[]) ContextMenuFor2.Clone();
            forThree[10] = 3;

            Assert.Equal( 0, filter.MatchFilterAll( forThree, out _ ) );
        }

        [Fact( Skip = "BUG: MatchFilterAll lists a filter once per matching condition, so its action runs repeatedly" )]
        public void MatchFilterAllWillListAFilterOnce()
        {
            PacketFilter filter = new PacketFilter();
            PacketFilterInfo pfi = ContextMenuFilter( 2 );
            filter.Add( pfi );

            Assert.Equal( 1, filter.MatchFilterAll( ContextMenuFor2, out PacketFilterInfo[] matched ) );
            Assert.Same( pfi, Assert.Single( matched ) );
        }

        [Fact]
        public void RemoveByValueWillFindAFilterBuiltFromEqualConditions()
        {
            PacketFilter filter = new PacketFilter();
            filter.Add( ContextMenuFilter( 2 ) );
            filter.Add( ContextMenuFilter( 3 ) );

            Assert.True( filter.Remove( 0xBF, ContextMenuFilter( 3 ).GetConditions() ) );
            Assert.Equal( 1, filter.Count() );
            Assert.False( filter.MatchFilter( new byte[] { 0xBF, 0, 0x0C, 0, 0x14, 0, 2, 0, 0, 0, 3, 0 } ) );
            Assert.True( filter.MatchFilter( ContextMenuFor2 ) );
        }

        [Fact]
        public void RemoveByValueWontRemoveWhenTheConditionsDiffer()
        {
            PacketFilter filter = new PacketFilter();
            filter.Add( ContextMenuFilter( 2 ) );
            filter.Add( new PacketFilterInfo( 0xBF ) );

            Assert.False( filter.Remove( 0xBF, new[] { PacketFilterConditions.ShortAtPositionCondition( 0x14, 3 ) } ) );
            Assert.False( filter.Remove( 0xBF, new[]
            {
                PacketFilterConditions.ShortAtPositionCondition( 0x14, 3 ),
                PacketFilterConditions.IntAtPositionCondition( 2, 7, true )
            } ) );
            Assert.False( filter.Remove( 0xBE, null ) );
            Assert.Equal( 2, filter.Count() );

            // Null constraints only match the condition-less filter.
            Assert.True( filter.Remove( 0xBF, null ) );
            Assert.Equal( 1, filter.Count() );
            Assert.True( filter.MatchFilter( ContextMenuFor2 ) );
        }

        [Fact]
        public void RemoveByInfoWillMatchOnIdAndConditionsNotTheAction()
        {
            PacketFilter filter = new PacketFilter();
            int calls = 0;
            filter.Add( new PacketFilterInfo( 0x95, ( _, _ ) => calls++ ) );

            Assert.True( filter.Contains( new PacketFilterInfo( 0x95 ) ) );

            filter.Remove( new PacketFilterInfo( 0x95 ) );

            Assert.Equal( 0, filter.Count() );
            Assert.Equal( 0, calls );
        }

        [Fact]
        public void ChangedWillFireForEveryRealChange()
        {
            PacketFilter filter = new PacketFilter();
            List<string> changes = new List<string>();
            filter.Changed += () => changes.Add( "changed" );

            PacketFilterInfo pfi = new PacketFilterInfo( 0x95 );
            filter.Add( pfi );
            filter.Remove( new PacketFilterInfo( 0x96 ) );
            filter.Remove( 0x96, null );
            filter.Remove( pfi );
            filter.Add( ContextMenuFilter( 2 ) );
            filter.Remove( 0xBF, ContextMenuFilter( 2 ).GetConditions() );
            filter.Clear();

            Assert.Equal( 5, changes.Count );
        }

        [Fact]
        public void GetFiltersWillReturnASnapshot()
        {
            PacketFilter filter = new PacketFilter();
            filter.Add( new PacketFilterInfo( 0x95 ) );

            PacketFilterInfo[] snapshot = filter.GetFilters();
            filter.Clear();

            Assert.Single( snapshot );
            Assert.Empty( filter.GetFilters() );
        }

        [Fact]
        public void FilterInfoEqualityWillCompareConditionsByValue()
        {
            Assert.Equal( ContextMenuFilter( 2 ), ContextMenuFilter( 2 ) );
            Assert.NotEqual( ContextMenuFilter( 2 ), ContextMenuFilter( 3 ) );
            Assert.NotEqual( new PacketFilterInfo( 0xBF ), ContextMenuFilter( 2 ) );
            Assert.NotEqual( ContextMenuFilter( 2 ), new PacketFilterInfo( 0xBF ) );
            Assert.False( ContextMenuFilter( 2 ).Equals( "not a filter" ) );
            Assert.NotEqual( PacketFilterConditions.ByteAtPositionCondition( 1, 2 ), PacketFilterConditions.ByteAtPositionCondition( 1, 2, true ) );
        }

        [Fact]
        public void FilterInfoToStringWillListItsConditions()
        {
            Assert.Contains( "No conditions.", new PacketFilterInfo( 0x95 ).ToString() );
            Assert.StartsWith( "PacketID: 0xBF", ContextMenuFilter( 2 ).ToString() );
        }
    }
}
