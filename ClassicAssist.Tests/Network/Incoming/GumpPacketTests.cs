using System.Collections.Generic;
using System.Text;
using ClassicAssist.Shared;
using ClassicAssist.UO;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using ClassicAssist.UO.Objects.Gumps;
using Xunit;

namespace ClassicAssist.Tests.Network.Incoming
{
    /// <summary>
    ///     Gumps and menus: 0xB0 (plain layout and big-endian text lines), 0xDD (the same, zlib-compressed,
    ///     with the decompressed length counted inside the compressed length), 0xBF 0x04 close and 0x7C item
    ///     list menus. WaitForGump / ReplyGump / InGump and the menu commands look the gump up by id, so a gump
    ///     that parses with its strings shifted or one that never leaves the collection on close breaks every
    ///     crafting and vendor macro.
    /// </summary>
    public class GumpPacketTests : IncomingPacketTestBase
    {
        private const int SENDER = 0x00000701;
        private const uint GUMP_ID = 0x93A564C3;

        private const string LAYOUT = "{ page 0 }{ resizepic 0 0 2600 300 200 }{ text 20 30 0 0 }{ text 20 50 0 1 }" +
                                      "{ button 60 95 4005 4007 1 0 7 }";

        [Fact]
        public void WillAddGumpWithTextAndRaiseEventOnGenericGump()
        {
            List<(uint ID, int Serial, Gump Gump)> raised = new List<(uint, int, Gump)>();

            void OnGump( uint id, int serial, Gump gump )
            {
                raised.Add( ( id, serial, gump ) );
            }

            IncomingPacketHandlers.GumpEvent += OnGump;

            try
            {
                Receive( GenericGump( SENDER, GUMP_ID, LAYOUT, "First line", "Zweite Zeile Ü" ) );
            }
            finally
            {
                IncomingPacketHandlers.GumpEvent -= OnGump;
            }

            Assert.True( Engine.Gumps.GetGump( GUMP_ID, out Gump gump ) );

            (uint id, int serial, Gump eventGump) = Assert.Single( raised );

            Assert.Equal( GUMP_ID, id );
            Assert.Equal( SENDER, serial );
            Assert.Same( gump, eventGump );
            Assert.Equal( 120, gump.X );
            Assert.Equal( 80, gump.Y );
            Assert.Equal( new[] { "First line", "Zweite Zeile Ü" }, gump.Strings );
            Assert.Equal( "Zweite Zeile Ü", gump.GetElementByXY( 20, 50 ).Text );
            Assert.Equal( SENDER, Engine.GumpList[GUMP_ID] );
        }

        /// <summary>
        ///     A gump with no layout is remembered as open (the server still expects a reply to it) but there
        ///     is nothing to parse.
        /// </summary>
        [Fact]
        public void WillTrackButNotParseGenericGumpWithEmptyLayout()
        {
            Receive( GenericGump( SENDER, GUMP_ID, "" ) );

            Assert.False( Engine.Gumps.GetGump( GUMP_ID, out _ ) );
            Assert.Equal( SENDER, Engine.GumpList[GUMP_ID] );
        }

        [Fact]
        public void WillDecompressLayoutAndTextOnCompressedGump()
        {
            List<Gump> raised = new List<Gump>();

            void OnGump( uint id, int serial, Gump gump )
            {
                raised.Add( gump );
            }

            IncomingPacketHandlers.GumpEvent += OnGump;

            try
            {
                Receive( CompressedGump( SENDER, GUMP_ID, LAYOUT, "First line", "Second line" ) );
            }
            finally
            {
                IncomingPacketHandlers.GumpEvent -= OnGump;
            }

            Assert.True( Engine.Gumps.GetGump( GUMP_ID, out Gump gump ) );
            Assert.Same( gump, Assert.Single( raised ) );
            Assert.Equal( new[] { "First line", "Second line" }, gump.Strings );
            Assert.Equal( "First line", gump.GetElementByXY( 20, 30 ).Text );
            Assert.Equal( SENDER, gump.Serial );
        }

        /// <summary>
        ///     A gump without text lines sends a zero line count and an empty (length 4) text block - it
        ///     must still parse.
        /// </summary>
        [Fact]
        public void WillParseCompressedGumpWithNoTextLines()
        {
            Receive( CompressedGump( SENDER, GUMP_ID, "{ page 0 }{ button 60 95 4005 4007 1 0 7 }" ) );

            Assert.True( Engine.Gumps.GetGump( GUMP_ID, out Gump gump ) );
            Assert.Empty( gump.Strings );
        }

