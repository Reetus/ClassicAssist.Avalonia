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
using ClassicAssist.Data;
using ClassicAssist.Shared;
using ClassicAssist.UO.Data;
using Xunit;

namespace ClassicAssist.Tests.UOData
{
    /// <summary>
    ///     The compressor for the format <see cref="BwtDecompress" /> reads (cliloc files from 7.0.104), built
    ///     from what the decompressor reverses so tests need no client data:
    ///     <list type="number">
    ///         <item>
    ///             Symbol stage: 256 little-endian int counts, then one block per symbol present, most frequent
    ///             first (ties by symbol value). A block is <c>count</c> bytes: the symbol's rank by first
    ///             appearance, then for each later occurrence the number of distinct symbols seen since the
    ///             previous one.
    ///         </item>
    ///         <item>Move-to-front over a 0..255 table.</item>
    ///         <item>A 4 byte header in front, and one trailing byte the reader consumes but never decodes.</item>
    ///     </list>
    /// </summary>
    internal static class BwtCompressor
    {
        public static byte[] Compress( byte[] data )
        {
            int[] counts = new int[256];

            foreach ( byte b in data )
            {
                counts[b]++;
            }

            List<int> bySymbolFrequency = Enumerable.Range( 0, 256 ).Where( s => counts[s] > 0 )
                .OrderByDescending( s => counts[s] ).ThenBy( s => s ).ToList();

            Dictionary<int, List<byte>> blocks = bySymbolFrequency.ToDictionary( s => s, _ => new List<byte>() );
            Dictionary<int, int> lastSeen = new Dictionary<int, int>();
            List<int> firstAppearance = new List<int>();

            for ( int t = 0; t < data.Length; t++ )
            {
                int symbol = data[t];

                if ( lastSeen.TryGetValue( symbol, out int previous ) )
                {
                    int distinct = 0;
                    bool[] seen = new bool[256];

                    for ( int u = previous + 1; u < t; u++ )
                    {
                        if ( !seen[data[u]] )
                        {
                            seen[data[u]] = true;
                            distinct++;
                        }
                    }

                    blocks[symbol].Add( (byte) distinct );
                }
                else
                {
                    blocks[symbol].Add( (byte) firstAppearance.Count );
                    firstAppearance.Add( symbol );
                }

                lastSeen[symbol] = t;
            }

            List<byte> payload = new List<byte>();

            foreach ( int count in counts )
            {
                payload.AddRange( BitConverter.GetBytes( count ) );
            }

            foreach ( int symbol in bySymbolFrequency )
            {
                payload.AddRange( blocks[symbol] );
            }

            List<byte> output = new List<byte> { 0xDE, 0xAD, 0xBE, 0xEF };
            List<byte> table = Enumerable.Range( 0, 256 ).Select( i => (byte) i ).ToList();

            foreach ( byte b in payload )
            {
                int index = table.IndexOf( b );
                output.Add( (byte) index );
                table.RemoveAt( index );
                table.Insert( 0, b );
            }

            output.Add( 0 );

            return output.ToArray();
        }
    }

    /// <summary>
    ///     Round trips through <see cref="BwtDecompress" /> using the test-only <see cref="BwtCompressor" />.
    ///     The decompressor reads without validating anything, so a regression shows up as cliloc text that is
    ///     silently wrong rather than missing (see ClilocTests). The inputs cover the paths through its symbol
    ///     list: one symbol only, a symbol exhausted early, all 256 values, and frequency ties.
    /// </summary>
    public class BwtDecompressTests : IDisposable
    {
        private readonly Version _originalClientVersion;
        private readonly object _originalClilocList;
        private readonly object _originalClilocPath;

        public BwtDecompressTests()
        {
            _originalClientVersion = Engine.ClientVersion;
            _originalClilocList = DataReaderState.GetStatic( typeof( Cliloc ), "_lazyClilocList" );
            _originalClilocPath = DataReaderState.GetStatic( typeof( Cliloc ), "_dataPath" );
        }

        public void Dispose()
        {
            Engine.ClientVersion = _originalClientVersion;
            DataReaderState.SetStatic( typeof( Cliloc ), "_lazyClilocList", _originalClilocList );
            DataReaderState.SetStatic( typeof( Cliloc ), "_dataPath", _originalClilocPath );
        }

        public static TheoryData<string> Inputs =>
            new TheoryData<string> { "a", "aaaaaaaa", "ab", "abracadabra", "banana bandana", "zyxwvutsrqponm" };

