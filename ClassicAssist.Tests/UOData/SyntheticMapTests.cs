#region License

// Copyright (C) 2026 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClassicAssist.Shared;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests.UOData
{
    /// <summary>
    ///     Builds map0 / staidx0 / statics0 in a temp directory. Map 0 is 4096 tall, so block index is
    ///     <c>x / 8 * 512 + y / 8</c>: x below 8 stays in the first blocks, and x = 8 jumps to block 512.
    /// </summary>
    internal sealed class SyntheticMap
    {
        public const int BlockSize = 196;
        public const int BlocksPerColumn = 512;

        private readonly Dictionary<(int X, int Y), (ushort Id, sbyte Z)> _land = new Dictionary<(int, int), (ushort, sbyte)>();
        private readonly List<(int X, int Y, ushort Id, sbyte Z, ushort Hue)> _statics = new List<(int, int, ushort, sbyte, ushort)>();

        public SyntheticMap( int blocks )
        {
            Blocks = blocks;
        }

        public int Blocks { get; }

        public SyntheticMap Land( int x, int y, ushort id, sbyte z )
        {
            _land[( x, y )] = ( id, z );

            return this;
        }

        public SyntheticMap Static( int x, int y, ushort id, sbyte z, ushort hue = 0 )
        {
            _statics.Add( ( x, y, id, z, hue ) );

            return this;
        }

        public static int BlockIndex( int x, int y )
        {
            return x / 8 * BlocksPerColumn + y / 8;
        }

        /// <summary>
        ///     196 bytes per block: a header (written as the block index, which is also what orders the chunks
        ///     of a .uop) then 64 cells of ushort id and sbyte z, row major.
        /// </summary>
        public byte[] BuildMap()
        {
            byte[] map = new byte[Blocks * BlockSize];

            for ( int block = 0; block < Blocks; block++ )
            {
                BitConverter.GetBytes( block ).CopyTo( map, block * BlockSize );
            }

            foreach ( KeyValuePair<(int X, int Y), (ushort Id, sbyte Z)> pair in _land )
            {
                int offset = BlockIndex( pair.Key.X, pair.Key.Y ) * BlockSize + 4 + pair.Key.Y % 8 * 24 + pair.Key.X % 8 * 3;
                BitConverter.GetBytes( pair.Value.Id ).CopyTo( map, offset );
                map[offset + 2] = (byte) pair.Value.Z;
            }

            return map;
        }

        /// <summary>
        ///     The same map as a .uop, chunked on block boundaries into uneven pieces.
        /// </summary>
        public byte[] BuildUop( params int[] chunkBlocks )
        {
            byte[] map = BuildMap();
            List<UopFile> files = new List<UopFile>();
            int block = 0;

            foreach ( int count in chunkBlocks.Concat( new[] { Blocks } ) )
            {
                int take = Math.Min( count, Blocks - block );

                if ( take <= 0 )
                {
                    break;
                }

                files.Add( new UopFile( (ulong) ( 1000 + files.Count ),
                    map.Skip( block * BlockSize ).Take( take * BlockSize ).ToArray() ) );
                block += take;
            }

            // Table order is irrelevant to the reader; shuffle it so it cannot be relied on.
            files.Reverse();

            return UopWriter.Build( files, 2 );
        }

        /// <summary>
        ///     staidx: one (start, length, extra) per block, -1 start for none. statics: 7 byte records of
        ///     ushort id, byte x, byte y, sbyte z, ushort hue, in the order added.
        /// </summary>
        public void BuildStatics( out byte[] index, out byte[] data, IDictionary<int, (int Start, int Length)> overrides = null )
        {
            index = new byte[Blocks * 12];
            List<byte> records = new List<byte>();

            for ( int block = 0; block < Blocks; block++ )
            {
                int start = -1;
                int length = 0;

                List<(int X, int Y, ushort Id, sbyte Z, ushort Hue)> inBlock =
                    _statics.Where( s => BlockIndex( s.X, s.Y ) == block ).ToList();

                if ( inBlock.Count > 0 )
                {
                    start = records.Count;
                    length = inBlock.Count * 7;

                    foreach ( (int x, int y, ushort id, sbyte z, ushort hue) in inBlock )
                    {
                        records.AddRange( BitConverter.GetBytes( id ) );
                        records.Add( (byte) ( x % 8 ) );
                        records.Add( (byte) ( y % 8 ) );
                        records.Add( (byte) z );
                        records.AddRange( BitConverter.GetBytes( hue ) );
                    }
                }

                if ( overrides != null && overrides.TryGetValue( block, out (int Start, int Length) value ) )
                {
                    ( start, length ) = value;
                }

                BitConverter.GetBytes( start ).CopyTo( index, block * 12 );
                BitConverter.GetBytes( length ).CopyTo( index, block * 12 + 4 );
            }

            data = records.ToArray();
        }
    }

    /// <summary>
    ///     Restores the map and statics readers - and the world they read the player and items from - after
    ///     a test points them at a temp directory.
    /// </summary>
    public abstract class SyntheticMapTestBase : IDisposable
    {
        private readonly object _originalMapPath;
        private readonly object _originalStaticsPath;
        private readonly object[] _originalStaticData;

        protected SyntheticMapTestBase()
        {
            EngineState.Reset();

            _originalMapPath = DataReaderState.GetStatic( typeof( MapInfo ), "_dataPath" );
            _originalStaticsPath = DataReaderState.GetStatic( typeof( Statics ), "_dataPath" );
            _originalStaticData = ( (Array) DataReaderState.GetStatic( typeof( Statics ), "_staticData" ) ).Cast<object>().ToArray();

            Directory = new TempDirectory();

            TileDataBuilder tiles = new TileDataBuilder();
            ConfigureTiles( tiles );
            TileData = new TileDataScope( tiles );
        }

        internal TempDirectory Directory { get; }
        internal TileDataScope TileData { get; }

        private protected virtual void ConfigureTiles( TileDataBuilder tiles )
        {
        }

        public void Dispose()
        {
            // Re-initialising disposes the views on our files, which is also what lets the directory go.
            MapInfo.Initialize( (string) _originalMapPath );

            Array staticData = (Array) DataReaderState.GetStatic( typeof( Statics ), "_staticData" );

            for ( int i = 0; i < _originalStaticData.Length; i++ )
            {
                staticData.SetValue( _originalStaticData[i], i );
            }

            DataReaderState.SetStatic( typeof( Statics ), "_dataPath", _originalStaticsPath );

            TileData.Dispose();
            Directory.Dispose();
            EngineState.Reset();
            GC.SuppressFinalize( this );
        }

        internal void Install( SyntheticMap map, bool uop = false, params int[] chunkBlocks )
        {
            if ( uop )
            {
                Directory.Write( "map0LegacyMUL.uop", map.BuildUop( chunkBlocks ) );
            }
            else
            {
                Directory.Write( "map0.mul", map.BuildMap() );
            }

            map.BuildStatics( out byte[] index, out byte[] data );
            Directory.Write( "staidx0.mul", index );
            Directory.Write( "statics0.mul", data );

            MapInfo.Initialize( Directory.Path );
            Statics.Initialize( Directory.Path );
        }
    }

    /// <summary>
    ///     Land lookups against a synthetic map0, as .mul and as .uop. The cell offset arithmetic
    ///     (<c>block * 196 + 4 + y % 8 * 24 + x % 8 * 3</c>) is easy to get subtly wrong - swapping x and y, or
    ///     dropping the header - and the result is a plausible tile from the wrong place, so the tests read
    ///     cells either side of block edges, where each mistake lands on a different, recognisable value.
    /// </summary>
    public class SyntheticMapLandTests : SyntheticMapTestBase
    {
        private static SyntheticMap BoundaryMap()
        {
            return new SyntheticMap( 514 )
                .Land( 0, 0, 0x10, 1 )
                .Land( 7, 0, 0x11, 2 )
                .Land( 0, 7, 0x12, 3 )
                .Land( 7, 7, 0x13, -4 )
                .Land( 0, 8, 0x14, 5 )
                .Land( 3, 5, 0x15, 6 )
                .Land( 5, 3, 0x16, 7 )
                .Land( 8, 0, 0x17, 8 )
                .Land( 8, 8, 0x18, -128 );
        }

        [Theory]
        [InlineData( false )]
        [InlineData( true )]
        public void WillReadCellsEitherSideOfBlockBoundaries( bool uop )
        {
            Install( BoundaryMap(), uop, 200, 1, 313 );

            (int X, int Y, int Id, int Z)[] expected =
            {
                ( 0, 0, 0x10, 1 ), ( 7, 0, 0x11, 2 ), ( 0, 7, 0x12, 3 ), ( 7, 7, 0x13, -4 ), ( 0, 8, 0x14, 5 ),
                ( 3, 5, 0x15, 6 ), ( 5, 3, 0x16, 7 ), ( 8, 0, 0x17, 8 ), ( 8, 8, 0x18, -128 ), ( 1, 1, 0, 0 )
            };

            foreach ( (int x, int y, int id, int z) in expected )
            {
                LandTile tile = MapInfo.GetLandTile( 0, x, y );

                Assert.Equal( id, tile.ID );
                Assert.Equal( z, tile.Z );
                Assert.Equal( x, tile.X );
                Assert.Equal( y, tile.Y );
            }
        }

        /// <summary>
        ///     The tile's flags and name come from tiledata, looked up by the id read from the map.
        /// </summary>
        [Fact]
        public void WillTakeFlagsFromTileData()
        {
            Install( new SyntheticMap( 1 ).Land( 2, 2, 0xA8, 0 ) );

            LandTile tile = MapInfo.GetLandTile( 0, 2, 2 );

            Assert.Equal( "water", tile.Name );
            Assert.True( tile.Flags.HasFlag( TileFlags.Impassable ) );
        }

        private protected override void ConfigureTiles( TileDataBuilder tiles )
        {
            tiles.Land[0xA8] = new LandTileSpec { Flags = (long) ( TileFlags.Impassable | TileFlags.Wet ), Name = "water" };
        }

        [Theory]
        [InlineData( false )]
        [InlineData( true )]
        public void WillReturnAnEmptyTilePastTheEndOfTheFile( bool uop )
        {
            Install( new SyntheticMap( 2 ).Land( 0, 15, 0x20, 9 ), uop, 1 );

            Assert.Equal( 0x20, MapInfo.GetLandTile( 0, 0, 15 ).ID );

            LandTile outside = MapInfo.GetLandTile( 0, 0, 16 );

            Assert.Equal( 0, outside.ID );
            Assert.Equal( 0, outside.Z );
            Assert.Equal( 0, MapInfo.GetLandTile( 0, 8, 0 ).ID );
        }

        [Fact]
        public void WillReturnAnEmptyTileForAMapWithNoFile()
        {
            Install( new SyntheticMap( 1 ).Land( 1, 1, 0x30, 1 ) );

            Assert.Equal( 0, MapInfo.GetLandTile( 1, 1, 1 ).ID );
            Assert.Equal( 0, MapInfo.GetLandTile( 6, 1, 1 ).ID );
            Assert.Equal( 0, MapInfo.GetLandTile( -1, 1, 1 ).ID );
        }

        /// <summary>
        ///     When both exist the .uop is the current data and wins over a stale .mul left beside it.
        /// </summary>
        [Fact]
        public void WillPreferTheUopOverTheMul()
        {
            Directory.Write( "map0.mul", new SyntheticMap( 1 ).Land( 1, 1, 0x40, 1 ).BuildMap() );
            Directory.Write( "map0LegacyMUL.uop", new SyntheticMap( 1 ).Land( 1, 1, 0x41, 2 ).BuildUop() );

            MapInfo.Initialize( Directory.Path );

            Assert.Equal( 0x41, MapInfo.GetLandTile( 0, 1, 1 ).ID );
        }

        /// <summary>
        ///     Initialize against another directory must read the new files and let go of the old ones - a view
        ///     left open would keep the first install's map locked for the life of the process.
        /// </summary>
        [Fact]
        public void WillReleaseTheOldFilesWhenInitializedAgain()
        {
            Install( new SyntheticMap( 1 ).Land( 1, 1, 0x50, 1 ) );
            Assert.Equal( 0x50, MapInfo.GetLandTile( 0, 1, 1 ).ID );

            using TempDirectory second = new TempDirectory();
            second.Write( "map0.mul", new SyntheticMap( 1 ).Land( 1, 1, 0x51, 1 ).BuildMap() );

            MapInfo.Initialize( second.Path );

            Assert.Equal( 0x51, MapInfo.GetLandTile( 0, 1, 1 ).ID );

            File.Delete( Directory.File( "map0.mul" ) );
            Assert.False( File.Exists( Directory.File( "map0.mul" ) ) );

            // Let go of the second directory too, before it is deleted.
            MapInfo.Initialize( Directory.Path );
        }

        /// <summary>
        ///     The corners are (x, y), (x, y + 1), (x + 1, y) and (x + 1, y + 1). The average is taken across
        ///     whichever diagonal is flatter, and rounds down (towards negative infinity, not zero).
        /// </summary>
        [Fact]
        public void WillAverageAcrossTheFlatterDiagonal()
        {
            SyntheticMap map = new SyntheticMap( 1 )
                .Land( 1, 1, 3, 10 ).Land( 1, 2, 3, 0 ).Land( 2, 1, 3, 1 ).Land( 2, 2, 3, 20 )
                .Land( 4, 4, 3, -3 ).Land( 4, 5, 3, 0 ).Land( 5, 4, 3, 5 ).Land( 5, 5, 3, -4 );

            Install( map );

            MapInfo.GetAverageZ( 0, 1, 1, out int lowest, out int average, out int highest );

            // top-bottom spans 10, left-right only 1: average left and right, (0 + 1) / 2 rounded down.
            Assert.Equal( 0, average );
            Assert.Equal( 0, lowest );
            Assert.Equal( 20, highest );

            // Here top-bottom is the flatter diagonal: -3 and -4 average to -3.5, which floors to -4.
            Assert.Equal( -4, MapInfo.GetAverageZ( 0, 4, 4 ) );
        }
    }

    /// <summary>
    ///     Statics are regrouped per cell at load time so a lookup can slice its cell out of the block. The
    ///     regrouping is a counting sort, so the things worth pinning are: only the requested cell's records
    ///     come back, in file order, with z sign-extended and hue little-endian; an empty block is not the same
    ///     as a missing one; and a block pointing past the end of the data file is dropped without taking the
    ///     rest of the map down with it.
    /// </summary>
    public class SyntheticStaticsTests : SyntheticMapTestBase
    {
        private protected override void ConfigureTiles( TileDataBuilder tiles )
        {
            tiles.Statics[0x0B40] = new StaticTileSpec { Name = "table", Height = 6, Flags = (long) TileFlags.Surface };
            tiles.Statics[0x0B41] = new StaticTileSpec { Name = "candle", Height = 2 };
            tiles.Statics[0x0B42] = new StaticTileSpec { Name = "chair", Height = 4 };
        }

        [Fact]
        public void WillReturnOnlyTheRequestedCellInFileOrder()
        {
            SyntheticMap map = new SyntheticMap( 514 )
                .Static( 3, 3, 0x0B40, 0, 0x0100 )
                .Static( 4, 3, 0x0B42, 1 )
                .Static( 3, 3, 0x0B41, 6, 0x0021 )
                .Static( 3, 4, 0x0B42, 2 )
                .Static( 3, 3, 0x0B42, -5 )
                .Static( 8, 8, 0x0B41, 10 );

            Install( map );

            StaticTile[] tiles = Statics.GetStatics( 0, 3, 3 );

            Assert.Equal( new[] { "table", "candle", "chair" }, tiles.Select( t => t.Name ).ToArray() );
            Assert.Equal( new[] { 0x0B40, 0x0B41, 0x0B42 }, tiles.Select( t => (int) t.ID ).ToArray() );
            Assert.Equal( new[] { 0, 6, -5 }, tiles.Select( t => t.Z ).ToArray() );
            Assert.Equal( new[] { 0x0100, 0x0021, 0 }, tiles.Select( t => t.Hue ).ToArray() );
            Assert.All( tiles, t => Assert.Equal( ( 3, 3 ), ( t.X, t.Y ) ) );
            Assert.Equal( 6, tiles[0].Height );

            Assert.Equal( "chair", Assert.Single( Statics.GetStatics( 0, 4, 3 ) ).Name );
            Assert.Equal( "chair", Assert.Single( Statics.GetStatics( 0, 3, 4 ) ).Name );

            StaticTile far = Assert.Single( Statics.GetStatics( 0, 8, 8 ) );
            Assert.Equal( 10, far.Z );
            Assert.Equal( 8, far.X );
        }

        [Fact]
        public void WillAppendToAListInTheSameOrder()
        {
            Install( new SyntheticMap( 1 ).Static( 1, 1, 0x0B40, 0 ).Static( 1, 1, 0x0B41, 6 ).Static( 2, 1, 0x0B42, 0 ) );

            List<StaticTile> results = new List<StaticTile> { new StaticTile { Name = "already here" } };

            Statics.GetStatics( 0, 1, 1, results );
            Statics.GetStatics( 0, 5, 5, results );
            Statics.GetStatics( 0, 2, 1, results );

            Assert.Equal( new[] { "already here", "table", "candle", "chair" }, results.Select( t => t.Name ).ToArray() );
        }

        /// <summary>
        ///     A block that exists but holds nothing returns an empty array; a block with no entry (start -1),
        ///     or past the end of the index, returns null. Callers check for both.
        /// </summary>
        [Fact]
        public void WillDistinguishAnEmptyBlockFromAMissingOne()
        {
            SyntheticMap map = new SyntheticMap( 3 ).Static( 0, 0, 0x0B40, 0 );

            map.BuildStatics( out byte[] index, out byte[] data,
                new Dictionary<int, (int, int)> { { 1, ( 0, 0 ) } } );

            Directory.Write( "map0.mul", map.BuildMap() );
            Directory.Write( "staidx0.mul", index );
            Directory.Write( "statics0.mul", data );
            Statics.Initialize( Directory.Path );

            Assert.Single( Statics.GetStatics( 0, 0, 0 ) );
            Assert.Empty( Statics.GetStatics( 0, 1, 1 ) );
            Assert.Empty( Statics.GetStatics( 0, 0, 9 ) );
            Assert.Null( Statics.GetStatics( 0, 0, 17 ) );
            Assert.Null( Statics.GetStatics( 0, 0, 25 ) );
            Assert.Null( Statics.GetStatics( 0, 8, 0 ) );
        }

        [Fact]
        public void WillSkipABlockThatRunsPastTheEndOfTheData()
        {
            SyntheticMap map = new SyntheticMap( 3 ).Static( 0, 0, 0x0B40, 0 ).Static( 0, 16, 0x0B41, 1 );

            map.BuildStatics( out byte[] index, out byte[] data,
                new Dictionary<int, (int, int)> { { 1, ( 7, 7 * 50 ) } } );

            Directory.Write( "staidx0.mul", index );
            Directory.Write( "statics0.mul", data );
            Statics.Initialize( Directory.Path );

            Assert.Null( Statics.GetStatics( 0, 0, 8 ) );
            Assert.Equal( "table", Assert.Single( Statics.GetStatics( 0, 0, 0 ) ).Name );
            Assert.Equal( "candle", Assert.Single( Statics.GetStatics( 0, 0, 16 ) ).Name );
        }

        [Fact]
        public void WillReturnNullWhenTheFilesAreMissing()
        {
            Statics.Initialize( Directory.Path );

            Assert.Null( Statics.GetStatics( 0, 0, 0 ) );

            Directory.Write( "staidx0.mul", new byte[12] );
            Statics.Initialize( Directory.Path );

            Assert.Null( Statics.GetStatics( 0, 0, 0 ) );
        }
    }

    /// <summary>
    ///     <see cref="MapInfo.ItemCanFit(int,int,int,int,int,int,int,out int)" /> decides where the assistant drops
    ///     things on the ground (the ModernUO DropToWorld algorithm). Getting it wrong either throws items at a
    ///     tile the server refuses or picks a z the server would place differently, so each rule gets its own
    ///     case: reach, the surface it lands on (land, table, bridge), what blocks it (walls, mobiles,
    ///     impassable land), and the stack-height limit the comment in the product calls out.
    /// </summary>
    public class ItemCanFitTests : SyntheticMapTestBase
    {
        private const ushort Grass = 3;
        private const ushort Water = 0xA8;
        private const int Coin = 0x0EED;
        private const int Crate = 0x0E3C;
        private const int TallCrate = 0x0E3D;
        private const int Table = 0x0B40;
        private const int Wall = 0x0080;
        private const int Bridge = 0x0B50;

        private protected override void ConfigureTiles( TileDataBuilder tiles )
        {
            tiles.Land[Water] = new LandTileSpec { Flags = (long) ( TileFlags.Impassable | TileFlags.Wet ), Name = "water" };
            tiles.Statics[Coin] = new StaticTileSpec { Name = "coin" };
            tiles.Statics[Crate] = new StaticTileSpec { Name = "crate", Height = 16 };
            tiles.Statics[TallCrate] = new StaticTileSpec { Name = "tall crate", Height = 17 };
            tiles.Statics[Table] = new StaticTileSpec { Name = "table", Height = 6, Flags = (long) TileFlags.Surface };
            tiles.Statics[Wall] = new StaticTileSpec { Name = "wall", Height = 20, Flags = (long) TileFlags.Impassable };
            tiles.Statics[Bridge] = new StaticTileSpec
            {
                Name = "bridge", Height = 10, Flags = (long) ( TileFlags.Surface | TileFlags.Bridge )
            };
        }

        private void Flat( SyntheticMap map, ushort land = Grass )
        {
            for ( int x = 0; x < 8; x++ )
            {
                for ( int y = 0; y < 8; y++ )
                {
                    map.Land( x, y, land, 0 );
                }
            }

            Install( map );
        }

        [Fact]
        public void WillDropOntoFlatGround()
        {
            Flat( new SyntheticMap( 1 ) );

            Assert.True( MapInfo.ItemCanFit( 0, 2, 2, Coin, 2, 2, 0, out int z ) );
            Assert.Equal( 0, z );
        }

        [Fact]
        public void WillNotDropOutOfReachOrOffTheMap()
        {
            Flat( new SyntheticMap( 1 ) );

            Assert.True( MapInfo.ItemCanFit( 0, 4, 4, Coin, 2, 2, 0, out _ ) );
            Assert.False( MapInfo.ItemCanFit( 0, 5, 2, Coin, 2, 2, 0, out _ ) );
            Assert.False( MapInfo.ItemCanFit( 0, -1, 0, Coin, 0, 0, 0, out _ ) );
        }

        [Fact]
        public void WillDropOnTopOfASurface()
        {
            Flat( new SyntheticMap( 1 ).Static( 2, 2, Table, 0 ) );

            Assert.True( MapInfo.ItemCanFit( 0, 2, 2, Coin, 2, 2, 0, out int z ) );
            Assert.Equal( 6, z );
        }

        /// <summary>
        ///     A bridge counts as half its height.
        /// </summary>
        [Fact]
        public void WillDropOnHalfABridgesHeight()
        {
            Flat( new SyntheticMap( 1 ).Static( 2, 2, Bridge, 0 ) );

            Assert.True( MapInfo.ItemCanFit( 0, 2, 2, Coin, 2, 2, 0, out int z ) );
            Assert.Equal( 5, z );
        }

        [Fact]
        public void WillNotDropIntoAWall()
        {
            Flat( new SyntheticMap( 1 ).Static( 2, 2, Wall, 0 ) );

            Assert.False( MapInfo.ItemCanFit( 0, 2, 2, Coin, 2, 2, 0, out _ ) );
        }

        [Fact]
        public void WillNotDropOntoImpassableLand()
        {
            Flat( new SyntheticMap( 1 ), Water );

            Assert.False( MapInfo.ItemCanFit( 0, 2, 2, Coin, 2, 2, 0, out _ ) );
        }

        [Fact]
        public void WillNotDropOntoAMobileButWillAboveIt()
        {
            Flat( new SyntheticMap( 1 ).Static( 3, 3, Table, 0 ) );

            Engine.Mobiles.Add( new Mobile( 0x100 ) { X = 2, Y = 2, Z = 0 } );

            Assert.False( MapInfo.ItemCanFit( 0, 2, 2, Coin, 2, 2, 0, out _ ) );

            // On the table next door the mobile is not in the way.
            Engine.Mobiles.Add( new Mobile( 0x101 ) { X = 3, Y = 3, Z = -20 } );

            Assert.True( MapInfo.ItemCanFit( 0, 3, 3, Coin, 2, 2, 0, out int z ) );
            Assert.Equal( 6, z );
        }

        /// <summary>
        ///     Items already on the tile stack upwards. A gap that opens only above reach - z 17 over a stack
        ///     that tops out at 16, with a reach of 16 - is refused by the server, so must be refused here.
        /// </summary>
        [Fact]
        public void WillStackOnItemsOnlyWithinReach()
        {
            Flat( new SyntheticMap( 1 ) );

            Engine.Items.Add( new Item( 0x40000100 ) { ID = Crate, X = 2, Y = 2, Z = 0 } );

            Assert.True( MapInfo.ItemCanFit( 0, 2, 2, Coin, 2, 2, 0, out int z ) );
            Assert.Equal( 16, z );

            Engine.Items.Add( new Item( 0x40000101 ) { ID = TallCrate, X = 3, Y = 3, Z = 0 } );

            Assert.False( MapInfo.ItemCanFit( 0, 3, 3, Coin, 2, 2, 0, out _ ) );

            // Standing higher brings the same gap into reach.
            Assert.True( MapInfo.ItemCanFit( 0, 3, 3, Coin, 2, 2, 5, out z ) );
            Assert.Equal( 17, z );
        }

        [Fact]
        public void WillNotFitWithoutAPlayer()
        {
            Flat( new SyntheticMap( 1 ) );

            Assert.False( MapInfo.ItemCanFit( 0, 2, 2, Coin, out _ ) );

            EngineState.ResetWithPlayer().X = 2;

            Assert.True( MapInfo.ItemCanFit( 0, 2, 2, Coin, out _ ) );
        }
    }
}
