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

using System.Collections.Generic;
using System.IO;
using ClassicAssist.UO.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Covers the map and statics readers, which hold their files open as memory mapped views and
    ///     pre-group statics by cell rather than re-opening and re-scanning per lookup.
    /// </summary>
    [TestClass]
    public class MapDataTests
    {
        private const int MAP = 0;
        private const int X = 1400;
        private const int Y = 1600;

        private static bool Ready()
        {
            if ( !TestData.HasUOData )
            {
                return false;
            }

            TileData.Initialize( TestData.UOPath );
            Statics.Initialize( TestData.UOPath );
            MapInfo.Initialize( TestData.UOPath );

            return true;
        }

        [TestMethod]
        public void WillReadTheSameLandTileEveryTime()
        {
            if ( !Ready() )
            {
                return;
            }

            LandTile first = MapInfo.GetLandTile( MAP, X, Y );
            LandTile second = MapInfo.GetLandTile( MAP, X, Y );

            Assert.AreEqual( first.ID, second.ID );
            Assert.AreEqual( first.Z, second.Z );
            Assert.AreEqual( first.Flags, second.Flags );
            Assert.AreEqual( X, first.X );
            Assert.AreEqual( Y, first.Y );
        }

        /// <summary>
        ///     Initialize used to be able to run again against a different install; the view has to be
        ///     rebuilt with it rather than pinned to whichever path was read first.
        /// </summary>
        [TestMethod]
        public void WillRereadAfterInitialize()
        {
            if ( !Ready() )
            {
                return;
            }

            LandTile first = MapInfo.GetLandTile( MAP, X, Y );

            MapInfo.Initialize( TestData.UOPath );

            Assert.AreEqual( first.ID, MapInfo.GetLandTile( MAP, X, Y ).ID );
        }

        /// <summary>
        ///     Reads past the end of the map return an empty tile. The previous reader seeked past the end
        ///     of the file and let the resulting EndOfStreamException out.
        /// </summary>
        [TestMethod]
        public void WillReturnEmptyTileOutsideTheMap()
        {
            if ( !Ready() )
            {
                return;
            }

            LandTile tile = MapInfo.GetLandTile( MAP, 99999, 99999 );

            Assert.AreEqual( 0, tile.ID );
            Assert.AreEqual( 0, tile.Z );
        }

        [TestMethod]
        public void WillReturnEmptyTileWithNoDataPath()
        {
            MapInfo.Initialize( "/nonexistent-uo-path" );

            Assert.AreEqual( 0, MapInfo.GetLandTile( MAP, X, Y ).ID );

            // Leave the shared statics pointing back at real data for whatever runs next.
            if ( TestData.HasUOData )
            {
                MapInfo.Initialize( TestData.UOPath );
            }
        }

        /// <summary>
        ///     Statics are stored grouped by cell so a lookup can slice its cell out of the block. If the
        ///     grouping or the range were wrong the block's other cells would leak into the result.
        /// </summary>
        [TestMethod]
        public void WillOnlyReturnStaticsForTheRequestedCell()
        {
            if ( !Ready() )
            {
                return;
            }

            int found = 0;

            // Whole block, so every cell of it gets checked including the ones that share a block with a
            // populated neighbour.
            for ( int x = X; x < X + 8; x++ )
            {
                for ( int y = Y; y < Y + 8; y++ )
                {
                    StaticTile[] tiles = Statics.GetStatics( MAP, x, y );

                    if ( tiles == null )
                    {
                        continue;
                    }

                    found += tiles.Length;

                    foreach ( StaticTile tile in tiles )
                    {
                        Assert.AreEqual( x, tile.X );
                        Assert.AreEqual( y, tile.Y );
                    }
                }
            }

            Assert.IsTrue( found > 0, "expected at least one static in a populated block" );
        }

        [TestMethod]
        public void WillAppendTheSameStaticsToAList()
        {
            if ( !Ready() )
            {
                return;
            }

            List<StaticTile> results = [];

            for ( int x = X; x < X + 8; x++ )
            {
                for ( int y = Y; y < Y + 8; y++ )
                {
                    StaticTile[] tiles = Statics.GetStatics( MAP, x, y ) ?? [];
                    int before = results.Count;

                    Statics.GetStatics( MAP, x, y, results );

                    Assert.AreEqual( tiles.Length, results.Count - before );

                    for ( int i = 0; i < tiles.Length; i++ )
                    {
                        Assert.AreEqual( tiles[i].ID, results[before + i].ID );
                        Assert.AreEqual( tiles[i].Z, results[before + i].Z );
                        Assert.AreEqual( tiles[i].Hue, results[before + i].Hue );
                    }
                }
            }
        }

        /// <summary>
        ///     Off the end of the block table. This used to index straight into the array and throw.
        /// </summary>
        [TestMethod]
        public void WillReturnNullForStaticsOutsideTheMap()
        {
            if ( !Ready() )
            {
                return;
            }

            Assert.IsNull( Statics.GetStatics( MAP, 99999, 99999 ) );
        }

        /// <summary>
        ///     Lookup translates an offset in the flattened file to one in the container. Reading through it
        ///     has to agree with the flattened bytes ReadAll produces, since the map reader uses the first
        ///     and this asserts against the second.
        /// </summary>
        [TestMethod]
        public void WillLookUpTheSameBytesReadAllProduces()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            string fileName = Path.Combine( TestData.UOPath, "map0LegacyMUL.uop" );

            if ( !File.Exists( fileName ) )
            {
                return;
            }

            byte[] flattened;
            byte[] raw = File.ReadAllBytes( fileName );

            using ( FileStream stream = File.OpenRead( fileName ) )
            {
                using UOPIndex index = new( stream );
                flattened = index.ReadAll();

                Assert.IsTrue( flattened.Length > 0 );

                for ( int offset = 0; offset < flattened.Length; offset += 999983 )
                {
                    int translated = index.Lookup( offset );

                    Assert.IsTrue( translated >= 0 && translated < raw.Length,
                        $"offset {offset} translated outside the file" );

                    Assert.AreEqual( flattened[offset], raw[translated], $"mismatch at offset {offset}" );
                }
            }

            // A map block is 196 bytes, so the flattened file has to be a whole number of them.
            Assert.AreEqual( 0, flattened.Length % 196 );
        }
    }
}
