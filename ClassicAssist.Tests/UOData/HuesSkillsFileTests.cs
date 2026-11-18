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
using System.Text;
using ClassicAssist.UO.Data;
using Xunit;

namespace ClassicAssist.Tests.UOData
{
    /// <summary>
    ///     hues.mul read from a synthetic file: 375 blocks of a 4 byte header and 8 entries of 32 colours,
    ///     table start/end and a 20 byte name. A misplaced block header shifts every later hue by one, which
    ///     recolours everything without failing, so the first and last entries and one either side of a block
    ///     boundary are checked.
    /// </summary>
    public class HuesFileTests : IDisposable
    {
        private readonly TempDirectory _directory = new TempDirectory();
        private readonly Lazy<HueEntry[]> _originalHues;
        private readonly object _originalPath;

        public HuesFileTests()
        {
            _originalHues = Hues._lazyHueEntries;
            _originalPath = DataReaderState.GetStatic( typeof( Hues ), "_dataPath" );
        }

        public void Dispose()
        {
            Hues._lazyHueEntries = _originalHues;
            DataReaderState.SetStatic( typeof( Hues ), "_dataPath", _originalPath );
            _directory.Dispose();
        }

        private static byte[] HuesFile()
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter( stream );

            for ( int block = 0; block < 375; block++ )
            {
                writer.Write( block );

                for ( int entry = 0; entry < 8; entry++ )
                {
                    int hue = block * 8 + entry;

                    for ( int c = 0; c < 32; c++ )
                    {
                        // Colour words are stored without the alpha bit; distinct per hue and per index.
                        writer.Write( (ushort) ( ( hue * 7 + c ) & 0x7FFF ) );
                    }

                    writer.Write( (ushort) 1 );
                    writer.Write( (ushort) 2 );

                    byte[] name = new byte[20];
                    Encoding.ASCII.GetBytes( $"hue {hue}" ).CopyTo( name, 0 );
                    writer.Write( name );
                }
            }

            writer.Flush();

            return stream.ToArray();
        }

        private HueEntry[] Load()
        {
            _directory.Write( "hues.mul", HuesFile() );
            Hues.Initialize( _directory.Path );

            return (HueEntry[]) DataReaderState.InvokeStatic( typeof( Hues ), "LoadHueIndex" );
        }

        [Fact]
        public void WillReadEveryEntryAcrossBlockHeaders()
        {
            HueEntry[] entries = Load();

            Assert.Equal( 3000, entries.Length );

            foreach ( int hue in new[] { 0, 7, 8, 1500, 2999 } )
            {
                Assert.Equal( $"hue {hue}", entries[hue].Name );
                Assert.Equal( (short) ( ( ( hue * 7 ) & 0x7FFF ) | 0x8000 ), entries[hue].Colors[0] );
                Assert.Equal( (short) ( ( ( hue * 7 + 31 ) & 0x7FFF ) | 0x8000 ), entries[hue].Colors[31] );
            }
        }

        /// <summary>
        ///     Hue ids from the server are 1-based and carry flags in the top bits; both are stripped before
        ///     indexing, and an id past the table leaves the pixels alone.
        /// </summary>
        [Fact]
        public void WillIndexOneBasedHuesAndIgnoreTheFlagBits()
        {
            HueEntry[] entries = Load();
            Hues._lazyHueEntries = new Lazy<HueEntry[]>( () => entries );

            ushort[] colours = { 0x8000 | ( 5 << 10 ), 0 };

            Hues.ApplyHue( 0x8000 | 0x4000 | 9, colours, false );

            Assert.Equal( (ushort) entries[8].Colors[5], colours[0] );
            Assert.Equal( 0, colours[1] );

            ushort[] untouched = { 0x8000 | ( 5 << 10 ) };
            Hues.ApplyHue( 3001, untouched, false );

            Assert.Equal( 0x8000 | ( 5 << 10 ), untouched[0] );
        }

