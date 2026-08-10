using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ClassicAssist.Misc;

namespace ClassicAssist.UO.Data;

public static class Art
{
    private static string _dataPath;
    private static bool _isUOPFormat;
    private static Lazy<Dictionary<int, Entry3D>> _lazyIndex;

    public static bool Initialize( string dataPath )
    {
        _dataPath = dataPath;

        if ( File.Exists( Path.Combine( dataPath, "artLegacyMUL.uop" ) ) )
        {
            _isUOPFormat = true;
        }

        _lazyIndex = new Lazy<Dictionary<int, Entry3D>>( () => _isUOPFormat ? LoadUOPIndex() : LoadMULIndex() );

        return true;
    }

    private static Dictionary<int, Entry3D> LoadMULIndex()
    {
        int entrySize = Marshal.SizeOf( typeof( Entry3D ) );
        byte[] buffer = new byte[entrySize];
        GCHandle pinnedBuffer = GCHandle.Alloc( buffer, GCHandleType.Pinned );
        int index = 0;

        Dictionary<int, Entry3D> indexes = [];

        using ( FileStream reader = File.Open( Path.Combine( _dataPath, "artidx.mul" ), FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite ) )
        {
            int bytesRead;

            do
            {
                bytesRead = reader.Read( buffer, 0, entrySize );
                indexes[index++] =
                    (Entry3D) Marshal.PtrToStructure( pinnedBuffer.AddrOfPinnedObject(), typeof( Entry3D ) );
            }
            while ( bytesRead > 0 );
        }

        return indexes;
    }

    private static Dictionary<int, Entry3D> LoadUOPIndex()
    {
        Dictionary<ulong, int> hashes = [];
        Dictionary<int, Entry3D> indexes = [];

        using ( FileStream reader = File.Open( Path.Combine( _dataPath, "artLegacyMUL.uop" ), FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite ) )
        {
            using BinaryReader bin = new( reader );
            reader.Seek( 12, SeekOrigin.Current );
            int firstAddress = bin.ReadInt32();

            for ( int i = 0; i < 0x10000 /*formatHeader.NumberOfFiles*/; i++ )
            {
                string entryName = $"build/artlegacymul/{i:D8}.tga";
                ulong hash = HashFileName( entryName );

                if ( !hashes.ContainsKey( hash ) )
                {
                    hashes.Add( hash, i );
                }
            }

            long nextAddress = firstAddress;

            do
            {
                reader.Seek( nextAddress, SeekOrigin.Begin );
                UOPBlockHeader blockHeader = reader.ReadStruct<UOPBlockHeader>();

                nextAddress = blockHeader.NextAddress;

                for ( int i = 0; i < blockHeader.NumberOfFiles; i++ )
                {
                    UOPFileHeader fileHeader = reader.ReadStruct<UOPFileHeader>();

                    if ( fileHeader.DataHeaderAddress == 0 )
                    {
                        continue;
                    }

                    if ( hashes.ContainsKey( fileHeader.Hash ) )
                    {
                        int index = hashes[fileHeader.Hash];

                        indexes[index] = new Entry3D( (int) fileHeader.DataHeaderAddress + fileHeader.Length,
                            fileHeader.IsCompressed == 1
                                ? fileHeader.CompressedSize
                                : fileHeader.DecompressedSize, 0 );
                    }
                }
            }
            while ( nextAddress > 0 );
        }

        return indexes;
    }

    /// <summary>
    ///     Decodes a static item tile and applies a hue to it.
    /// </summary>
    /// <returns>The tile, or <see cref="Pixmap.Empty" /> if the item has no art.</returns>
    public static Pixmap GetStatic( int itemID, int hue )
    {
        ushort[] colours = DecodeStatic( itemID, out int width, out int height );

        if ( colours == null )
        {
            return Pixmap.Empty;
        }

        if ( hue != 0 )
        {
            StaticTile tileData = TileData.GetStaticTile( itemID );

            // Hue while the pixels are still colour words. Doing it after the widen to RGBA would mean
            // mapping 8-bit channels back to 5-bit to index the hue table, which is lossy.
            Hues.ApplyHue( hue, colours, tileData.Flags.HasFlag( TileFlags.PartialHue ) );
        }

        return HuesHelper.ToPixmap( colours, width, height );
    }

