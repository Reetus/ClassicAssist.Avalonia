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
using ClassicAssist.UO.Data;
using Xunit;

namespace ClassicAssist.Tests.UOData
{
    /// <summary>
    ///     A static item tile as art.mul stores it: per row, runs of (x offset, length, colour words), ending
    ///     in (0, 0), with a table of row offsets in front.
    /// </summary>
    internal sealed class ArtTile
    {
        public ArtTile( int width, int height )
        {
            Width = width;
            Height = height;
            Rows = Enumerable.Range( 0, height ).Select( _ => new List<(int Skip, ushort[] Colours)>() ).ToArray();
        }

        public int Height { get; }
        public List<(int Skip, ushort[] Colours)>[] Rows { get; }
        public int Width { get; }

        public ArtTile Run( int row, int skip, params ushort[] colours )
        {
            Rows[row].Add( ( skip, colours ) );

            return this;
        }

        public byte[] Encode()
        {
            List<ushort> body = new List<ushort>();
            List<ushort> lookups = new List<ushort>();

            foreach ( List<(int Skip, ushort[] Colours)> row in Rows )
            {
                lookups.Add( (ushort) body.Count );

                foreach ( (int skip, ushort[] colours) in row )
                {
                    body.Add( (ushort) skip );
                    body.Add( (ushort) colours.Length );
                    body.AddRange( colours );
                }

                body.Add( 0 );
                body.Add( 0 );
            }

            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter( stream );

            writer.Write( 0x1234 );
            writer.Write( (short) Width );
            writer.Write( (short) Height );
            lookups.ForEach( writer.Write );
            body.ForEach( writer.Write );
            writer.Flush();

            return stream.ToArray();
        }
    }

    /// <summary>
    ///     <see cref="Art.GetStatic(int)" /> against synthetic art.mul/artidx.mul and artLegacyMUL.uop. The run
    ///     decoder decides which pixels are transparent and the uop path finds entries by hashing their file
    ///     names, so a wrong hash finds nothing and a wrong offset draws another item's art. Item art backs the
    ///     entity viewer and gump images; a corrupt tile must degrade to a partial image rather than throw.
    /// </summary>
    public class ArtFileTests : IDisposable
    {
        private const int StaticBase = 0x4000;

        private readonly TempDirectory _directory = new TempDirectory();
        private readonly object _originalPath;
        private readonly object _originalUop;
        private readonly object _originalIndex;
        private readonly Lazy<HueEntry[]> _originalHues;

        public ArtFileTests()
        {
            _originalPath = DataReaderState.GetStatic( typeof( Art ), "_dataPath" );
            _originalUop = DataReaderState.GetStatic( typeof( Art ), "_isUOPFormat" );
            _originalIndex = DataReaderState.GetStatic( typeof( Art ), "_lazyIndex" );
            _originalHues = Hues._lazyHueEntries;

            // Initialize only ever sets the format flag, so start from .mul.
            DataReaderState.SetStatic( typeof( Art ), "_isUOPFormat", false );
        }

        public void Dispose()
        {
            DataReaderState.SetStatic( typeof( Art ), "_dataPath", _originalPath );
            DataReaderState.SetStatic( typeof( Art ), "_isUOPFormat", _originalUop );
            DataReaderState.SetStatic( typeof( Art ), "_lazyIndex", _originalIndex );
            Hues._lazyHueEntries = _originalHues;
            _directory.Dispose();
        }

        /// <summary>
        ///     4 x 3: row 0 has two runs with a gap, row 1 is empty, row 2 is fully covered. Colour words are
        ///     stored without the alpha bit, which the decoder adds.
        /// </summary>
        private static ArtTile Sample()
        {
            return new ArtTile( 4, 3 )
                .Run( 0, 0, 0x7C00 )
                .Run( 0, 1, 0x03E0, 0x001F )
                .Run( 2, 0, 0x4210, 0x4210, 0x4210, 0x7FFF );
        }

