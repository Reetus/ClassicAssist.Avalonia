using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace ClassicAssist.UO.Data;

public static class TileData
{
    private static string _dataPath;
    private static readonly Lazy<LandTile[]> _landTiles = new( LoadLandTiles );
    private static readonly Lazy<StaticTile[]> _staticTiles = new( LoadStaticTiles );

    private static bool _oldFormat;

    public static void Initialize( string dataPath )
    {
        _dataPath = dataPath;
    }

    /// <summary>
    ///     Reads a fixed width, NUL padded ASCII name. Stops at the first NUL rather than trimming the
    ///     tail, so an interior NUL truncates instead of being kept in the middle of the string.
    /// </summary>
    private static string ReadName( ReadOnlySpan<byte> span )
    {
        int nul = span.IndexOf( (byte) 0 );
        ReadOnlySpan<byte> slice = nul < 0 ? span : span[..nul];

        return slice.IsEmpty ? string.Empty : Encoding.ASCII.GetString( slice );
    }

    private static StaticTile[] LoadStaticTiles()
    {
        // No client data configured at all - as opposed to a path that is set but has no
        // tiledata.mul, which is a real misconfiguration and still throws below. Upstream returns
        // a tile named "unknown" here and comments that its tests depend on it; the port dropped
        // that, which left anything reading Item.Name (autoloot's "Autolooting {0}" message, for
        // one) unable to run without a UO install. The single-element array gives the same answer
        // through GetStaticTile's existing range check, without reintroducing the try/catch that
        // stopped it inlining. Only the static tiles get this - upstream's GetLandTile throws with
        // no data too, so LoadLandTiles is deliberately left alone.
        if ( string.IsNullOrEmpty( _dataPath ) || !Directory.Exists( _dataPath ) )
        {
            return [new StaticTile { Name = "unknown" }];
        }

        string fileName = Path.Combine( _dataPath, "tiledata.mul" );

        if ( !File.Exists( fileName ) )
        {
            throw new FileNotFoundException( "File not found.", fileName );
        }

        byte[] fileBytes = File.ReadAllBytes( fileName );
        ReadOnlySpan<byte> span = fileBytes;

        StaticTile[] staticTiles = new StaticTile[( fileBytes.Length - 428032 ) / 1188 * 32];

        // The format marker lives at offset 36, 20 bytes.
        if ( ReadName( span.Slice( 36, 20 ) ) == "VOID!!!!!!" )
        {
            _oldFormat = true;
        }

        if ( _oldFormat )
        {
            int p = 428032;

            for ( int i = 0; i < 0x4000; ++i )
            {
                if ( ( i & 0x1F ) == 0 )
                {
                    p += 4; // block header
                }

                staticTiles[i].ID = (ushort) i;
                staticTiles[i].Flags = (TileFlags) BinaryPrimitives.ReadInt32LittleEndian( span.Slice( p, 4 ) );
                p += 4;
                staticTiles[i].Weight = span[p++];
                staticTiles[i].Quality = span[p++];
                p += 2; // unknown int16
                p += 1; // unknown byte
                staticTiles[i].Quantity = span[p++];
                p += 2; // unknown int16
                p += 1; // unknown byte
                p += 1; // unknown byte
                p += 2; // unknown int16
                staticTiles[i].Height = span[p++];
                staticTiles[i].Name = ReadName( span.Slice( p, 20 ) );
                p += 20;
            }
        }
        else
        {
            int p = 493568;

            for ( int i = 0; i < 0x10000; ++i )
            {
                if ( ( i & 0x1F ) == 0 )
                {
                    p += 4; // block header
                }

                staticTiles[i].ID = (ushort) i;
                staticTiles[i].Flags = (TileFlags) BinaryPrimitives.ReadInt64LittleEndian( span.Slice( p, 8 ) );
                p += 8;
                staticTiles[i].Weight = span[p++];
                staticTiles[i].Quality = span[p++];
                p += 2; // unknown int16
                p += 1; // unknown byte
                staticTiles[i].Quantity = span[p++];
                p += 4; // unknown int32
                p += 1; // unknown byte
                p += 1; // unknown byte
                staticTiles[i].Height = span[p++];
                staticTiles[i].Name = ReadName( span.Slice( p, 20 ) );
                p += 20;
            }
        }

        return staticTiles;
    }

    private static LandTile[] LoadLandTiles()
    {
        string fileName = Path.Combine( _dataPath, "tiledata.mul" );

        if ( !File.Exists( fileName ) )
        {
            throw new FileNotFoundException( "File not found.", fileName );
        }

        byte[] fileBytes = File.ReadAllBytes( fileName );
        ReadOnlySpan<byte> span = fileBytes;

        LandTile[] landTiles = new LandTile[16384];

        if ( ReadName( span.Slice( 36, 20 ) ) == "VOID!!!!!!" )
        {
            _oldFormat = true;
        }

        int p = 0;

        if ( _oldFormat )
        {
            for ( int i = 0; i < 0x4000; ++i )
            {
                if ( i == 0 || i > 0 && ( i & 0x1f ) == 0 )
                {
                    p += 4; // block header
                }

                landTiles[i].Flags = (TileFlags) BinaryPrimitives.ReadInt32LittleEndian( span.Slice( p, 4 ) );
                p += 4;
                landTiles[i].ID = BinaryPrimitives.ReadInt16LittleEndian( span.Slice( p, 2 ) );
                p += 2;
                landTiles[i].Name = ReadName( span.Slice( p, 20 ) );
                p += 20;
            }
        }
        else
        {
            for ( int i = 0; i < 0x4000; ++i )
            {
                if ( i == 1 || i > 0 && ( i & 0x1f ) == 0 )
                {
                    p += 4; // block header
                }

                landTiles[i].Flags = (TileFlags) BinaryPrimitives.ReadInt64LittleEndian( span.Slice( p, 8 ) );
                p += 8;
                landTiles[i].ID = BinaryPrimitives.ReadInt16LittleEndian( span.Slice( p, 2 ) );
                p += 2;
                landTiles[i].Name = ReadName( span.Slice( p, 20 ) );
                p += 20;
            }
        }

        return landTiles;
    }

    public static LandTile GetLandTile( int index )
    {
        return _landTiles.Value[index];
    }

    /// <summary>
    ///     Out of range ids fall back to tile 0, same as before - but as a range check rather than a
    ///     caught IndexOutOfRangeException, since the try/catch stopped the JIT inlining what is one of
    ///     the hottest lookups in the assistant.
    /// </summary>
    public static StaticTile GetStaticTile( int index )
    {
        StaticTile[] tiles = _staticTiles.Value;

        return (uint) index < (uint) tiles.Length ? tiles[index] : tiles[0];
    }

    public static Layer GetLayer( int id )
    {
        StaticTile tileData = GetStaticTile( id );

        if ( !tileData.Flags.HasFlag( TileFlags.Wearable ) )
        {
            return Layer.Invalid;
        }

        return (Layer) tileData.Quality;
    }
}