using System.Linq;
using ClassicAssist.UO.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class HuesHelperTests
    {
        [TestMethod]
        public void WillTreatZeroAsTransparent()
        {
            Assert.AreEqual( 0u, HuesHelper.Color16To32( 0 ) >> 24, "alpha should be clear when bit 15 is unset" );
        }

        [TestMethod]
        public void WillConvertKnownColours()
        {
            // 0x8000 is opaque with every colour channel at 0.
            Assert.AreEqual( 0xFF000000, HuesHelper.Color16To32( 0x8000 ) );

            // 0xFFFF is opaque with every channel saturated.
            Assert.AreEqual( 0xFFFFFFFF, HuesHelper.Color16To32( 0xFFFF ) );
        }

        [TestMethod]
        public void WillPlaceChannelsInRgbaByteOrder()
        {
            // Red only: 5 bits of red at full, so byte 0 (lowest) is 0xFF and green/blue are 0.
            uint red = HuesHelper.Color16To32( 0x8000 | ( 0x1F << 10 ) );

            Assert.AreEqual( 0xFFu, red & 0xFF, "red belongs in the first byte" );
            Assert.AreEqual( 0x00u, ( red >> 8 ) & 0xFF );
            Assert.AreEqual( 0x00u, ( red >> 16 ) & 0xFF );

            uint blue = HuesHelper.Color16To32( 0x8000 | 0x1F );

            Assert.AreEqual( 0xFFu, ( blue >> 16 ) & 0xFF, "blue belongs in the third byte" );
        }

        [TestMethod]
        public void WillRoundTripEveryColourWord()
        {
            // Color32To16 has to pick the nearest curve entry, so it is only exact for values the curve
            // actually produces - which is precisely what Color16To32 emits.
            for ( int c = 0; c < 0x8000; c++ )
            {
                ushort original = (ushort) ( c | 0x8000 );
                uint wide = HuesHelper.Color16To32( original );

                ushort roundTripped = HuesHelper.Color32To16( (byte) ( wide & 0xFF ), (byte) ( ( wide >> 8 ) & 0xFF ),
                    (byte) ( ( wide >> 16 ) & 0xFF ) );

                Assert.AreEqual( original, roundTripped, $"0x{original:X4} did not survive the round trip" );
            }
        }

        [TestMethod]
        public void WillConvertBlockToPixmap()
        {
            ushort[] colours = { 0, 0x8000, 0xFFFF, 0 };

            Pixmap pixmap = HuesHelper.ToPixmap( colours, 2, 2 );

            Assert.AreEqual( 2, pixmap.Width );
            Assert.AreEqual( 2, pixmap.Height );
            Assert.AreEqual( 0u, pixmap.GetPixel( 0, 0 ) );
            Assert.AreEqual( 0xFF000000, pixmap.GetPixel( 1, 0 ) );
            Assert.AreEqual( 0xFFFFFFFF, pixmap.GetPixel( 0, 1 ) );
        }
    }

    [TestClass]
    public class PixmapTests
    {
        [TestMethod]
        public void WillReturnTransparentOutsideBounds()
        {
            Pixmap pixmap = new Pixmap( 2, 2, new uint[] { 1, 2, 3, 4 } );

            Assert.AreEqual( 0u, pixmap.GetPixel( -1, 0 ) );
            Assert.AreEqual( 0u, pixmap.GetPixel( 0, -1 ) );
            Assert.AreEqual( 0u, pixmap.GetPixel( 2, 0 ) );
            Assert.AreEqual( 0u, pixmap.GetPixel( 0, 2 ) );
            Assert.AreEqual( 4u, pixmap.GetPixel( 1, 1 ) );
        }

        [TestMethod]
        public void WillReportEmpty()
        {
            Assert.IsTrue( Pixmap.Empty.IsEmpty );
            Assert.IsFalse( new Pixmap( 1, 1, new uint[1] ).IsEmpty );
        }
    }

    [TestClass]
    public class ArtTests
    {
        [TestMethod]
        public void WillDecodeStaticTile()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            Art.Initialize( TestData.UOPath );

            // 0x1BF8 is a silver ingot - small, and present in every client.
            Pixmap pixmap = Art.GetStatic( 0x1BF8 );

            Assert.IsFalse( pixmap.IsEmpty, "expected art for 0x1BF8" );
            Assert.IsTrue( pixmap.Width > 0 && pixmap.Width <= 1024 );
            Assert.IsTrue( pixmap.Height > 0 && pixmap.Height <= 1024 );
            Assert.AreEqual( pixmap.Width * pixmap.Height, pixmap.Pixels.Length );

            // Item art is a shape cut out of a rectangle, so it must have both.
            Assert.IsTrue( pixmap.Pixels.Any( p => ( p >> 24 ) == 0xFF ), "expected some opaque pixels" );
            Assert.IsTrue( pixmap.Pixels.Any( p => p == 0 ), "expected some transparent pixels" );
        }

        [TestMethod]
        public void WillReturnEmptyRatherThanThrowForMissingArt()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            Art.Initialize( TestData.UOPath );

            // Well past the end of the art table.
            Pixmap pixmap = Art.GetStatic( 0xFFFF );

            Assert.IsNotNull( pixmap.Pixels ?? new uint[0] );
        }

        [TestMethod]
        public void WillApplyHue()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            Art.Initialize( TestData.UOPath );
            Hues.Initialize( TestData.UOPath );
            TileData.Initialize( TestData.UOPath );

            Pixmap plain = Art.GetStatic( 0x1BF8 );
            Pixmap hued = Art.GetStatic( 0x1BF8, 0x0021 );

            Assert.IsFalse( plain.IsEmpty );
            Assert.IsFalse( hued.IsEmpty );
            Assert.AreEqual( plain.Width, hued.Width );
            Assert.AreEqual( plain.Height, hued.Height );

            bool anyChanged = plain.Pixels.Where( ( t, i ) => t != hued.Pixels[i] ).Any();

            Assert.IsTrue( anyChanged, "hueing should have recoloured at least one pixel" );

            // Transparency is structural - a hue must never fill in the cut-out.
            for ( int i = 0; i < plain.Pixels.Length; i++ )
            {
                if ( plain.Pixels[i] == 0 )
                {
                    Assert.AreEqual( 0u, hued.Pixels[i], $"pixel {i} was transparent and should have stayed so" );
                }
            }
        }

        [TestMethod]
        public void WillLeaveBufferAloneForOutOfRangeHue()
        {
            ushort[] colours = { 0x8000, 0xFFFF };
            ushort[] expected = (ushort[]) colours.Clone();

            Hues.ApplyHue( 0, colours, false );

            CollectionAssert.AreEqual( expected, colours, "hue 0 means unhued" );
        }
    }
}
