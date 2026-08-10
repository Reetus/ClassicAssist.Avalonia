using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClassicAssist.UO.Data;

public class UOPIndex : IDisposable
{
    /// <summary>
    ///     Running total of entry lengths, so <c>_cumulativeLengths[i]</c> is the exclusive end of entry
    ///     <c>i</c> in the flattened stream. Lets <see cref="Lookup" /> binary search rather than walk every
    ///     entry - a map uop has ~1500 of them and the walk ran per tile read.
    /// </summary>
    private readonly int[] _cumulativeLengths;

    private readonly UOPEntry[] _entries;
    private readonly int _length;
    private readonly BinaryReader _reader;

    public UOPIndex( Stream stream )
    {
        _reader = new BinaryReader( stream );
        _length = (int) stream.Length;

        if ( _reader.ReadInt32() != 0x50594D )
        {
            throw new ArgumentException( "Invalid UOP file." );
        }

        Version = _reader.ReadInt32();
        _reader.ReadInt32();
        int nextTable = _reader.ReadInt32();

        List<UOPEntry> entries = [];

        do
        {
            stream.Seek( nextTable, SeekOrigin.Begin );
            int count = _reader.ReadInt32();
            nextTable = _reader.ReadInt32();
            _reader.ReadInt32();

            for ( int i = 0; i < count; ++i )
            {
                int offset = _reader.ReadInt32();

                if ( offset == 0 )
                {
                    stream.Seek( 30, SeekOrigin.Current );
                    continue;
                }

                _reader.ReadInt64();
                int length = _reader.ReadInt32();

                entries.Add( new UOPEntry( offset, length ) );

                stream.Seek( 18, SeekOrigin.Current );
            }
        }
        while ( nextTable != 0 && nextTable < _length );

        entries.Sort( OffsetComparer.Instance );

        foreach ( UOPEntry t in entries )
        {
            stream.Seek( t.Offset + 2, SeekOrigin.Begin );

            int dataOffset = _reader.ReadInt16();
            t.Offset += 4 + dataOffset;

            stream.Seek( dataOffset, SeekOrigin.Current );
            t.Order = _reader.ReadInt32();
        }

        entries.Sort();

        // A zero length entry occupies no space in the flattened stream, so the linear walk this
        // replaced could never return one. Dropping them keeps the cumulative totals strictly
        // increasing, which is what makes the binary search below unambiguous.
        _entries = [.. entries.Where( e => e.Length > 0 )];

        _cumulativeLengths = new int[_entries.Length];
        int cumulative = 0;

        for ( int i = 0; i < _entries.Length; i++ )
        {
            cumulative += _entries[i].Length;
            _cumulativeLengths[i] = cumulative;
        }
    }

    public int Version { get; }

    public int Lookup( int offset )
    {
        int index = Array.BinarySearch( _cumulativeLengths, offset );

        if ( index < 0 )
        {
            // Not an exact end boundary: the complement is the first entry that ends past offset.
            index = ~index;
        }
        else
        {
            // Exactly on an entry's end, which belongs to the next one.
            index++;
        }

        if ( index >= _entries.Length )
        {
            return _length;
        }

        int entryStart = index > 0 ? _cumulativeLengths[index - 1] : 0;

        return _entries[index].Offset + ( offset - entryStart );
    }

    /// <summary>
    ///     Reads every entry into one buffer, i.e. the file as the client sees it once the uop container
    ///     is stripped. Callers that would otherwise <see cref="Lookup" /> their way through the whole
    ///     file can index the result directly.
    /// </summary>
    public byte[] ReadAll()
    {
        int totalLength = _cumulativeLengths.Length > 0 ? _cumulativeLengths[^1] : 0;

        byte[] result = new byte[totalLength];
        int position = 0;

        foreach ( UOPEntry entry in _entries )
        {
            _reader.BaseStream.Seek( entry.Offset, SeekOrigin.Begin );
            _reader.BaseStream.ReadExactly( result, position, entry.Length );
            position += entry.Length;
        }

        return result;
    }

    public void Close()
    {
        _reader.Close();
    }

    private class OffsetComparer : IComparer<UOPEntry>
    {
        public static readonly IComparer<UOPEntry> Instance = new OffsetComparer();

        public int Compare( UOPEntry x, UOPEntry y )
        {
            if ( x == null || y == null )
            {
                return -1;
            }

            return x.Offset.CompareTo( y.Offset );
        }
    }

    private class UOPEntry : IComparable<UOPEntry>
    {
        public readonly int Length;
        public int Offset;
        public int Order;

        public UOPEntry( int offset, int length )
        {
            Offset = offset;
            Length = length;
            Order = 0;
        }

        public int CompareTo( UOPEntry other )
        {
            return Order.CompareTo( other.Order );
        }
    }

    #region IDisposable Support

    private bool disposedValue; // To detect redundant calls

    protected virtual void Dispose( bool disposing )
    {
        if ( disposedValue )
        {
            return;
        }

        if ( disposing )
        {
            _reader.Dispose();
        }

        disposedValue = true;
    }

    public void Dispose()
    {
        Dispose( true );
        GC.SuppressFinalize( this );
    }

    #endregion
}