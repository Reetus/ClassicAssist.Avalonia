/***************************************************************************
 *                              PacketWriter.cs
 *                            -------------------
 *   begin                : May 1, 2002
 *   copyright            : (C) The RunUO Software Team
 *   email                : info@runuo.com
 *
 *   $Id$
 *
 ***************************************************************************/

/***************************************************************************
 *
 *   This program is free software; you can redistribute it and/or modify
 *   it under the terms of the GNU General Public License as published by
 *   the Free Software Foundation; either version 2 of the License, or
 *   (at your option) any later version.
 *
 ***************************************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ClassicAssist.UO.Data;

/// <summary>
///     Provides functionality for writing primitive binary data.
/// </summary>
public class PacketWriter : IDisposable
{
    private static readonly Stack<PacketWriter> Pool = new();

    /// <summary>
    ///     Internal format buffer.
    /// </summary>
    private readonly byte[] _buffer = new byte[4];
    private int m_Capacity;

    /// <summary>
    ///     Instantiates a new PacketWriter instance with the default capacity of 4 bytes.
    /// </summary>
    public PacketWriter() : this( 32 )
    {
    }

    /// <summary>
    ///     Instantiates a new PacketWriter instance with a given capacity.
    /// </summary>
    /// <param name="capacity">Initial capacity for the internal stream.</param>
    public PacketWriter( int capacity )
    {
        UnderlyingStream = new MemoryStream( capacity );
        m_Capacity = capacity;
    }

    /// <summary>
    ///     Gets the total stream length.
    /// </summary>
    public long Length => UnderlyingStream.Length;

    /// <summary>
    ///     Gets or sets the current stream position.
    /// </summary>
    public long Position
    {
        get => UnderlyingStream.Position;
        set => UnderlyingStream.Position = value;
    }

    /// <summary>
    ///     The internal stream used by this PacketWriter instance.
    /// </summary>
    public MemoryStream UnderlyingStream { get; }

    public void Dispose()
    {
        Dispose( true );
        GC.SuppressFinalize( this );
    }

    public static PacketWriter CreateInstance()
    {
        return CreateInstance( 32 );
    }

    public static PacketWriter CreateInstance( int capacity )
    {
        PacketWriter pw = null;

        lock ( Pool )
        {
            if ( Pool.Count > 0 )
            {
                pw = Pool.Pop();

                if ( pw != null )
                {
                    pw.m_Capacity = capacity;
                    pw.UnderlyingStream.SetLength( 0 );
                }
            }
        }

        return pw ?? new PacketWriter( capacity );
    }

    public static void ReleaseInstance( PacketWriter pw )
    {
        lock ( Pool )
        {
            if ( !Pool.Contains( pw ) )
            {
                Pool.Push( pw );
            }
            else
            {
                try
                {
                    using StreamWriter op = new( "neterr.log" );
                    op.WriteLine( "{0}\tInstance pool contains writer", DateTime.UtcNow );
                }
                catch
                {
                    Console.WriteLine( @"net error" );
                }
            }
        }
    }

    /// <summary>
    ///     Writes a 1-byte boolean value to the underlying stream. False is represented by 0, true by 1.
    /// </summary>
    public void Write( bool value )
    {
        UnderlyingStream.WriteByte( (byte) ( value ? 1 : 0 ) );
    }

    /// <summary>
    ///     Writes a 1-byte unsigned integer value to the underlying stream.
    /// </summary>
    public void Write( byte value )
    {
        UnderlyingStream.WriteByte( value );
    }

    /// <summary>
    ///     Writes a 1-byte signed integer value to the underlying stream.
    /// </summary>
    public void Write( sbyte value )
    {
        UnderlyingStream.WriteByte( (byte) value );
    }

    /// <summary>
    ///     Writes a 2-byte signed integer value to the underlying stream.
    /// </summary>
    public void Write( short value )
    {
        _buffer[0] = (byte) ( value >> 8 );
        _buffer[1] = (byte) value;

        UnderlyingStream.Write( _buffer, 0, 2 );
    }

    /// <summary>
    ///     Writes a 2-byte unsigned integer value to the underlying stream.
    /// </summary>
    public void Write( ushort value )
    {
        _buffer[0] = (byte) ( value >> 8 );
        _buffer[1] = (byte) value;

        UnderlyingStream.Write( _buffer, 0, 2 );
    }

    /// <summary>
    ///     Writes a 4-byte signed integer value to the underlying stream.
    /// </summary>
    public void Write( int value )
    {
        _buffer[0] = (byte) ( value >> 24 );
        _buffer[1] = (byte) ( value >> 16 );
        _buffer[2] = (byte) ( value >> 8 );
        _buffer[3] = (byte) value;

        UnderlyingStream.Write( _buffer, 0, 4 );
    }

    /// <summary>
    ///     Writes a 4-byte unsigned integer value to the underlying stream.
    /// </summary>
    public void Write( uint value )
    {
        _buffer[0] = (byte) ( value >> 24 );
        _buffer[1] = (byte) ( value >> 16 );
        _buffer[2] = (byte) ( value >> 8 );
        _buffer[3] = (byte) value;

        UnderlyingStream.Write( _buffer, 0, 4 );
    }

    /// <summary>
    ///     Writes a sequence of bytes to the underlying stream
    /// </summary>
    public void Write( byte[] buffer, int offset, int size )
    {
        UnderlyingStream.Write( buffer, offset, size );
    }

    /// <summary>
    ///     Writes a fixed-length ASCII-encoded string value to the underlying stream. To fit (size), the string content is
    ///     either truncated or padded with null characters.
    /// </summary>
    public void WriteAsciiFixed( string value, int size )
    {
        if ( value == null )
        {
            Console.WriteLine( @"Network: Attempted to WriteAsciiFixed() with null value" );
            value = string.Empty;
        }

        int length = value.Length;

        UnderlyingStream.SetLength( UnderlyingStream.Length + size );

        if ( length >= size )
        {
            UnderlyingStream.Position +=
                Encoding.ASCII.GetBytes( value, 0, size, UnderlyingStream.GetBuffer(), (int) UnderlyingStream.Position );
        }
        else
        {
            Encoding.ASCII.GetBytes( value, 0, length, UnderlyingStream.GetBuffer(), (int) UnderlyingStream.Position );
            UnderlyingStream.Position += size;
        }

        /*byte[] buffer = Encoding.ASCII.GetBytes( value );

			if ( buffer.Length >= size )
			{
				m_Stream.Write( buffer, 0, size );
			}
			else
			{
				m_Stream.Write( buffer, 0, buffer.Length );
				Fill( size - buffer.Length );
			}*/
    }

    /// <summary>
    ///     Writes a dynamic-length ASCII-encoded string value to the underlying stream, followed by a 1-byte null character.
    /// </summary>
    public void WriteAsciiNull( string value )
    {
        if ( value == null )
        {
            Console.WriteLine( @"Network: Attempted to WriteAsciiNull() with null value" );
            value = string.Empty;
        }

        int length = value.Length;

        UnderlyingStream.SetLength( UnderlyingStream.Length + length + 1 );

        Encoding.ASCII.GetBytes( value, 0, length, UnderlyingStream.GetBuffer(), (int) UnderlyingStream.Position );
        UnderlyingStream.Position += length + 1;

        /*byte[] buffer = Encoding.ASCII.GetBytes( value );

			m_Stream.Write( buffer, 0, buffer.Length );
			m_Stream.WriteByte( 0 );*/
    }

    /// <summary>
    ///     Writes a dynamic-length little-endian unicode string value to the underlying stream, followed by a 2-byte null
    ///     character.
    /// </summary>
    public void WriteLittleUniNull( string value )
    {
        if ( value == null )
        {
            Console.WriteLine( @"Network: Attempted to WriteLittleUniNull() with null value" );
            value = string.Empty;
        }

        int length = value.Length;

        UnderlyingStream.SetLength( UnderlyingStream.Length + ( length + 1 ) * 2 );

        UnderlyingStream.Position +=
            Encoding.Unicode.GetBytes( value, 0, length, UnderlyingStream.GetBuffer(), (int) UnderlyingStream.Position );
        UnderlyingStream.Position += 2;

        /*byte[] buffer = Encoding.Unicode.GetBytes( value );

			m_Stream.Write( buffer, 0, buffer.Length );

			m_Buffer[0] = 0;
			m_Buffer[1] = 0;
			m_Stream.Write( m_Buffer, 0, 2 );*/
    }

    /// <summary>
    ///     Writes a fixed-length little-endian unicode string value to the underlying stream. To fit (size), the string
    ///     content is either truncated or padded with null characters.
    /// </summary>
    public void WriteLittleUniFixed( string value, int size )
    {
        if ( value == null )
        {
            Console.WriteLine( @"Network: Attempted to WriteLittleUniFixed() with null value" );
            value = string.Empty;
        }

        size *= 2;

        int length = value.Length;

        UnderlyingStream.SetLength( UnderlyingStream.Length + size );

        if ( length * 2 >= size )
        {
            UnderlyingStream.Position +=
                Encoding.Unicode.GetBytes( value, 0, length, UnderlyingStream.GetBuffer(), (int) UnderlyingStream.Position );
        }
        else
        {
            Encoding.Unicode.GetBytes( value, 0, length, UnderlyingStream.GetBuffer(), (int) UnderlyingStream.Position );
            UnderlyingStream.Position += size;
        }

        /*size *= 2;

			byte[] buffer = Encoding.Unicode.GetBytes( value );

			if ( buffer.Length >= size )
			{
				m_Stream.Write( buffer, 0, size );
			}
			else
			{
				m_Stream.Write( buffer, 0, buffer.Length );
				Fill( size - buffer.Length );
			}*/
    }

    /// <summary>
    ///     Writes a dynamic-length big-endian unicode string value to the underlying stream, followed by a 2-byte null
    ///     character.
    /// </summary>
    public void WriteBigUniNull( string value )
    {
        if ( value == null )
        {
            Console.WriteLine( @"Network: Attempted to WriteBigUniNull() with null value" );
            value = string.Empty;
        }

        int length = value.Length;

        UnderlyingStream.SetLength( UnderlyingStream.Length + ( length + 1 ) * 2 );

        UnderlyingStream.Position +=
            Encoding.BigEndianUnicode.GetBytes( value, 0, length, UnderlyingStream.GetBuffer(), (int) UnderlyingStream.Position );
        UnderlyingStream.Position += 2;

        /*byte[] buffer = Encoding.BigEndianUnicode.GetBytes( value );

			m_Stream.Write( buffer, 0, buffer.Length );

			m_Buffer[0] = 0;
			m_Buffer[1] = 0;
			m_Stream.Write( m_Buffer, 0, 2 );*/
    }

    /// <summary>
    ///     Writes a fixed-length big-endian unicode string value to the underlying stream. To fit (size), the string content
    ///     is either truncated or padded with null characters.
    /// </summary>
    public void WriteBigUniFixed( string value, int size )
    {
        if ( value == null )
        {
            Console.WriteLine( @"Network: Attempted to WriteBigUniFixed() with null value" );
            value = string.Empty;
        }

        size *= 2;

        int length = value.Length;

        UnderlyingStream.SetLength( UnderlyingStream.Length + size );

        if ( length * 2 >= size )
        {
            UnderlyingStream.Position +=
                Encoding.BigEndianUnicode.GetBytes( value, 0, length, UnderlyingStream.GetBuffer(), (int) UnderlyingStream.Position );
        }
        else
        {
            Encoding.BigEndianUnicode.GetBytes( value, 0, length, UnderlyingStream.GetBuffer(), (int) UnderlyingStream.Position );
            UnderlyingStream.Position += size;
        }

        /*size *= 2;

			byte[] buffer = Encoding.BigEndianUnicode.GetBytes( value );

			if ( buffer.Length >= size )
			{
				m_Stream.Write( buffer, 0, size );
			}
			else
			{
				m_Stream.Write( buffer, 0, buffer.Length );
				Fill( size - buffer.Length );
			}*/
    }

    /// <summary>
    ///     Fills the stream from the current position up to (capacity) with 0x00's
    /// </summary>
    public void Fill()
    {
        Fill( (int) ( m_Capacity - UnderlyingStream.Length ) );
    }

    /// <summary>
    ///     Writes a number of 0x00 byte values to the underlying stream.
    /// </summary>
    public void Fill( int length )
    {
        if ( UnderlyingStream.Position == UnderlyingStream.Length )
        {
            UnderlyingStream.SetLength( UnderlyingStream.Length + length );
            UnderlyingStream.Seek( 0, SeekOrigin.End );
        }
        else
        {
            UnderlyingStream.Write( new byte[length], 0, length );
        }
    }

    /// <summary>
    ///     Offsets the current position from an origin.
    /// </summary>
    public long Seek( long offset, SeekOrigin origin )
    {
        return UnderlyingStream.Seek( offset, origin );
    }

    /// <summary>
    ///     Gets the entire stream content as a byte array.
    /// </summary>
    public byte[] ToArray()
    {
        return UnderlyingStream.ToArray();
    }

    protected virtual void Dispose( bool isDisposing )
    {
        if ( isDisposing )
        {
            UnderlyingStream.Dispose();
        }

        GC.SuppressFinalize( this );
    }
}