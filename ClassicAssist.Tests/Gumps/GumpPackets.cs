using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using ClassicAssist.UO;
using ClassicAssist.UO.Data;

namespace ClassicAssist.Tests.Gumps
{
    /// <summary>A generic gump packet (0xB0) taken apart field by field.</summary>
    internal sealed class DecodedGump
    {
        public int DeclaredLength { get; set; }
        public uint GumpId { get; set; }
        public int LayoutLength { get; set; }
        public string Layout { get; set; }
        public int Serial { get; set; }
        public string[] Strings { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    /// <summary>
    ///     Encodes and decodes the gump packets the tests feed in or capture, independently of the
    ///     product code, straight from the protocol layout.
    /// </summary>
    internal static class GumpPackets
    {
        /// <summary>
        ///     0xB0: id, u16 length, i32 serial, u32 gump id, i32 x, i32 y, u16 layout length, ASCII layout,
        ///     u16 line count, then per line u16 length (characters) and big-endian UTF-16 text.
        /// </summary>
        public static DecodedGump DecodeGeneric( byte[] packet )
        {
            if ( packet[0] != 0xB0 )
            {
                throw new ArgumentException( $"Not a 0xB0 packet: 0x{packet[0]:X2}" );
            }

            DecodedGump result = new DecodedGump
            {
                DeclaredLength = BinaryPrimitives.ReadUInt16BigEndian( packet.AsSpan( 1 ) ),
                Serial = BinaryPrimitives.ReadInt32BigEndian( packet.AsSpan( 3 ) ),
                GumpId = BinaryPrimitives.ReadUInt32BigEndian( packet.AsSpan( 7 ) ),
                X = BinaryPrimitives.ReadInt32BigEndian( packet.AsSpan( 11 ) ),
                Y = BinaryPrimitives.ReadInt32BigEndian( packet.AsSpan( 15 ) ),
                LayoutLength = BinaryPrimitives.ReadUInt16BigEndian( packet.AsSpan( 19 ) )
            };

            int pos = 21;

            result.Layout = Encoding.ASCII.GetString( packet, pos, result.LayoutLength );
            pos += result.LayoutLength;

            int count = BinaryPrimitives.ReadUInt16BigEndian( packet.AsSpan( pos ) );
            pos += 2;

            result.Strings = new string[count];

            for ( int i = 0; i < count; i++ )
            {
                int length = BinaryPrimitives.ReadUInt16BigEndian( packet.AsSpan( pos ) );
                pos += 2;

                result.Strings[i] = Encoding.BigEndianUnicode.GetString( packet, pos, length * 2 );
                pos += length * 2;
            }

            if ( pos != packet.Length )
            {
                throw new InvalidOperationException( $"Decoded {pos} bytes of a {packet.Length} byte packet" );
            }

            return result;
        }

        /// <summary>Builds a 0xB0 packet from a layout and text lines.</summary>
        public static byte[] BuildGeneric( int serial, uint gumpId, int x, int y, string layout, params string[] lines )
        {
            PacketWriter writer = new PacketWriter();
            writer.Write( (byte) 0xB0 );
            writer.Write( (short) 0 );
            writer.Write( serial );
            writer.Write( gumpId );
            writer.Write( x );
            writer.Write( y );
            writer.Write( (ushort) layout.Length );
            byte[] layoutBytes = Encoding.ASCII.GetBytes( layout );
            writer.Write( layoutBytes, 0, layoutBytes.Length );
            writer.Write( (ushort) lines.Length );

            foreach ( string line in lines )
            {
                writer.Write( (ushort) line.Length );
                byte[] bytes = Encoding.BigEndianUnicode.GetBytes( line );
                writer.Write( bytes, 0, bytes.Length );
            }

            byte[] packet = writer.ToArray();
            BinaryPrimitives.WriteUInt16BigEndian( packet.AsSpan( 1 ), (ushort) packet.Length );

            return packet;
        }

        /// <summary>
        ///     0xDD: id, u16 length, i32 serial, u32 gump id, i32 x, i32 y, then the layout as
        ///     (u32 compressed length + 4, u32 decompressed length, zlib bytes), u32 line count, and the
        ///     lines - each u16 length (characters) + big-endian UTF-16 - compressed the same way.
        /// </summary>
        public static byte[] BuildCompressed( int serial, uint gumpId, int x, int y, string layout,
            params string[] lines )
        {
            byte[] layoutBytes = Encoding.ASCII.GetBytes( layout + "\0" );
            byte[] layoutCompressed = Compression.Compress( layoutBytes );

            List<byte> text = new List<byte>();

            foreach ( string line in lines )
            {
                text.Add( (byte) ( line.Length >> 8 ) );
                text.Add( (byte) line.Length );
                text.AddRange( Encoding.BigEndianUnicode.GetBytes( line ) );
            }

            PacketWriter writer = new PacketWriter();
            writer.Write( (byte) 0xDD );
            writer.Write( (short) 0 );
            writer.Write( serial );
            writer.Write( gumpId );
            writer.Write( x );
            writer.Write( y );
            writer.Write( layoutCompressed.Length + 4 );
            writer.Write( layoutBytes.Length );
            writer.Write( layoutCompressed, 0, layoutCompressed.Length );
            writer.Write( lines.Length );

            if ( text.Count > 0 )
            {
                byte[] textCompressed = Compression.Compress( text.ToArray() );

                writer.Write( textCompressed.Length + 4 );
                writer.Write( text.Count );
                writer.Write( textCompressed, 0, textCompressed.Length );
            }
            else
            {
                writer.Write( 0 );
            }

            byte[] packet = writer.ToArray();
            BinaryPrimitives.WriteUInt16BigEndian( packet.AsSpan( 1 ), (ushort) packet.Length );

            return packet;
        }

        /// <summary>
        ///     Builds the client's 0xB1 gump response as the client sends it: text entry lengths are in
        ///     characters.
        /// </summary>
        public static byte[] BuildResponse( int serial, uint gumpId, int buttonId, int[] switches = null,
            params (int Id, string Text)[] entries )
        {
            switches ??= Array.Empty<int>();

            PacketWriter writer = new PacketWriter();
            writer.Write( (byte) 0xB1 );
            writer.Write( (short) 0 );
            writer.Write( serial );
            writer.Write( gumpId );
            writer.Write( buttonId );
            writer.Write( switches.Length );

            foreach ( int @switch in switches )
            {
                writer.Write( @switch );
            }

            writer.Write( entries.Length );

            foreach ( (int id, string text) in entries )
            {
                writer.Write( (short) id );
                writer.Write( (short) text.Length );
                byte[] bytes = Encoding.BigEndianUnicode.GetBytes( text );
                writer.Write( bytes, 0, bytes.Length );
            }

            byte[] packet = writer.ToArray();
            BinaryPrimitives.WriteUInt16BigEndian( packet.AsSpan( 1 ), (ushort) packet.Length );

            return packet;
        }
    }
}