    /// <summary>
    ///     Decodes a static item tile.
    /// </summary>
    /// <returns>The tile, or <see cref="Pixmap.Empty" /> if the item has no art.</returns>
    public static Pixmap GetStatic( int itemId )
    {
        ushort[] colours = DecodeStatic( itemId, out int width, out int height );

        return colours == null ? Pixmap.Empty : HuesHelper.ToPixmap( colours, width, height );
    }

    /// <summary>
    ///     Reads one tile out of art.mul / artLegacyMUL.uop as ARGB1555 colour words.
    ///     <para>
    ///         The body is a run-length stream: per row, pairs of (x offset, run length) followed by that
    ///         many colour words, terminated when both are zero. Anything not covered by a run stays 0 and
    ///         reads as transparent.
    ///     </para>
    /// </summary>
    private static ushort[] DecodeStatic( int itemId, out int width, out int height )
    {
        width = 0;
        height = 0;

        itemId += 0x4000;

        string fileName = Path.Combine( _dataPath, _isUOPFormat ? "artLegacyMUL.uop" : "art.mul" );

        if ( !File.Exists( fileName ) )
        {
            return null;
        }


        if ( !_lazyIndex.Value.TryGetValue( itemId, out Entry3D entry ) && !_lazyIndex.Value.TryGetValue( 0x4000, out entry ) )
        {
            return null;
        }

        using FileStream artFile =
            File.Open( fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite );
        using BinaryReader reader = new( artFile );
        artFile.Seek( entry.Lookup, SeekOrigin.Begin );

        reader.ReadInt32();

        width = reader.ReadInt16();
        height = reader.ReadInt16();

        if ( width <= 0 || height <= 0 )
        {
            width = 0;
            height = 0;

            return null;
        }

        int[] lookups = new int[height];
        int start = (int) reader.BaseStream.Position + height * 2;

        for ( int i = 0; i < height; i++ )
        {
            lookups[i] = start + reader.ReadUInt16() * 2;
        }

        ushort[] pixels = new ushort[width * height];

        for ( int y = 0; y < height; y++ )
        {
            reader.BaseStream.Seek( lookups[y], SeekOrigin.Begin );

            int rowStart = y * width;
            int x = 0;

            int xOffset, xRun;

            while ( ( xOffset = reader.ReadUInt16() ) + ( xRun = reader.ReadUInt16() ) != 0 )
            {
                x += xOffset;

                // A corrupt or truncated entry can run off the end of the row; stop rather than
                // throw, so one bad tile does not take the caller down.
                if ( x < 0 || x + xRun > width )
                {
                    break;
                }

                for ( int i = 0; i < xRun; i++ )
                {
                    pixels[rowStart + x++] = (ushort) ( reader.ReadUInt16() ^ 0x8000 );
                }
            }
        }

        return pixels;
    }

