#region License

// Copyright (C) 2020 Reetus
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

namespace ClassicAssist.UO.Data;

public class Statics
{
    private const int CELLS_PER_BLOCK = 64;

    private static string _dataPath;

    private static readonly Lazy<StaticRecord[][]>[] _staticData = new Lazy<StaticRecord[][]>[6];

    private static int[,] _defaultMapSize { get; } =
    {
        { 7168, 4096 }, { 7168, 4096 }, { 2304, 1600 }, { 2560, 2048 }, { 1448, 1448 }, { 1280, 4096 }
    };

    public static void Initialize( string dataPath )
    {
        _dataPath = dataPath;

        for ( int i = 0; i < 6; i++ )
        {
            int map = i;
            _staticData[i] = new Lazy<StaticRecord[][]>( () => LoadStatics( map ) );
        }
    }

    /// <summary>
    ///     Reads statics{map}.mul into one record array per 8x8 block, with each block's records grouped
    ///     by cell so a lookup can slice out its cell instead of scanning the block. Records keep their
    ///     file order within a cell.
    /// </summary>
    private static StaticRecord[][] LoadStatics( int map )
    {
        string staticIndexFile = Path.Combine( _dataPath, $"staidx{map}.mul" );
        string staticMulFile = Path.Combine( _dataPath, $"statics{map}.mul" );

        if ( !File.Exists( staticIndexFile ) )
        {
            return null;
        }

        if ( !File.Exists( staticMulFile ) )
        {
            return null;
        }

        byte[] indexBytes = File.ReadAllBytes( staticIndexFile );

        // Read whole rather than seeking per block: this used to be a Seek plus 5 BinaryReader calls
        // per record over a FileStream, for a file that is tens of megabytes.
        byte[] mulBytes = File.ReadAllBytes( staticMulFile );

        int blockCount = indexBytes.Length / 12;
        StaticRecord[][] staticItems = new StaticRecord[blockCount][];

        ReadOnlySpan<byte> indexSpan = indexBytes;
        ReadOnlySpan<byte> mulSpan = mulBytes;

        // Scratch for the counting sort, reused for every block.
        Span<int> cellOffsets = stackalloc int[CELLS_PER_BLOCK + 1];

        for ( int x = 0; x < blockCount; x++ )
        {
            int offset = x * 12;
            int start = BitConverter.ToInt32( indexBytes, offset );
            int length = BitConverter.ToInt32( indexBytes, offset + 4 );

            if ( start == -1 )
            {
                continue;
            }

            int recordCount = length > 0 ? length / 7 : 0;

            if ( start < 0 || (long) start + recordCount * 7 > mulSpan.Length )
            {
                // Truncated or corrupt statics file - skip the block rather than tear down the load.
                continue;
            }

            if ( recordCount == 0 )
            {
                // An empty but present block. Distinct from a missing one: the lookup returns an empty
                // array for this and null for that, and callers do check.
                staticItems[x] = [];
                continue;
            }

            ReadOnlySpan<byte> blockSpan = mulSpan.Slice( start, recordCount * 7 );

            cellOffsets.Clear();

            // Counting sort by cell: tally, prefix sum, place. Two passes, and the placement pass walks
            // the block in file order so records within a cell keep it.
            for ( int r = 0; r < recordCount; r++ )
            {
                cellOffsets[CellOf( blockSpan, r ) + 1]++;
            }

            for ( int c = 0; c < CELLS_PER_BLOCK; c++ )
            {
                cellOffsets[c + 1] += cellOffsets[c];
            }

            StaticRecord[] records = new StaticRecord[recordCount];

            for ( int r = 0; r < recordCount; r++ )
            {
                ReadOnlySpan<byte> record = blockSpan.Slice( r * 7, 7 );
                byte cell = (byte) ( record[3] * 8 + record[2] );

                records[cellOffsets[cell]++] = new StaticRecord
                {
                    Cell = cell,
                    ID = (ushort) ( record[0] | ( record[1] << 8 ) ),
                    Z = (sbyte) record[4],
                    Hue = (ushort) ( record[5] | ( record[6] << 8 ) )
                };
            }

            staticItems[x] = records;
        }

        return staticItems;
    }

    private static byte CellOf( ReadOnlySpan<byte> blockSpan, int record )
    {
        // x at offset 2, y at offset 3 of the 7 byte record.
        return (byte) ( blockSpan[record * 7 + 3] * 8 + blockSpan[record * 7 + 2] );
    }

    /// <summary>
    ///     Locates the given cell's records within a block, which are contiguous because
    ///     <see cref="LoadStatics" /> grouped them.
    /// </summary>
    private static bool TryGetCellRange( int map, int x, int y, out StaticRecord[] records, out int start,
        out int count )
    {
        records = null;
        start = 0;
        count = 0;

        StaticRecord[][] blocks = _staticData?[map]?.Value;

        if ( blocks == null )
        {
            return false;
        }

        int blockIndex = x / 8 * ( _defaultMapSize[map, 1] / 8 ) + y / 8;

        if ( (uint) blockIndex >= (uint) blocks.Length )
        {
            return false;
        }

        StaticRecord[] blockStatics = blocks[blockIndex];

        if ( blockStatics == null )
        {
            return false;
        }

        records = blockStatics;

        byte cell = (byte) ( y % 8 * 8 + x % 8 );

        start = LowerBound( blockStatics, cell );

        while ( start + count < blockStatics.Length && blockStatics[start + count].Cell == cell )
        {
            count++;
        }

        return true;
    }

    private static int LowerBound( StaticRecord[] records, byte cell )
    {
        int lo = 0;
        int hi = records.Length;

        while ( lo < hi )
        {
            int mid = (int) ( ( (uint) lo + (uint) hi ) >> 1 );

            if ( records[mid].Cell < cell )
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    public static StaticTile[] GetStatics( int map, int x, int y )
    {
        if ( !TryGetCellRange( map, x, y, out StaticRecord[] records, out int start, out int count ) )
        {
            return null;
        }

        StaticTile[] tiles = new StaticTile[count];

        for ( int i = 0; i < count; i++ )
        {
            tiles[i] = ToTile( records[start + i], x, y );
        }

        return tiles;
    }

    /// <summary>
    ///     Appends the statics at ( x, y ) to <paramref name="results" />, for callers that only iterate
    ///     what they get back and can reuse a list across calls.
    /// </summary>
    public static void GetStatics( int map, int x, int y, List<StaticTile> results )
    {
        if ( !TryGetCellRange( map, x, y, out StaticRecord[] records, out int start, out int count ) )
        {
            return;
        }

        for ( int i = 0; i < count; i++ )
        {
            results.Add( ToTile( records[start + i], x, y ) );
        }
    }

    private static StaticTile ToTile( StaticRecord record, int x, int y )
    {
        StaticTile tile = TileData.GetStaticTile( record.ID );
        tile.X = x;
        tile.Y = y;
        tile.Z = record.Z;
        tile.Hue = record.Hue;

        return tile;
    }

    private struct StaticRecord
    {
        public ushort ID;
        public ushort Hue;
        public byte Cell;
        public sbyte Z;
    }
}
