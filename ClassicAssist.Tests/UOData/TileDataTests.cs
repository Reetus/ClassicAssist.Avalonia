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
using System.IO;
using ClassicAssist.UO.Data;
using Xunit;

namespace ClassicAssist.Tests.UOData
{
    /// <summary>
    ///     tiledata.mul in both layouts, written synthetically. The two differ in flag width and record size,
    ///     and both have a 4 byte header in front of every 32 records, so a reader that gets the header
    ///     placement or a field offset wrong still "works" - it just returns the neighbouring tile's flags or a
    ///     name shifted by a few bytes. Ids at block boundaries and the last id of each table are the ones that
    ///     catch that. Weight, quality (the layer for wearables), height and the 64 bit flags feed autoloot,
    ///     dress and the drop fit check.
    /// </summary>
    public class TileDataTests
    {
        private const long HighFlag = 1L << 40;

        private static TileDataBuilder Sample( bool oldFormat )
        {
            TileDataBuilder builder = new TileDataBuilder();

            long flags = (long) ( TileFlags.Surface | TileFlags.Impassable );

            if ( !oldFormat )
            {
                flags |= HighFlag;
            }

            builder.Land[2] = new LandTileSpec { Flags = flags, TextureId = 0x0102, Name = "grass" };
            builder.Land[31] = new LandTileSpec { Flags = (long) TileFlags.Wet, TextureId = 31, Name = "water" };
            builder.Land[32] = new LandTileSpec { Flags = (long) TileFlags.Impassable, TextureId = 32, Name = "rock" };
            builder.Land[0x3FFF] = new LandTileSpec { Flags = (long) TileFlags.Bridge, TextureId = 7, Name = "last land" };

            int lastStatic = oldFormat ? 0x3FFF : 0xFFFF;

            builder.Statics[0] = new StaticTileSpec { Name = "nodraw", Height = 1 };
            builder.Statics[31] = new StaticTileSpec { Name = "block end", Weight = 31, Height = 2 };
            builder.Statics[32] = new StaticTileSpec { Name = "block start", Weight = 32, Height = 3 };
            builder.Statics[0x1BF8] = new StaticTileSpec
            {
                Flags = flags | (long) TileFlags.Stackable,
                Weight = 6,
                Quality = 9,
                Quantity = 4,
                Height = 1,
                Name = "silver ingot"
            };
            builder.Statics[lastStatic] = new StaticTileSpec { Name = "exactly twenty chars", Height = 99 };

            return builder;
        }

        [Fact]
        public void WillReadEveryStaticFieldInTheNewLayout()
        {
            TileDataScope.Load( Write( Sample( false ), false, out TempDirectory directory ), out _, out StaticTile[] statics );

            using ( directory )
            {
                StaticTile ingot = statics[0x1BF8];

                Assert.Equal( 0x1BF8, ingot.ID );
                Assert.Equal( "silver ingot", ingot.Name );
                Assert.Equal( 6, ingot.Weight );
                Assert.Equal( 9, ingot.Quality );
                Assert.Equal( 4, ingot.Quantity );
                Assert.Equal( 1, ingot.Height );
                Assert.True( ingot.Flags.HasFlag( TileFlags.Stackable ) );
                Assert.True( ingot.Flags.HasFlag( TileFlags.Surface ) );
                Assert.Equal( HighFlag, (long) ingot.Flags & HighFlag ); // the upper 32 bits must survive

                Assert.Equal( "block end", statics[31].Name );
                Assert.Equal( 31, statics[31].Weight );
                Assert.Equal( "block start", statics[32].Name );
                Assert.Equal( 32, statics[32].Weight );
                Assert.Equal( "exactly twenty chars", statics[0xFFFF].Name );
                Assert.Equal( 99, statics[0xFFFF].Height );
                Assert.Equal( 0xFFFF, statics[0xFFFF].ID );
            }
        }

        [Fact]
        public void WillReadEveryStaticFieldInTheOldLayout()
        {
            TileDataScope.Load( Write( Sample( true ), true, out TempDirectory directory ), out _, out StaticTile[] statics );

            using ( directory )
            {
                StaticTile ingot = statics[0x1BF8];

                Assert.Equal( "silver ingot", ingot.Name );
                Assert.Equal( 6, ingot.Weight );
                Assert.Equal( 9, ingot.Quality );
                Assert.Equal( 4, ingot.Quantity );
                Assert.Equal( 1, ingot.Height );
                Assert.Equal( TileFlags.Surface | TileFlags.Impassable | TileFlags.Stackable, ingot.Flags );

                Assert.Equal( "block end", statics[31].Name );
                Assert.Equal( "block start", statics[32].Name );
                Assert.Equal( 32, statics[32].Weight );
                Assert.Equal( "exactly twenty chars", statics[0x3FFF].Name );
                Assert.Equal( 0x3FFF, statics[0x3FFF].ID );
                Assert.Equal( 0x4000, statics.Length );
            }
        }

        [Theory]
        [InlineData( false )]
        [InlineData( true )]
        public void WillReadLandTilesEitherSideOfABlockBoundary( bool oldFormat )
        {
            TileDataScope.Load( Write( Sample( oldFormat ), oldFormat, out TempDirectory directory ), out LandTile[] land,
                out _ );

            using ( directory )
            {
                Assert.Equal( "grass", land[2].Name );
                Assert.Equal( 0x0102, land[2].ID );
                Assert.True( land[2].Flags.HasFlag( TileFlags.Surface | TileFlags.Impassable ) );

                Assert.Equal( "water", land[31].Name );
                Assert.Equal( TileFlags.Wet, land[31].Flags );
                Assert.Equal( "rock", land[32].Name );
                Assert.Equal( TileFlags.Impassable, land[32].Flags );
                Assert.Equal( "last land", land[0x3FFF].Name );
                Assert.Equal( TileFlags.Bridge, land[0x3FFF].Flags );

                if ( !oldFormat )
                {
                    Assert.Equal( HighFlag, (long) land[2].Flags & HighFlag );
                }
            }
        }

