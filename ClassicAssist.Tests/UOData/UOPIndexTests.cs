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
    ///     <see cref="UOPIndex" /> turns a .uop container back into the flat .mul the map reader expects: entries
    ///     are ordered by the key at the start of their data (not by table or file position), zero length and
    ///     unused slots drop out, and <see cref="UOPIndex.Lookup" /> translates a flat offset to a file offset by
    ///     binary search. An off-by-one at an entry boundary reads the neighbouring map chunk, which shows up as
    ///     wrong terrain rather than as an error, so every offset is checked against an independently built
    ///     flat image.
    /// </summary>
    public class UOPIndexTests
    {
        /// <summary>
        ///     Entry data whose first int is the ordering key, followed by recognisable filler.
        /// </summary>
        private static byte[] Chunk( int order, int length )
        {
            byte[] data = new byte[length];
            BitConverter.GetBytes( order ).CopyTo( data, 0 );

            for ( int i = 4; i < length; i++ )
            {
                data[i] = (byte) ( order * 31 + i );
            }

            return data;
        }

        private static UOPIndex Open( byte[] file )
        {
            return new UOPIndex( new MemoryStream( file ) );
        }

        [Fact]
        public void WillRejectAFileWithoutThePackageMagic()
        {
            byte[] file = UopWriter.Build( new List<UopFile> { new UopFile( 1, Chunk( 0, 8 ) ) }, 10 );
            file[0] = 0x00;

            Assert.Throws<ArgumentException>( () => Open( file ) );
        }

        [Fact]
        public void WillReadTheVersionFromTheHeader()
        {
            byte[] file = UopWriter.Build( new List<UopFile> { new UopFile( 1, Chunk( 0, 8 ) ) }, 10, 7 );

            using UOPIndex index = Open( file );

            Assert.Equal( 7, index.Version );
        }

        /// <summary>
        ///     Entries spread over three chained tables, stored in the file in reverse, and keyed out of table
        ///     order. The flattened stream must follow the keys, and every single offset must translate to the
        ///     byte at that position of the flat image.
        /// </summary>
        [Fact]
        public void WillFlattenEntriesInKeyOrderAcrossChainedTables()
        {
            int[] orders = { 3, 0, 4, 1, 2 };
            int[] lengths = { 17, 9, 40, 4, 23 };

            List<UopFile> files = new List<UopFile>();

            for ( int i = 0; i < orders.Length; i++ )
            {
                files.Add( new UopFile( (ulong) ( 100 + i ), Chunk( orders[i], lengths[i] ) ) );
            }

            byte[] file = UopWriter.Build( files, 2 );

            byte[] expected = Bytes.Concat( files.OrderBy( f => BitConverter.ToInt32( f.Data, 0 ) )
                .Select( f => f.Data ).ToArray() );

            using UOPIndex index = Open( file );

            Assert.Equal( expected, index.ReadAll() );

            for ( int offset = 0; offset < expected.Length; offset++ )
            {
                int translated = index.Lookup( offset );

                Assert.InRange( translated, 0, file.Length - 1 );
                Assert.Equal( expected[offset], file[translated] );
            }
        }

        /// <summary>
        ///     The offset exactly at the end of one entry is the first byte of the next, not one past the end
        ///     of the first - the case the binary search has to step over by hand.
        /// </summary>
        [Fact]
        public void WillMapAnEntryEndBoundaryToTheStartOfTheNextEntry()
        {
            List<UopFile> files = new List<UopFile> { new UopFile( 1, Chunk( 0, 10 ) ), new UopFile( 2, Chunk( 1, 10 ) ) };

            byte[] file = UopWriter.Build( files, 10 );

            using UOPIndex index = Open( file );

            // The second entry's data starts with its key, 1, so its first byte reads 0x01.
            Assert.Equal( 0x01, file[index.Lookup( 10 )] );
            Assert.Equal( 1, BitConverter.ToInt32( file, index.Lookup( 10 ) ) );
            Assert.Equal( 0, BitConverter.ToInt32( file, index.Lookup( 0 ) ) );
            Assert.Equal( index.Lookup( 9 ) + 1, index.Lookup( 0 ) + 10 );
        }

        [Fact]
        public void WillReturnTheFileLengthForOffsetsPastTheEnd()
        {
            byte[] file = UopWriter.Build( new List<UopFile> { new UopFile( 1, Chunk( 0, 12 ) ) }, 10 );

            using UOPIndex index = Open( file );

            Assert.Equal( file.Length, index.Lookup( 12 ) );
            Assert.Equal( file.Length, index.Lookup( 5000 ) );
        }

        /// <summary>
        ///     Unused table slots (data address 0) and zero length entries occupy no space in the flat stream.
        ///     A zero length entry left in would make two cumulative totals equal and the binary search
        ///     ambiguous.
        /// </summary>
        [Fact]
        public void WillSkipUnusedSlotsAndZeroLengthEntries()
        {
            List<UopFile> files = new List<UopFile>
            {
                new UopFile( 1, Chunk( 0, 6 ) ),
                new UopFile( 0, Array.Empty<byte>() ) { EmptySlot = true },
                new UopFile( 2, Array.Empty<byte>() ),
                new UopFile( 3, Chunk( 2, 6 ) ),
                new UopFile( 0, Array.Empty<byte>() ) { EmptySlot = true }
            };

            byte[] file = UopWriter.Build( files, 3 );

            using UOPIndex index = Open( file );

            byte[] expected = Bytes.Concat( files[0].Data, files[3].Data );

            Assert.Equal( expected, index.ReadAll() );

            for ( int offset = 0; offset < expected.Length; offset++ )
            {
                Assert.Equal( expected[offset], file[index.Lookup( offset )] );
            }
        }

        [Fact]
        public void WillTreatAContainerWithNoEntriesAsEmpty()
        {
            byte[] file = UopWriter.Build( new List<UopFile>(), 10 );

            using UOPIndex index = Open( file );

            Assert.Empty( index.ReadAll() );
            Assert.Equal( file.Length, index.Lookup( 0 ) );
        }

        /// <summary>
        ///     A next-table pointer past the end of the file ends the walk instead of seeking off the end.
        /// </summary>
        [Fact]
        public void WillStopAtANextTablePointerPastTheEndOfTheFile()
        {
            List<UopFile> files = new List<UopFile> { new UopFile( 1, Chunk( 0, 8 ) ) };
            byte[] file = UopWriter.Build( files, 10 );

            // The only table is the last thing in the file: count, then its next pointer.
            int table = BitConverter.ToInt32( file, 12 );
            BitConverter.GetBytes( (long) file.Length + 100 ).CopyTo( file, table + 4 );

            using UOPIndex index = Open( file );

            Assert.Equal( files[0].Data, index.ReadAll() );
        }
    }
}