        [Theory]
        [MemberData( nameof( Inputs ) )]
        public void WillRoundTripText( string text )
        {
            byte[] data = Encoding.ASCII.GetBytes( text );

            Assert.Equal( data, BwtDecompress.Decompress( BwtCompressor.Compress( data ) ) );
        }

        [Fact]
        public void WillRoundTripEveryByteValue()
        {
            byte[] data = Enumerable.Range( 0, 256 ).Select( i => (byte) i )
                .Concat( Enumerable.Range( 0, 256 ).Select( i => (byte) ( 255 - i ) ) ).ToArray();

            Assert.Equal( data, BwtDecompress.Decompress( BwtCompressor.Compress( data ) ) );
        }

        /// <summary>
        ///     Skewed random data - a long block for the common symbols, single occurrences for the rare ones,
        ///     and many frequency ties in between.
        /// </summary>
        [Fact]
        public void WillRoundTripRandomData()
        {
            Random random = new Random( 1234 );
            byte[] data = new byte[20000];

            for ( int i = 0; i < data.Length; i++ )
            {
                data[i] = (byte) ( random.Next( 4 ) == 0 ? random.Next( 256 ) : random.Next( 8 ) );
            }

            Assert.Equal( data, BwtDecompress.Decompress( BwtCompressor.Compress( data ) ) );
        }

        /// <summary>
        ///     The header is skipped rather than interpreted: two buffers differing only there decode the same.
        /// </summary>
        [Fact]
        public void WillIgnoreTheHeader()
        {
            byte[] compressed = BwtCompressor.Compress( Encoding.ASCII.GetBytes( "header test" ) );
            byte[] altered = (byte[]) compressed.Clone();
            altered[0] = 0x00;
            altered[3] = 0x7F;

            Assert.Equal( BwtDecompress.Decompress( compressed ), BwtDecompress.Decompress( altered ) );
        }

        /// <summary>
        ///     End to end: a compressed Cliloc.enu is decompressed and parsed when the client is 7.0.104 or later,
        ///     including an empty string between two others (the case the old parse loop stopped on).
        /// </summary>
        [Fact]
        public void WillLoadACompressedClilocForNewClients()
        {
            using TempDirectory directory = new TempDirectory();

            directory.Write( "Cliloc.enu", BwtCompressor.Compress( ClilocFile( ( 500000, "First" ), ( 500001, "" ),
                ( 1042971, "~1_NOTHING~" ), ( 3000184, "Hair Color" ) ) ) );

            Engine.ClientVersion = new Version( 7, 0, 104, 0 );
            Cliloc.Initialize( directory.Path );

            Dictionary<int, string> items = Cliloc.GetItems();

            Assert.Equal( 4, items.Count );
            Assert.Equal( "First", Cliloc.GetProperty( 500000 ) );
            Assert.Equal( string.Empty, items[500001] );
            Assert.Equal( "~1_NOTHING~", Cliloc.GetProperty( 1042971 ) );
            Assert.Equal( "Hair Color", Cliloc.GetProperty( 3000184 ) );
        }

        /// <summary>
        ///     Older clients ship the file uncompressed; decompressing it anyway would produce garbage.
        /// </summary>
        [Fact]
        public void WillReadAnUncompressedClilocForOldClients()
        {
            using TempDirectory directory = new TempDirectory();

            directory.Write( "Cliloc.enu", ClilocFile( ( 500000, "First" ), ( 500002, "Second" ) ) );

            Engine.ClientVersion = new Version( 7, 0, 103, 0 );
            Cliloc.Initialize( directory.Path );

            Assert.Equal( "Second", Cliloc.GetProperty( 500002 ) );
            Assert.Equal( 2, Cliloc.GetItems().Count );
        }

        /// <summary>
        ///     Cliloc.enu: int header, short header, then per entry int number, byte flag, ushort length and
        ///     UTF-8 text.
        /// </summary>
        private static byte[] ClilocFile( params (int Number, string Text)[] entries )
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter( stream );

            writer.Write( 2 );
            writer.Write( (short) 1 );

            foreach ( (int number, string text) in entries )
            {
                byte[] utf8 = Encoding.UTF8.GetBytes( text );

                writer.Write( number );
                writer.Write( (byte) 0 );
                writer.Write( (ushort) utf8.Length );
                writer.Write( utf8 );
            }

            writer.Flush();

            return stream.ToArray();
        }
    }
}