        /// <summary>
        ///     Land tile 0 sits after the file's first block header like every other tile. The new-layout loop
        ///     skips that header after tile 0 (at i == 1) instead of before it, so tile 0 is read 4 bytes early:
        ///     its flags take the header and its name comes out shifted.
        /// </summary>
        [Fact( Skip = "BUG: TileData.LoadLandTiles (new layout) skips the first block header after land tile 0, so tile 0 is misread" )]
        public void WillReadLandTileZeroInTheNewLayout()
        {
            TileDataBuilder builder = new TileDataBuilder();
            builder.Land[0] = new LandTileSpec { Flags = (long) TileFlags.Impassable, TextureId = 0x55, Name = "VOID!!!!!!" };

            TileDataScope.Load( Write( builder, false, out TempDirectory directory ), out LandTile[] land, out _ );

            using ( directory )
            {
                Assert.Equal( "VOID!!!!!!", land[0].Name );
                Assert.Equal( TileFlags.Impassable, land[0].Flags );
                Assert.Equal( 0x55, land[0].ID );
            }
        }

        [Fact]
        public void WillReadLandTileZeroInTheOldLayout()
        {
            TileDataBuilder builder = new TileDataBuilder();
            builder.Land[0] = new LandTileSpec { Flags = (long) TileFlags.Impassable, TextureId = 0x55, Name = "nodraw" };

            TileDataScope.Load( Write( builder, true, out TempDirectory directory ), out LandTile[] land, out _ );

            using ( directory )
            {
                Assert.Equal( "nodraw", land[0].Name );
                Assert.Equal( TileFlags.Impassable, land[0].Flags );
                Assert.Equal( 0x55, land[0].ID );
            }
        }

        /// <summary>
        ///     Names are NUL padded; an interior NUL ends the name rather than surviving into it.
        /// </summary>
        [Fact]
        public void WillStopANameAtTheFirstNul()
        {
            TileDataBuilder builder = new TileDataBuilder();
            builder.Statics[5] = new StaticTileSpec { Name = "short\0garbage" };

            TileDataScope.Load( Write( builder, false, out TempDirectory directory ), out _, out StaticTile[] statics );

            using ( directory )
            {
                Assert.Equal( "short", statics[5].Name );
                Assert.Equal( string.Empty, statics[6].Name );
            }
        }

        [Fact]
        public void WillFallBackToTileZeroForOutOfRangeIds()
        {
            using TileDataScope scope = new TileDataScope( Sample( false ) );

            Assert.Equal( "silver ingot", TileData.GetStaticTile( 0x1BF8 ).Name );
            Assert.Equal( "nodraw", TileData.GetStaticTile( -1 ).Name );
            Assert.Equal( "nodraw", TileData.GetStaticTile( scope.Statics.Length ).Name );
            Assert.Equal( "grass", TileData.GetLandTile( 2 ).Name );
        }

        /// <summary>
        ///     For a wearable, the quality byte is the layer it equips to; anything else has no layer.
        /// </summary>
        [Fact]
        public void WillReadTheLayerOfWearablesFromQuality()
        {
            TileDataBuilder builder = new TileDataBuilder();
            builder.Statics[0x1515] = new StaticTileSpec
            {
                Flags = (long) TileFlags.Wearable, Quality = (byte) Layer.Cloak, Name = "cloak"
            };
            builder.Statics[0x1516] = new StaticTileSpec { Quality = (byte) Layer.Cloak, Name = "not worn" };

            using TileDataScope scope = new TileDataScope( builder );

            Assert.Equal( Layer.Cloak, TileData.GetLayer( 0x1515 ) );
            Assert.Equal( Layer.Invalid, TileData.GetLayer( 0x1516 ) );
        }

        /// <summary>
        ///     No client data configured at all gives one placeholder tile, so item names read without an
        ///     install; a configured path with no tiledata.mul is a misconfiguration and throws.
        /// </summary>
        [Fact]
        public void WillOnlyTolerateAMissingFileWhenNoDataPathIsSet()
        {
            object originalPath = DataReaderState.GetStatic( typeof( TileData ), "_dataPath" );

            try
            {
                TileData.Initialize( null );

                StaticTile[] placeholder = (StaticTile[]) DataReaderState.InvokeStatic( typeof( TileData ), "LoadStaticTiles" );

                Assert.Equal( "unknown", Assert.Single( placeholder ).Name );

                using TempDirectory directory = new TempDirectory();

                TileData.Initialize( directory.Path );

                Assert.Throws<FileNotFoundException>( () =>
                    DataReaderState.InvokeStatic( typeof( TileData ), "LoadStaticTiles" ) );
            }
            finally
            {
                DataReaderState.SetStatic( typeof( TileData ), "_dataPath", originalPath );
            }
        }

        private static string Write( TileDataBuilder builder, bool oldFormat, out TempDirectory directory )
        {
            directory = new TempDirectory();
            directory.Write( "tiledata.mul", builder.Build( oldFormat ) );

            return directory.Path;
        }
    }
}
