using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ClassicAssist.UO.Data
{
    public static class Hues
    {
        private const int BlockCount = 375;
        private static string _dataPath;

        public static Lazy<HueEntry[]> _lazyHueEntries = new Lazy<HueEntry[]>( LoadHueIndex );

        public static bool Initialize( string dataPath )
        {
            _dataPath = dataPath;

            return true;
        }

        private static HueEntry[] LoadHueIndex()
        {
            HueEntry[] entries = new HueEntry[3000];

            if ( !File.Exists( Path.Combine( _dataPath, "hues.mul" ) ) )
            {
                throw new FileNotFoundException( "File not found", "hues.mul" );
            }

            using ( FileStream reader = File.Open( Path.Combine( _dataPath, "hues.mul" ), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite ) )
            {
                BinaryReader binaryReader = new BinaryReader( reader );
                int total = 0;

                for ( int i = 0; i < BlockCount; i++ )
                {
                    binaryReader.ReadInt32();

                    for ( int j = 0; j < 8; ++j, ++total )
                    {
                        entries[total] = HueEntry.Read( binaryReader );
                    }
                }
            }

            return entries;
        }

        /// <summary>
        ///     Recolours a block of ARGB1555 colour words in place.
        ///     <para>
        ///         A hue is a 32-entry ramp indexed by the pixel's red channel, so hueing is a table lookup per
        ///         pixel rather than a blend. Fully transparent pixels are left alone.
        ///     </para>
        /// </summary>
        /// <param name="hue">1-based hue id as it arrives from the server; the upper bits are flags.</param>
        /// <param name="colours">Tightly packed colour words, modified in place.</param>
        /// <param name="onlyHueGrayPixels">
        ///     Partial hue: only recolour pixels that are already grey, leaving coloured detail alone.
        /// </param>
        public static void ApplyHue( int hue, ushort[] colours, bool onlyHueGrayPixels )
        {
            if ( colours == null )
            {
                return;
            }

            hue = ( hue & 0x3FFF ) - 1;

            // Checked before touching _lazyHueEntries: hue 0 means unhued, and an unhued draw has no
            // business loading hues.mul - it would make every caller depend on Initialize having run.
            if ( hue < 0 )
            {
                return;
            }

            HueEntry[] entries = _lazyHueEntries.Value;

            if ( hue >= entries.Length )
            {
                return;
            }

            short[] rampColours = entries[hue].Colors;

            if ( rampColours == null )
            {
                return;
            }

            for ( int i = 0; i < colours.Length; i++ )
            {
                int c = colours[i];

                if ( c == 0 )
                {
                    continue;
                }

                int r = ( c >> 10 ) & 0x1F;

                if ( onlyHueGrayPixels )
                {
                    int g = ( c >> 5 ) & 0x1F;
                    int b = c & 0x1F;

                    if ( r != g || r != b )
                    {
                        continue;
                    }
                }

                colours[i] = (ushort) rampColours[r];
            }
        }

    }

    [StructLayout( LayoutKind.Sequential, Pack = 1 )]
    public struct HueEntry
    {
        [MarshalAs( UnmanagedType.ByValArray, SizeConst = 32 )]
        public short[] Colors;

        public short TableStart;
        public short TableEnd;

        [MarshalAs( UnmanagedType.ByValArray, SizeConst = 20 )]
        public string Name;

        public static HueEntry Read( BinaryReader reader )
        {
            // ReSharper disable once UseObjectOrCollectionInitializer
            HueEntry entry = new HueEntry();

            entry.Colors = new short[32];

            for ( int i = 0; i < 32; ++i )
            {
                entry.Colors[i] = (short) ( reader.ReadUInt16() | 0x8000 );
            }

            entry.TableStart = (short) ( reader.ReadUInt16() | 0x8000 );
            entry.TableEnd = (short) ( reader.ReadUInt16() | 0x8000 );
            entry.Name = Encoding.ASCII.GetString( reader.ReadBytes( 20 ) ).TrimEnd( '\0' );

            return entry;
        }
    }
}