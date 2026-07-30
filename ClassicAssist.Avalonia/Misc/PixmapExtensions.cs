using System;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ClassicAssist.UO.Data;

namespace ClassicAssist.Avalonia.Misc
{
    /// <summary>
    ///     Bridges the UI-framework-agnostic <see cref="Pixmap" /> the art and hue loaders produce onto
    ///     Avalonia. This lives here rather than in ClassicAssist.Shared so that assembly stays loadable
    ///     inside the game process, which has no Avalonia.
    /// </summary>
    public static class PixmapExtensions
    {
        /// <summary>
        ///     Copies a decoded tile into a <see cref="WriteableBitmap" /> ready to bind to an Image.
        /// </summary>
        /// <returns>The bitmap, or null for an empty pixmap - binding null simply shows nothing.</returns>
        public static unsafe WriteableBitmap ToBitmap( this Pixmap pixmap )
        {
            if ( pixmap.IsEmpty )
            {
                return null;
            }

            // Rgba8888 matches Pixmap's byte order. Alpha out of the UO formats is only ever 0 or 255, so
            // the data is already valid premultiplied and needs no conversion.
            WriteableBitmap bitmap = new WriteableBitmap( new PixelSize( pixmap.Width, pixmap.Height ),
                new Vector( 96, 96 ), PixelFormat.Rgba8888, AlphaFormat.Premul );

            using ( ILockedFramebuffer buffer = bitmap.Lock() )
            {
                int rowBytes = pixmap.Width * sizeof( uint );

                fixed ( uint* source = pixmap.Pixels )
                {
                    // Copy a row at a time: the framebuffer is free to pad each row out to its own stride,
                    // which is not necessarily width * 4.
                    for ( int y = 0; y < pixmap.Height; y++ )
                    {
                        Buffer.MemoryCopy( source + y * pixmap.Width,
                            (byte*) buffer.Address + y * buffer.RowBytes, rowBytes, rowBytes );
                    }
                }
            }

            return bitmap;
        }
    }
}
