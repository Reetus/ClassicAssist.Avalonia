using System;
using System.Runtime.CompilerServices;

namespace ClassicAssist.UO.Data;

/// <summary>
///     Conversion between UO's 16-bit ARGB1555 colour words and 32-bit RGBA.
///     <para>
///         The 5-bit channels do not scale linearly to 8 bits - the client uses a fixed 32-entry curve, so
///         a straight <c>value * 255 / 31</c> gives visibly wrong colours. Same table as ClassicUO and
///         GumpStudioEnhanced.
///     </para>
/// </summary>
public static class HuesHelper
{
    private static readonly byte[] _table =
    [
        0x00, 0x08, 0x10, 0x18, 0x20, 0x29, 0x31, 0x39, 0x41, 0x4A, 0x52, 0x5A, 0x62, 0x6A, 0x73, 0x7B, 0x83,
        0x8B, 0x94, 0x9C, 0xA4, 0xAC, 0xB4, 0xBD, 0xC5, 0xCD, 0xD5, 0xDE, 0xE6, 0xEE, 0xF6, 0xFF
    ];

    private static readonly Lazy<byte[]> _lazyNearest5Bit = new( BuildNearest5BitTable );

    /// <summary>
    ///     ARGB1555 to RGBA8888. Bit 15 is the one-bit alpha, so a word of 0 is fully transparent.
    /// </summary>
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static uint Color16To32( ushort c )
    {
        return (uint) ( _table[( c >> 10 ) & 0x1F] | ( _table[( c >> 5 ) & 0x1F] << 8 ) |
                        ( _table[c & 0x1F] << 16 ) | ( ( c >> 15 ) * 0xFF ) << 24 );
    }

    /// <summary>
    ///     RGBA8888 back to ARGB1555, picking the nearest entry in the curve for each channel. The alpha bit
    ///     is set, since callers only round-trip visible pixels.
    /// </summary>
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static ushort Color32To16( byte r, byte g, byte b )
    {
        byte[] nearest = _lazyNearest5Bit.Value;

        return (ushort) ( 0x8000 | ( nearest[r] << 10 ) | ( nearest[g] << 5 ) | nearest[b] );
    }

    /// <summary>
    ///     Converts a tightly packed block of colour words in place into a <see cref="Pixmap" />.
    /// </summary>
    public static Pixmap ToPixmap( ushort[] colours, int width, int height )
    {
        if ( colours == null || width <= 0 || height <= 0 )
        {
            return Pixmap.Empty;
        }

        uint[] pixels = new uint[width * height];

        for ( int i = 0; i < pixels.Length && i < colours.Length; i++ )
        {
            ushort c = colours[i];

            // Untouched runs stay 0. Treating those as transparent rather than opaque black is what
            // gives item art its cut-out shape.
            pixels[i] = c == 0 ? 0 : Color16To32( c );
        }

        return new Pixmap( width, height, pixels );
    }

    /// <summary>
    ///     For every 8-bit channel value, the index of the closest entry in the curve. Built once so
    ///     <see cref="Color32To16" /> is three array reads rather than three 32-step scans.
    /// </summary>
    private static byte[] BuildNearest5BitTable()
    {
        byte[] map = new byte[256];

        for ( int value = 0; value < 256; value++ )
        {
            int nearest = 0;
            int minDiff = int.MaxValue;

            for ( int i = 0; i < _table.Length; i++ )
            {
                int diff = Math.Abs( _table[i] - value );

                if ( diff >= minDiff )
                {
                    continue;
                }

                minDiff = diff;
                nearest = i;
            }

            map[value] = (byte) nearest;
        }

        return map;
    }
}