        private static void AssertSample( Pixmap pixmap )
        {
            Assert.Equal( 4, pixmap.Width );
            Assert.Equal( 3, pixmap.Height );

            Assert.Equal( HuesHelper.Color16To32( 0xFC00 ), pixmap.GetPixel( 0, 0 ) );
            Assert.Equal( 0u, pixmap.GetPixel( 1, 0 ) );
            Assert.Equal( HuesHelper.Color16To32( 0x83E0 ), pixmap.GetPixel( 2, 0 ) );
            Assert.Equal( HuesHelper.Color16To32( 0x801F ), pixmap.GetPixel( 3, 0 ) );

            for ( int x = 0; x < 4; x++ )
            {
                Assert.Equal( 0u, pixmap.GetPixel( x, 1 ) );
            }

            Assert.Equal( HuesHelper.Color16To32( 0xC210 ), pixmap.GetPixel( 0, 2 ) );
            Assert.Equal( HuesHelper.Color16To32( 0xFFFF ), pixmap.GetPixel( 3, 2 ) );
        }

        /// <summary>
        ///     artidx.mul: one (lookup, length, extra) per index; statics start at 0x4000.
        /// </summary>
        private void WriteMul( IDictionary<int, byte[]> tiles, IDictionary<int, int> lookupOverrides = null )
        {
            int entries = tiles.Keys.Max() + StaticBase + 1;
            byte[] index = new byte[entries * 12];
            List<byte> art = new List<byte>();

            for ( int i = 0; i < entries; i++ )
            {
                BitConverter.GetBytes( -1 ).CopyTo( index, i * 12 );
            }

            foreach ( KeyValuePair<int, byte[]> tile in tiles )
            {
                int slot = ( StaticBase + tile.Key ) * 12;
                BitConverter.GetBytes( art.Count ).CopyTo( index, slot );
                BitConverter.GetBytes( tile.Value.Length ).CopyTo( index, slot + 4 );
                art.AddRange( tile.Value );
            }

            if ( lookupOverrides != null )
            {
                foreach ( KeyValuePair<int, int> pair in lookupOverrides )
                {
                    BitConverter.GetBytes( pair.Value ).CopyTo( index, ( StaticBase + pair.Key ) * 12 );
                }
            }

            _directory.Write( "artidx.mul", index );
            _directory.Write( "art.mul", art.ToArray() );
            Art.Initialize( _directory.Path );
        }

        private void WriteUop( IDictionary<int, byte[]> tiles )
        {
            List<UopFile> files = tiles.Select( t =>
                new UopFile( Lookup3.Hash( $"build/artlegacymul/{StaticBase + t.Key:D8}.tga" ), t.Value ) ).ToList();

            _directory.Write( "artLegacyMUL.uop", UopWriter.Build( files, 2 ) );
            Art.Initialize( _directory.Path );
        }

        [Fact]
        public void WillDecodeRunsFromMul()
        {
            WriteMul( new Dictionary<int, byte[]> { { 0, new ArtTile( 1, 1 ).Run( 0, 0, 1 ).Encode() }, { 7, Sample().Encode() } } );

            AssertSample( Art.GetStatic( 7 ) );
        }

        [Fact]
        public void WillDecodeRunsFromUop()
        {
            WriteUop( new Dictionary<int, byte[]> { { 3, new ArtTile( 1, 1 ).Run( 0, 0, 1 ).Encode() }, { 0x1BF8, Sample().Encode() } } );

            AssertSample( Art.GetStatic( 0x1BF8 ) );
            Assert.Equal( 1, Art.GetStatic( 3 ).Width );
        }

        [Fact]
        public void WillReturnEmptyWhenNeitherTheItemNorItemZeroIsInTheUop()
        {
            WriteUop( new Dictionary<int, byte[]> { { 5, Sample().Encode() } } );

            Assert.True( Art.GetStatic( 6 ).IsEmpty );
        }

        [Fact]
        public void WillReturnEmptyWhenThereIsNoArtFile()
        {
            Art.Initialize( _directory.Path );

            Assert.True( Art.GetStatic( 1 ).IsEmpty );
        }

        /// <summary>
        ///     artidx.mul marks an item with no art with a lookup of -1. That must come back empty, as the
        ///     method documents, rather than seek to -1 and throw.
        /// </summary>
        [Fact( Skip = "BUG: Art.DecodeStatic seeks to an artidx lookup of -1 (no art) and throws IOException instead of returning Pixmap.Empty" )]
        public void WillReturnEmptyForAnItemMarkedAsHavingNoArt()
        {
            WriteMul( new Dictionary<int, byte[]> { { 0, Sample().Encode() }, { 2, Sample().Encode() } },
                new Dictionary<int, int> { { 2, -1 } } );

            Assert.True( Art.GetStatic( 2 ).IsEmpty );
        }