        [Fact]
        public void WillThrowWhenHuesMulIsMissing()
        {
            Hues.Initialize( _directory.Path );

            Assert.Throws<FileNotFoundException>( () => DataReaderState.InvokeStatic( typeof( Hues ), "LoadHueIndex" ) );
        }
    }

    /// <summary>
    ///     Skills.idx / skills.mul from a synthetic install. Each skill record is an "invokable" byte then a
    ///     NUL terminated name, and the index's length counts both, so an off-by-one shows as a name with a
    ///     stray character or a missing last letter. The list ends at the first zero-length index entry.
    /// </summary>
    public class SkillsFileTests : IDisposable
    {
        private readonly TempDirectory _directory = new TempDirectory();
        private readonly object _originalLazy;
        private readonly object _originalPath;

        public SkillsFileTests()
        {
            _originalLazy = DataReaderState.GetStatic( typeof( Skills ), "_lazySkillData" );
            _originalPath = DataReaderState.GetStatic( typeof( Skills ), "_dataPath" );
        }

        public void Dispose()
        {
            DataReaderState.SetStatic( typeof( Skills ), "_lazySkillData", _originalLazy );
            DataReaderState.SetStatic( typeof( Skills ), "_dataPath", _originalPath );
            _directory.Dispose();
        }

        private void Write( params (bool Invokable, string Name)[] skills )
        {
            List<byte> mul = new List<byte>();
            List<byte> index = new List<byte>();

            foreach ( (bool invokable, string name) in skills )
            {
                int start = mul.Count;

                mul.Add( (byte) ( invokable ? 1 : 0 ) );
                mul.AddRange( Encoding.ASCII.GetBytes( name ) );
                mul.Add( 0 );

                index.AddRange( BitConverter.GetBytes( start ) );
                index.AddRange( BitConverter.GetBytes( mul.Count - start ) );
                index.AddRange( BitConverter.GetBytes( 0 ) );
            }

            // A zero length entry ends the list; the one after it must not be read.
            index.AddRange( new byte[12] );
            index.AddRange( BitConverter.GetBytes( 0 ) );
            index.AddRange( BitConverter.GetBytes( 5 ) );
            index.AddRange( BitConverter.GetBytes( 0 ) );

            _directory.Write( "Skills.idx", index.ToArray() );
            _directory.Write( "skills.mul", mul.ToArray() );
            Skills.Initialize( _directory.Path );
        }

        [Fact]
        public void WillReadNamesAndInvokableFlags()
        {
            Write( ( false, "Alchemy" ), ( true, "Anatomy" ), ( false, "Animal Lore" ) );

            Assert.Equal( "Alchemy", Skills.GetSkillName( 0 ) );
            Assert.Equal( "Anatomy", Skills.GetSkillName( 1 ) );
            Assert.Equal( "Animal Lore", Skills.GetSkillName( 2 ) );
            Assert.Equal( string.Empty, Skills.GetSkillName( 3 ) );

            Assert.False( Skills.IsInvokable( 0 ) );
            Assert.True( Skills.IsInvokable( 1 ) );
            Assert.False( Skills.IsInvokable( -1 ) );
            Assert.False( Skills.IsInvokable( 4 ) );
            Assert.Equal( 3, Skills.GetSkillsArray().Length );
        }

        [Fact]
        public void WillThrowWhenTheFilesAreMissing()
        {
            Skills.Initialize( _directory.Path );

            Assert.Throws<FileNotFoundException>( () => Skills.GetSkillName( 0 ) );
        }

        /// <summary>
        ///     Documented as returning the names indexed by skill id. It casts a LINQ Select iterator to
        ///     string[], which always throws InvalidCastException.
        /// </summary>
        [Fact( Skip = "BUG: Skills.GetSkillNames casts IEnumerable<string> (Select) to string[] and always throws InvalidCastException" )]
        public void WillReturnSkillNamesIndexedById()
        {
            Write( ( false, "Alchemy" ), ( true, "Anatomy" ) );

            Assert.Equal( new[] { "Alchemy", "Anatomy" }, Skills.GetSkillNames() );
        }
    }
}