        [Fact]
        public void WontAddGumpWhenCompressedLayoutIsCorrupt()
        {
            byte[] packet = CompressedGump( SENDER, GUMP_ID, LAYOUT, "First line" );

            // Garble the layout stream past its two-byte zlib header (the stream starts at offset 27).
            for ( int i = 29; i < 40; i++ )
            {
                packet[i] ^= 0xFF;
            }

            Receive( packet );

            Assert.False( Engine.Gumps.GetGump( GUMP_ID, out _ ) );
        }

        [Fact]
        public void WillRemoveGumpOnCloseGump()
        {
            Receive( GenericGump( SENDER, GUMP_ID, LAYOUT, "a", "b" ) );

            Assert.True( Engine.Gumps.GetGump( GUMP_ID, out _ ) );

            Receive( Extended( 0x04, w =>
            {
                w.Write( GUMP_ID );
                w.Write( 0 ); // button
            } ) );

            Assert.False( Engine.Gumps.GetGump( GUMP_ID, out _ ) );
            Assert.False( Engine.GumpList.ContainsKey( GUMP_ID ) );
        }

        [Fact]
        public void WillAddMenuAndRaiseEventOnItemListMenu()
        {
            // 0x7C from a blacksmith: serial 0x0007767C, menu 0x01CF, "Blacksmithing", three choices.
            byte[] packet =
            {
                0x7C, 0x00, 0x39, 0x00, 0x07, 0x76, 0x7C, 0x01, 0xCF, 0x0D, 0x42, 0x6C, 0x61, 0x63, 0x6B, 0x73,
                0x6D, 0x69, 0x74, 0x68, 0x69, 0x6E, 0x67, 0x03, 0x0F, 0xAF, 0x00, 0x00, 0x06, 0x52, 0x65, 0x70,
                0x61, 0x69, 0x72, 0x0F, 0xB1, 0x00, 0x00, 0x05, 0x53, 0x6D, 0x65, 0x6C, 0x74, 0x13, 0xB9, 0x00,
                0x00, 0x07, 0x57, 0x65, 0x61, 0x70, 0x6F, 0x6E, 0x73
            };

            List<(int ID, Menu Menu)> raised = new List<(int, Menu)>();

            void OnMenu( int id, Menu menu )
            {
                raised.Add( ( id, menu ) );
            }

            IncomingPacketHandlers.MenuAddedEvent += OnMenu;

            try
            {
                Receive( packet );
            }
            finally
            {
                IncomingPacketHandlers.MenuAddedEvent -= OnMenu;
            }

            Assert.True( Engine.Menus.GetMenu( 0x01CF, out Menu menu ) );
            Assert.Same( menu, Assert.Single( raised ).Menu );
            Assert.Equal( 0x0007767C, menu.Serial );
            Assert.Equal( "Blacksmithing", menu.Title );
            Assert.Equal( 3, menu.Entries.Length );
            Assert.Equal( new[] { 1, 2, 3 }, new[] { menu.Entries[0].Index, menu.Entries[1].Index, menu.Entries[2].Index } );
            Assert.Equal( 0x13B9, menu.Entries[2].ID );
            Assert.Equal( "Weapons", menu.Entries[2].Title );
        }

        private static byte[] GenericGump( int serial, uint id, string layout, params string[] lines )
        {
            return Dynamic( 0xB0, w =>
            {
                w.Write( serial );
                w.Write( id );
                w.Write( 120 ); // x
                w.Write( 80 ); // y
                w.Write( (short) layout.Length );
                w.WriteAsciiFixed( layout, layout.Length );
                w.Write( (short) lines.Length );

                foreach ( string line in lines )
                {
                    w.Write( (short) line.Length );
                    w.WriteBigUniFixed( line, line.Length );
                }
            } );
        }

        private static byte[] CompressedGump( int serial, uint id, string layout, params string[] lines )
        {
            byte[] layoutBytes = Encoding.ASCII.GetBytes( layout + '\0' );
            byte[] compressedLayout = Compression.Compress( layoutBytes );

            List<byte> text = new List<byte>();

            foreach ( string line in lines )
            {
                text.Add( (byte) ( line.Length >> 8 ) );
                text.Add( (byte) line.Length );
                text.AddRange( Encoding.BigEndianUnicode.GetBytes( line ) );
            }

            return Dynamic( 0xDD, w =>
            {
                w.Write( serial );
                w.Write( id );
                w.Write( 120 ); // x
                w.Write( 80 ); // y
                w.Write( compressedLayout.Length + 4 );
                w.Write( layoutBytes.Length );
                w.Write( compressedLayout, 0, compressedLayout.Length );
                w.Write( lines.Length );

                if ( text.Count == 0 )
                {
                    w.Write( 4 );
                    w.Write( 0 );
                    return;
                }

                byte[] compressedText = Compression.Compress( text.ToArray() );

                w.Write( compressedText.Length + 4 );
                w.Write( text.Count );
                w.Write( compressedText, 0, compressedText.Length );
            } );
        }
    }
}
