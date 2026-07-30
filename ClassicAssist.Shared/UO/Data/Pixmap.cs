using System;

namespace ClassicAssist.UO.Data
{
    /// <summary>
    ///     A decoded image as packed RGBA8888 pixels, one <see cref="uint" /> per pixel.
    ///     <para>
    ///         This exists because <c>System.Drawing.Bitmap</c> does not. <c>System.Drawing.Common</c> throws
    ///         <see cref="PlatformNotSupportedException" /> on anything but Windows from .NET 7 onwards, so the
    ///         art and hue loaders cannot hand back a Bitmap and still run on Linux. Decoding to a plain array
    ///         also keeps this assembly free of any UI framework - the Avalonia side turns it into a
    ///         WriteableBitmap, and the tests can assert on pixels without a display.
    ///     </para>
    ///     <para>
    ///         Byte order in memory is R, G, B, A, matching Avalonia's <c>PixelFormat.Rgba8888</c>. Alpha is
    ///         all-or-nothing coming out of the UO formats, so the data is equally valid read as straight or
    ///         premultiplied.
    ///     </para>
    /// </summary>
    public readonly struct Pixmap
    {
        public Pixmap( int width, int height, uint[] pixels )
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; }

        public int Height { get; }

        public uint[] Pixels { get; }

        public bool IsEmpty => Width <= 0 || Height <= 0 || Pixels == null;

        public static Pixmap Empty { get; } = new Pixmap( 0, 0, Array.Empty<uint>() );

        /// <summary>
        ///     Pixel at (x, y), or fully transparent if outside the image.
        /// </summary>
        public uint GetPixel( int x, int y )
        {
            if ( Pixels == null || x < 0 || y < 0 || x >= Width || y >= Height )
            {
                return 0;
            }

            return Pixels[y * Width + x];
        }
    }
}