    private static ulong HashFileName( string s )
    {
        uint ecx, edx, ebx, esi, edi;

        // Transliterated from the C original, which zeroes the registers as a block before use.
        // Four of them are overwritten immediately, so this reads as redundant - but keeping the
        // shape is what makes the function checkable line-by-line against the source it came from.
#pragma warning disable IDE0059
        uint eax = ecx = edx = ebx = esi = edi = 0;
#pragma warning restore IDE0059
        ebx = edi = esi = (uint) s.Length + 0xDEADBEEF;
        int i;

        for ( i = 0; i + 12 < s.Length; i += 12 )
        {
            edi = (uint) ( ( s[i + 7] << 24 ) | ( s[i + 6] << 16 ) | ( s[i + 5] << 8 ) | s[i + 4] ) + edi;
            esi = (uint) ( ( s[i + 11] << 24 ) | ( s[i + 10] << 16 ) | ( s[i + 9] << 8 ) | s[i + 8] ) + esi;
            edx = (uint) ( ( s[i + 3] << 24 ) | ( s[i + 2] << 16 ) | ( s[i + 1] << 8 ) | s[i] ) - esi;
            edx = ( edx + ebx ) ^ ( esi >> 28 ) ^ ( esi << 4 );
            esi += edi;
            edi = ( edi - edx ) ^ ( edx >> 26 ) ^ ( edx << 6 );
            edx += esi;
            esi = ( esi - edi ) ^ ( edi >> 24 ) ^ ( edi << 8 );
            edi += edx;
            ebx = ( edx - esi ) ^ ( esi >> 16 ) ^ ( esi << 16 );
            esi += edi;
            edi = ( edi - ebx ) ^ ( ebx >> 13 ) ^ ( ebx << 19 );
            ebx += esi;
            esi = ( esi - edi ) ^ ( edi >> 28 ) ^ ( edi << 4 );
            edi += ebx;
        }

        if ( s.Length - i > 0 )
        {
            switch ( s.Length - i )
            {
                case 12:
                    esi += (uint) s[i + 11] << 24;
                    goto case 11;
                case 11:
                    esi += (uint) s[i + 10] << 16;
                    goto case 10;
                case 10:
                    esi += (uint) s[i + 9] << 8;
                    goto case 9;
                case 9:
                    esi += s[i + 8];
                    goto case 8;
                case 8:
                    edi += (uint) s[i + 7] << 24;
                    goto case 7;
                case 7:
                    edi += (uint) s[i + 6] << 16;
                    goto case 6;
                case 6:
                    edi += (uint) s[i + 5] << 8;
                    goto case 5;
                case 5:
                    edi += s[i + 4];
                    goto case 4;
                case 4:
                    ebx += (uint) s[i + 3] << 24;
                    goto case 3;
                case 3:
                    ebx += (uint) s[i + 2] << 16;
                    goto case 2;
                case 2:
                    ebx += (uint) s[i + 1] << 8;
                    goto case 1;
                case 1:
                    ebx += s[i];
                    break;
            }

            esi = ( esi ^ edi ) - ( ( edi >> 18 ) ^ ( edi << 14 ) );
            ecx = ( esi ^ ebx ) - ( ( esi >> 21 ) ^ ( esi << 11 ) );
            edi = ( edi ^ ecx ) - ( ( ecx >> 7 ) ^ ( ecx << 25 ) );
            esi = ( esi ^ edi ) - ( ( edi >> 16 ) ^ ( edi << 16 ) );
            edx = ( esi ^ ecx ) - ( ( esi >> 28 ) ^ ( esi << 4 ) );
            edi = ( edi ^ edx ) - ( ( edx >> 18 ) ^ ( edx << 14 ) );
            eax = ( esi ^ edi ) - ( ( edi >> 8 ) ^ ( edi << 24 ) );
            return ( (ulong) edi << 32 ) | eax;
        }

        return ( (ulong) esi << 32 ) | eax;
    }

    #region Structures

    [StructLayout( LayoutKind.Explicit )]
    private struct Entry3D
    {
        [FieldOffset( 0 )]
        public readonly int Lookup;

        [FieldOffset( 4 )]
        public readonly int Length;

        [FieldOffset( 8 )]
        public readonly int Extra;

        public Entry3D( int lookup, int length, int extra )
        {
            Lookup = lookup;
            Length = length;
            Extra = extra;
        }
    }

    [StructLayout( LayoutKind.Explicit, Size = 12 )]
    private struct UOPBlockHeader
    {
        [FieldOffset( 0 )]
        public readonly int NumberOfFiles;

        [FieldOffset( 4 )]
        public readonly long NextAddress;
    }

    [StructLayout( LayoutKind.Explicit, Pack = 0, Size = 34 )]
    private struct UOPFileHeader
    {
        [FieldOffset( 0 )]
        public readonly long DataHeaderAddress;

        [FieldOffset( 8 )]
        public readonly int Length;

        [FieldOffset( 12 )]
        public readonly int CompressedSize;

        [FieldOffset( 16 )]
        public readonly int DecompressedSize;

        [FieldOffset( 20 )]
        public readonly ulong Hash;

        [FieldOffset( 28 )]
        public readonly int Unknown;

        [FieldOffset( 32 )]
        public readonly short IsCompressed;
    }

    #endregion
}