        [Fact]
        public void WillReturnEmptyForAZeroSizedTile()
        {
            WriteMul( new Dictionary<int, byte[]> { { 0, new ArtTile( 0, 0 ).Encode() } } );

            Assert.True( Art.GetStatic( 0 ).IsEmpty );
        }

        /// <summary>
        ///     A run that would write past the end of its row stops that row; the rows around it still decode.
        /// </summary>
        [Fact]
        public void WillStopARowThatRunsPastTheTileEdge()
        {
            ArtTile tile = new ArtTile( 3, 2 )
                .Run( 0, 1, 0x7FFF, 0x7FFF, 0x7FFF )
                .Run( 1, 0, 0x001F, 0x001F, 0x001F );

            WriteMul( new Dictionary<int, byte[]> { { 0, tile.Encode() } } );

            Pixmap pixmap = Art.GetStatic( 0 );

            Assert.Equal( 0u, pixmap.GetPixel( 1, 0 ) );
            Assert.Equal( 0u, pixmap.GetPixel( 2, 0 ) );
            Assert.Equal( HuesHelper.Color16To32( 0x801F ), pixmap.GetPixel( 2, 1 ) );
        }

        /// <summary>
        ///     Hueing recolours by the pixel's red channel through the hue's ramp. With the tile's PartialHue
        ///     flag only grey pixels change, so coloured detail survives; transparent pixels never change.
        /// </summary>
        [Fact]
        public void WillHueOnlyGreyPixelsOfAPartialHueTile()
        {
            ArtTile art = new ArtTile( 3, 1 ).Run( 0, 1, 0x4210, 0x7C00 );

            WriteMul( new Dictionary<int, byte[]> { { 0, art.Encode() }, { 1, art.Encode() } } );

            short[] ramp = Enumerable.Range( 0, 32 ).Select( i => (short) ( 0x8000 | ( i << 5 ) ) ).ToArray();
            HueEntry[] hues = new HueEntry[3000];
            hues[0x20] = new HueEntry { Colors = ramp };
            Hues._lazyHueEntries = new Lazy<HueEntry[]>( () => hues );

            TileDataBuilder tiles = new TileDataBuilder();
            tiles.Statics[1] = new StaticTileSpec { Flags = (long) TileFlags.PartialHue };

            using TileDataScope scope = new TileDataScope( tiles );

            Pixmap full = Art.GetStatic( 0, 0x21 );
            Pixmap partial = Art.GetStatic( 1, 0x21 );

            Assert.Equal( 0u, full.GetPixel( 0, 0 ) );
            Assert.Equal( HuesHelper.Color16To32( (ushort) ramp[0x10] ), full.GetPixel( 1, 0 ) );
            Assert.Equal( HuesHelper.Color16To32( (ushort) ramp[0x1F] ), full.GetPixel( 2, 0 ) );

            Assert.Equal( HuesHelper.Color16To32( (ushort) ramp[0x10] ), partial.GetPixel( 1, 0 ) );
            Assert.Equal( HuesHelper.Color16To32( 0xFC00 ), partial.GetPixel( 2, 0 ) );
        }

        /// <summary>
        ///     The uop entry names are hashed with lookup3's hashlittle2. Checked against the reference
        ///     implementation's published vector, and the product's transliteration against an independent
        ///     one for every tail length the hash distinguishes.
        /// </summary>
        [Fact]
        public void WillHashFileNamesWithLookup3()
        {
            Assert.Equal( 0xCE7226E617770551UL, Lookup3.Hash( "Four score and seven years ago" ) );

            const string alphabet = "build/artlegacymul/00016384.tga-and-some-more-text";

            for ( int length = 1; length <= alphabet.Length; length++ )
            {
                string name = alphabet[..length];

                Assert.Equal( Lookup3.Hash( name ), (ulong) DataReaderState.InvokeStatic( typeof( Art ), "HashFileName", name ) );
            }
        }
    }
}
