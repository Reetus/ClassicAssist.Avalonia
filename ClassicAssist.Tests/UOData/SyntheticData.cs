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
using System.Reflection;
using System.Text;
using ClassicAssist.UO.Data;

namespace ClassicAssist.Tests.UOData
{
    /// <summary>
    ///     A scratch directory under the temp path, deleted (best effort) on dispose.
    /// </summary>
    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "ca-tests-" + Guid.NewGuid().ToString( "N" ) );
            Directory.CreateDirectory( Path );
        }

        public string Path { get; }

        public string File( string name )
        {
            return System.IO.Path.Combine( Path, name );
        }

        public string Write( string name, byte[] bytes )
        {
            string path = File( name );
            System.IO.File.WriteAllBytes( path, bytes );

            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete( Path, true );
            }
            catch ( IOException )
            {
                // Something still has a file open; the temp directory is cleaned up eventually anyway.
            }
            catch ( UnauthorizedAccessException )
            {
            }
        }
    }

    /// <summary>
    ///     One file inside a synthetic .uop container.
    /// </summary>
    internal sealed class UopFile
    {
        public UopFile( ulong hash, byte[] data )
        {
            Hash = hash;
            Data = data;
        }

        public byte[] Data { get; }
        public ulong Hash { get; }

        /// <summary>Written as an unused table slot (data address 0) instead of a real entry.</summary>
        public bool EmptySlot { get; set; }
    }

    /// <summary>
    ///     Writes a Mythic package (.uop) the way the client's files are laid out:
    ///     <code>
    ///         header:  int magic 'MYP\0', int version, int signature, long firstTable, int blockSize, int count
    ///         table:   int count, long nextTable, count x entry
    ///         entry:   long dataAddress, int headerLength, int compressedLength, int decompressedLength,
    ///                  ulong hash, uint crc, short compressionFlag      (34 bytes)
    ///         data:    short flag, short metadataLength, metadata, file bytes
    ///     </code>
    ///     Data blocks are written in the reverse of table order and the tables go after them, so a reader that
    ///     relied on either the table order or on the first table following the header would read garbage.
    /// </summary>
    internal static class UopWriter
    {
        public const int Magic = 0x50594D;
        private const int EntrySize = 34;
        private const int HeaderSize = 28;
        private const int MetadataLength = 8;

        public static byte[] Build( IList<UopFile> files, int entriesPerTable, int version = 5 )
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter( stream );

            writer.Write( Magic );
            writer.Write( version );
            writer.Write( unchecked( (int) 0xFD23EC43 ) );
            writer.Write( 0L ); // first table, patched below
            writer.Write( entriesPerTable );
            writer.Write( files.Count );

            long[] addresses = new long[files.Count];

            for ( int i = files.Count - 1; i >= 0; i-- )
            {
                if ( files[i].EmptySlot )
                {
                    continue;
                }

                addresses[i] = stream.Position;
                writer.Write( (short) 3 );
                writer.Write( (short) MetadataLength );
                writer.Write( new byte[MetadataLength] );
                writer.Write( files[i].Data );
            }

            List<long> tableStarts = new List<long>();
            List<long> nextPointers = new List<long>();

            for ( int start = 0; start < files.Count || start == 0; start += entriesPerTable )
            {
                int count = Math.Min( entriesPerTable, files.Count - start );

                tableStarts.Add( stream.Position );
                writer.Write( count );
                nextPointers.Add( stream.Position );
                writer.Write( 0L );

                for ( int i = start; i < start + count; i++ )
                {
                    UopFile file = files[i];

                    if ( file.EmptySlot )
                    {
                        writer.Write( new byte[EntrySize] );
                        continue;
                    }

                    writer.Write( addresses[i] );
                    writer.Write( 4 + MetadataLength );
                    writer.Write( file.Data.Length );
                    writer.Write( file.Data.Length );
                    writer.Write( file.Hash );
                    writer.Write( 0u );
                    writer.Write( (short) 0 );
                }

                if ( files.Count == 0 )
                {
                    break;
                }
            }

            stream.Position = 12;
            writer.Write( tableStarts[0] );

            for ( int i = 0; i < nextPointers.Count - 1; i++ )
            {
                stream.Position = nextPointers[i];
                writer.Write( tableStarts[i + 1] );
            }

            writer.Flush();

            return stream.ToArray();
        }

        /// <summary>Total size of the header, for tests that need to corrupt it.</summary>
        public static int Size => HeaderSize;
    }

    /// <summary>
    ///     The package file-name hash: Bob Jenkins' lookup3 <c>hashlittle2</c> with both seeds zero, returned
    ///     as <c>(b &lt;&lt; 32) | c</c>. Written from the reference C rather than from the product's copy so
    ///     the two can be checked against each other.
    /// </summary>
    internal static class Lookup3
    {
        public static ulong Hash( string text )
        {
            byte[] k = Encoding.ASCII.GetBytes( text );
            int length = k.Length;
            uint a, b, c;
            a = b = c = 0xDEADBEEF + (uint) length;

            int offset = 0;

            while ( length > 12 )
            {
                a += ReadWord( k, offset );
                b += ReadWord( k, offset + 4 );
                c += ReadWord( k, offset + 8 );

                a -= c; a ^= Rot( c, 4 ); c += b;
                b -= a; b ^= Rot( a, 6 ); a += c;
                c -= b; c ^= Rot( b, 8 ); b += a;
                a -= c; a ^= Rot( c, 16 ); c += b;
                b -= a; b ^= Rot( a, 19 ); a += c;
                c -= b; c ^= Rot( b, 4 ); b += a;

                length -= 12;
                offset += 12;
            }

            if ( length == 0 )
            {
                return ( (ulong) b << 32 ) | c;
            }

            byte[] tail = new byte[12];
            Array.Copy( k, offset, tail, 0, length );

            a += ReadWord( tail, 0 );
            b += ReadWord( tail, 4 );
            c += ReadWord( tail, 8 );

            c ^= b; c -= Rot( b, 14 );
            a ^= c; a -= Rot( c, 11 );
            b ^= a; b -= Rot( a, 25 );
            c ^= b; c -= Rot( b, 16 );
            a ^= c; a -= Rot( c, 4 );
            b ^= a; b -= Rot( a, 14 );
            c ^= b; c -= Rot( b, 24 );

            return ( (ulong) b << 32 ) | c;
        }

        private static uint ReadWord( byte[] bytes, int offset )
        {
            return (uint) ( bytes[offset] | ( bytes[offset + 1] << 8 ) | ( bytes[offset + 2] << 16 ) |
                            ( bytes[offset + 3] << 24 ) );
        }

        private static uint Rot( uint x, int k )
        {
            return ( x << k ) | ( x >> ( 32 - k ) );
        }
    }

    internal struct LandTileSpec
    {
        public long Flags;
        public short TextureId;
        public string Name;
    }

    internal struct StaticTileSpec
    {
        public long Flags;
        public byte Weight;
        public byte Quality;
        public byte Quantity;
        public byte Height;
        public string Name;
    }

    /// <summary>
    ///     Writes tiledata.mul in either layout. Both are 512 land blocks then static blocks, each block a
    ///     4 byte header and 32 records:
    ///     <list type="bullet">
    ///         <item>old (pre 7.0.9): 32 bit flags; land 26 bytes, static 37 bytes, 0x4000 statics</item>
    ///         <item>new: 64 bit flags; land 30 bytes, static 41 bytes, 0x10000 statics</item>
    ///     </list>
    ///     The reader tells them apart by land tile 1's name, which in the old layout is "VOID!!!!!!" at
    ///     offset 36, so the old layout writes that name there unless the test sets tile 1 itself.
    /// </summary>
    internal sealed class TileDataBuilder
    {
        public Dictionary<int, LandTileSpec> Land { get; } = new Dictionary<int, LandTileSpec>();
        public Dictionary<int, StaticTileSpec> Statics { get; } = new Dictionary<int, StaticTileSpec>();

        public byte[] Build( bool oldFormat )
        {
            int landSize = oldFormat ? 26 : 30;
            int staticSize = oldFormat ? 37 : 41;
            int staticCount = oldFormat ? 0x4000 : 0x10000;
            int landBytes = 512 * ( 4 + 32 * landSize );
            int total = landBytes + staticCount / 32 * ( 4 + 32 * staticSize );

            byte[] bytes = new byte[total];

            Dictionary<int, LandTileSpec> land = new Dictionary<int, LandTileSpec>( Land );

            if ( oldFormat && !land.ContainsKey( 1 ) )
            {
                land[1] = new LandTileSpec { Name = "VOID!!!!!!" };
            }

            foreach ( KeyValuePair<int, LandTileSpec> pair in land )
            {
                int p = ( pair.Key / 32 + 1 ) * 4 + pair.Key * landSize;

                if ( oldFormat )
                {
                    WriteInt32( bytes, p, (int) pair.Value.Flags );
                    p += 4;
                }
                else
                {
                    WriteInt64( bytes, p, pair.Value.Flags );
                    p += 8;
                }

                WriteInt16( bytes, p, pair.Value.TextureId );
                WriteName( bytes, p + 2, pair.Value.Name );
            }

            foreach ( KeyValuePair<int, StaticTileSpec> pair in Statics )
            {
                int p = landBytes + ( pair.Key / 32 + 1 ) * 4 + pair.Key * staticSize;
                StaticTileSpec spec = pair.Value;

                if ( oldFormat )
                {
                    WriteInt32( bytes, p, (int) spec.Flags );
                    bytes[p + 4] = spec.Weight;
                    bytes[p + 5] = spec.Quality;
                    bytes[p + 9] = spec.Quantity;
                    bytes[p + 16] = spec.Height;
                    WriteName( bytes, p + 17, spec.Name );
                }
                else
                {
                    WriteInt64( bytes, p, spec.Flags );
                    bytes[p + 8] = spec.Weight;
                    bytes[p + 9] = spec.Quality;
                    bytes[p + 13] = spec.Quantity;
                    bytes[p + 20] = spec.Height;
                    WriteName( bytes, p + 21, spec.Name );
                }
            }

            return bytes;
        }

        private static void WriteName( byte[] bytes, int offset, string name )
        {
            if ( name == null )
            {
                return;
            }

            byte[] ascii = Encoding.ASCII.GetBytes( name );
            Array.Copy( ascii, 0, bytes, offset, Math.Min( 20, ascii.Length ) );
        }

        internal static void WriteInt16( byte[] bytes, int offset, short value )
        {
            BitConverter.GetBytes( value ).CopyTo( bytes, offset );
        }

        internal static void WriteInt32( byte[] bytes, int offset, int value )
        {
            BitConverter.GetBytes( value ).CopyTo( bytes, offset );
        }

        internal static void WriteInt64( byte[] bytes, int offset, long value )
        {
            BitConverter.GetBytes( value ).CopyTo( bytes, offset );
        }
    }

    /// <summary>
    ///     Points <see cref="Lazy{T}.Value" /> at a substitute for as long as it lives, then puts the original
    ///     state back - created or not.
    ///     <para>
    ///         Needed for <see cref="TileData" />, whose tables are <c>static readonly Lazy</c> fields: once
    ///         anything in the process has read them (and with no client data that is the single "unknown"
    ///         tile) <see cref="TileData.Initialize" /> can no longer change them. The seam the product would
    ///         need instead is an Initialize that recreates the Lazy, as <see cref="MapInfo" /> and
    ///         <see cref="Statics" /> do. This relies on the BCL's private Lazy fields and fails loudly if
    ///         they change.
    ///     </para>
    /// </summary>
    internal sealed class LazyOverride<T> : IDisposable
    {
        private static readonly FieldInfo _stateField = Field( "_state" );
        private static readonly FieldInfo _factoryField = Field( "_factory" );
        private static readonly FieldInfo _valueField = Field( "_value" );

        private readonly Lazy<T> _lazy;
        private readonly object _state;
        private readonly object _factory;
        private readonly object _value;

        public LazyOverride( Lazy<T> lazy, T value )
        {
            _lazy = lazy;
            _state = _stateField.GetValue( lazy );
            _factory = _factoryField.GetValue( lazy );
            _value = _valueField.GetValue( lazy );

            _valueField.SetValue( lazy, value );
            _stateField.SetValue( lazy, null );
        }

        public void Dispose()
        {
            _valueField.SetValue( _lazy, _value );
            _factoryField.SetValue( _lazy, _factory );
            _stateField.SetValue( _lazy, _state );
        }

        private static FieldInfo Field( string name )
        {
            return typeof( Lazy<T> ).GetField( name, BindingFlags.Instance | BindingFlags.NonPublic ) ??
                   throw new InvalidOperationException( $"Lazy<T>.{name} not found; LazyOverride needs updating." );
        }
    }

    /// <summary>
    ///     Reflection access to the private statics of the data readers, so tests can load from a temp
    ///     directory and put the process-wide state back afterwards.
    /// </summary>
    internal static class DataReaderState
    {
        public static object GetStatic( Type type, string name )
        {
            return StaticField( type, name ).GetValue( null );
        }

        public static void SetStatic( Type type, string name, object value )
        {
            StaticField( type, name ).SetValue( null, value );
        }

        public static object InvokeStatic( Type type, string name, params object[] args )
        {
            MethodInfo method = type.GetMethod( name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public ) ??
                                throw new InvalidOperationException( $"{type.Name}.{name} not found" );

            try
            {
                return method.Invoke( null, args );
            }
            catch ( TargetInvocationException e ) when ( e.InnerException != null )
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture( e.InnerException ).Throw();

                throw;
            }
        }

        private static FieldInfo StaticField( Type type, string name )
        {
            return type.GetField( name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public ) ??
                   throw new InvalidOperationException( $"{type.Name}.{name} not found" );
        }
    }

    /// <summary>
    ///     Loads a synthetic tiledata.mul through <see cref="TileData" />'s own parsers and makes
    ///     <see cref="TileData.GetLandTile" /> / <see cref="TileData.GetStaticTile" /> return it until disposed.
    /// </summary>
    internal sealed class TileDataScope : IDisposable
    {
        private readonly LazyOverride<LandTile[]> _land;
        private readonly LazyOverride<StaticTile[]> _statics;

        public TileDataScope( TileDataBuilder builder, bool oldFormat = false )
        {
            using TempDirectory directory = new TempDirectory();

            directory.Write( "tiledata.mul", builder.Build( oldFormat ) );

            Load( directory.Path, out LandTile[] land, out StaticTile[] statics );

            Land = land;
            Statics = statics;

            _land = new LazyOverride<LandTile[]>(
                (Lazy<LandTile[]>) DataReaderState.GetStatic( typeof( TileData ), "_landTiles" ), land );
            _statics = new LazyOverride<StaticTile[]>(
                (Lazy<StaticTile[]>) DataReaderState.GetStatic( typeof( TileData ), "_staticTiles" ), statics );
        }

        public LandTile[] Land { get; }
        public StaticTile[] Statics { get; }

        /// <summary>
        ///     Runs the private loaders against <paramref name="path" />, leaving the reader's path and format
        ///     flag as they were.
        /// </summary>
        public static void Load( string path, out LandTile[] land, out StaticTile[] statics )
        {
            object originalPath = DataReaderState.GetStatic( typeof( TileData ), "_dataPath" );
            object originalFormat = DataReaderState.GetStatic( typeof( TileData ), "_oldFormat" );

            try
            {
                TileData.Initialize( path );

                // The format flag is only ever set, never cleared, so start each load from "new".
                DataReaderState.SetStatic( typeof( TileData ), "_oldFormat", false );
                statics = (StaticTile[]) DataReaderState.InvokeStatic( typeof( TileData ), "LoadStaticTiles" );

                DataReaderState.SetStatic( typeof( TileData ), "_oldFormat", false );
                land = (LandTile[]) DataReaderState.InvokeStatic( typeof( TileData ), "LoadLandTiles" );
            }
            finally
            {
                DataReaderState.SetStatic( typeof( TileData ), "_dataPath", originalPath );
                DataReaderState.SetStatic( typeof( TileData ), "_oldFormat", originalFormat );
            }
        }

        public void Dispose()
        {
            _statics.Dispose();
            _land.Dispose();
        }
    }

    internal static class Bytes
    {
        public static byte[] Concat( params byte[][] parts )
        {
            return parts.SelectMany( p => p ).ToArray();
        }
    }
}
