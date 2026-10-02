#region License

// Copyright (C) 2026 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY

#endregion

using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ClassicAssist.Data.Screenshot;
using SkiaSharp;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     Draws a captured client frame into its final PNG: the frame itself, the watermark in the top
///     right, and the info bar in the top left.
/// </summary>
public class AvaloniaScreenshotComposer : IScreenshotComposer
{
    private const double LOGO_OPACITY = 0.6;
    private const double LOGO_MARGIN = 5;
    private const double INFO_BAR_PADDING = 5;
    private const double INFO_BAR_CORNER_RADIUS = 5;

    private static readonly Uri _logoUri = new( "avares://ClassicAssist.Avalonia/Assets/screenshot_logo.png" );

    /// <summary>
    ///     Upstream asks for Arial, which only Windows reliably has. The rest are the usual stand-ins on
    ///     the other two platforms, and Avalonia walks the list in order.
    /// </summary>
    private static readonly FontFamily _infoBarFont =
        FontFamily.Parse( "Arial, Helvetica, Liberation Sans, DejaVu Sans, sans-serif" );

    private static Bitmap _logo;

    public async Task ComposeAsync( ScreenshotComposeRequest request )
    {
        // Read off the UI thread - it is several megabytes - then draw on it, since that is the thread
        // that owns the render interface. Callers await this from a macro thread or from the UI thread
        // itself, and InvokeAsync is safe either way.
        byte[] pixels = File.ReadAllBytes( request.FramePath );

        await Dispatcher.UIThread.InvokeAsync( () => Compose( request, pixels ) );
    }

    /// <summary>
    ///     Encodes the raw frame as an in-memory image for callers that want the bytes rather than a
    ///     file (the MCP snapshot tool). No watermark or info bar is drawn.
    ///     <para>
    ///         Done through Skia rather than <see cref="Bitmap.Save" />: Avalonia's save path only ever
    ///         writes PNG, whereas this tool offers JPEG too. Runs off the UI thread - it is pure
    ///         pixel work with no Avalonia objects, and a full-screen encode would otherwise stall the
    ///         UI.
    ///     </para>
    /// </summary>
    public Task<ScreenshotImage> EncodeAsync( ScreenshotEncodeRequest request )
    {
        return Task.Run( () => Encode( request ) );
    }

    private static ScreenshotImage Encode( ScreenshotEncodeRequest request )
    {
        byte[] pixels = File.ReadAllBytes( request.FramePath );

        int width = request.Width;
        int height = request.Height;

        // The frame is tightly packed RGBA, which is exactly Skia's Rgba8888. Copy row by row because
        // a Skia bitmap's rows can be padded to a wider stride.
        using SKBitmap bitmap = new( new SKImageInfo( width, height, SKColorType.Rgba8888, SKAlphaType.Opaque ) );

        IntPtr destination = bitmap.GetPixels();
        int rowLength = width * 4;

        for ( int y = 0; y < height; y++ )
        {
            int offset = y * rowLength;

            if ( offset + rowLength > pixels.Length )
            {
                break;
            }

            Marshal.Copy( pixels, offset, destination + y * bitmap.RowBytes, rowLength );
        }

        bool jpeg = string.Equals( request.Format, "jpeg", StringComparison.OrdinalIgnoreCase ) ||
                    string.Equals( request.Format, "jpg", StringComparison.OrdinalIgnoreCase );

        SKBitmap output = bitmap;
        SKBitmap resized = null;

        if ( request.MaxWidth is > 0 && width > request.MaxWidth.Value )
        {
            width = request.MaxWidth.Value;
            height = Math.Max( 1, (int) Math.Round( height * ( width / (double) request.Width ) ) );

            resized = bitmap.Resize( new SKImageInfo( width, height, SKColorType.Rgba8888, SKAlphaType.Opaque ),
                SKBitmapResizeMethod.Lanczos3 );

            output = resized;
        }

        try
        {
            using MemoryStream stream = new();
            using SKImage image = SKImage.FromBitmap( output );

            using ( SKData data = image.Encode( jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png,
                       jpeg ? 90 : 100 ) )
            {
                data.SaveTo( stream );
            }

            return new ScreenshotImage
            {
                Data = stream.ToArray(),
                MimeType = jpeg ? "image/jpeg" : "image/png",
                Format = jpeg ? "jpeg" : "png",
                Width = width,
                Height = height
            };
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static void Compose( ScreenshotComposeRequest request, byte[] pixels )
    {
        PixelSize size = new( request.Width, request.Height );
        Vector dpi = new( 96, 96 );

        using WriteableBitmap frame = new( size, dpi, PixelFormat.Rgba8888, AlphaFormat.Opaque );

        CopyPixels( frame, pixels, request.Width, request.Height );

        using RenderTargetBitmap target = new( size, dpi );

        using ( DrawingContext context = target.CreateDrawingContext() )
        {
            Rect bounds = new( 0, 0, request.Width, request.Height );

            context.DrawImage( frame, bounds );

            DrawLogo( context, bounds );

            if ( !string.IsNullOrEmpty( request.InfoBarText ) )
            {
                DrawInfoBar( context, request );
            }
        }

        Directory.CreateDirectory( Path.GetDirectoryName( request.OutputPath ) ?? string.Empty );

        target.Save( request.OutputPath );
    }

    /// <summary>
    ///     Copies row by row rather than in one block: the frame is tightly packed at four bytes per
    ///     pixel, but a locked bitmap's rows can be padded to a wider stride.
    /// </summary>
    private static void CopyPixels( WriteableBitmap bitmap, byte[] pixels, int width, int height )
    {
        int rowLength = width * 4;

        using ILockedFramebuffer buffer = bitmap.Lock();

        for ( int y = 0; y < height; y++ )
        {
            int offset = y * rowLength;

            if ( offset + rowLength > pixels.Length )
            {
                break;
            }

            Marshal.Copy( pixels, offset, buffer.Address + y * buffer.RowBytes, rowLength );
        }
    }

    private static void DrawLogo( DrawingContext context, Rect bounds )
    {
        Bitmap logo = GetLogo();

        if ( logo == null )
        {
            return;
        }

        Rect destination = new( bounds.Width - logo.Size.Width - LOGO_MARGIN, LOGO_MARGIN, logo.Size.Width,
            logo.Size.Height );

        using ( context.PushOpacity( LOGO_OPACITY ) )
        {
            context.DrawImage( logo, destination );
        }
    }

    private static void DrawInfoBar( DrawingContext context, ScreenshotComposeRequest request )
    {
        FormattedText text = new( request.InfoBarText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface( _infoBarFont ), request.FontSize,
            new SolidColorBrush( ParseColour( request.FontColour, Colors.White ) ) );

        Rect background = new( 0, 0, text.Width + INFO_BAR_PADDING * 2, text.Height + INFO_BAR_PADDING * 2 );

        context.DrawRectangle( new SolidColorBrush( ParseColour( request.BackgroundColour, Colors.Black ) ), null,
            new RoundedRect( background, INFO_BAR_CORNER_RADIUS ), default );

        context.DrawText( text, new Point( INFO_BAR_PADDING, INFO_BAR_PADDING ) );
    }

    /// <summary>
    ///     Colours come from the profile as #AARRGGBB strings, which is also what WPF wrote, so profiles
    ///     move between the two builds unchanged.
    /// </summary>
    private static Color ParseColour( string colour, Color fallback )
    {
        return Color.TryParse( colour, out Color parsed ) ? parsed : fallback;
    }

    private static Bitmap GetLogo()
    {
        if ( _logo != null )
        {
            return _logo;
        }

        try
        {
            using Stream stream = AssetLoader.Open( _logoUri );

            _logo = new Bitmap( stream );
        }
        catch ( Exception )
        {
            // A missing watermark should not cost the screenshot.
        }

        return _logo;
    }
}
