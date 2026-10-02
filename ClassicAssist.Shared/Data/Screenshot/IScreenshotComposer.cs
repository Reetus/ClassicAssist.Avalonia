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

using System.Threading.Tasks;

namespace ClassicAssist.Data.Screenshot;

/// <summary>
///     Turns the raw frame the client handed back into the PNG on disk. Implemented in the Avalonia
///     assembly, since drawing the watermark and the info bar over the frame is that toolkit's job -
///     this half only knows what should end up in the image.
/// </summary>
public interface IScreenshotComposer
{
    Task ComposeAsync( ScreenshotComposeRequest request );

    /// <summary>
    ///     Encodes a captured frame as an in-memory image, without the watermark or info bar and
    ///     without writing a file. Used by the MCP snapshot tool, which returns the bytes as base64.
    /// </summary>
    Task<ScreenshotImage> EncodeAsync( ScreenshotEncodeRequest request );
}

public class ScreenshotComposeRequest
{
    /// <summary>Where the finished PNG should be written.</summary>
    public string OutputPath { get; set; }

    /// <summary>File holding <see cref="Width" /> * <see cref="Height" /> * 4 bytes of RGBA.</summary>
    public string FramePath { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Text for the info bar in the top-left corner; no bar is drawn when this is empty.</summary>
    public string InfoBarText { get; set; }

    public int FontSize { get; set; }

    /// <summary>#AARRGGBB, as stored in the profile.</summary>
    public string FontColour { get; set; }

    /// <summary>#AARRGGBB, as stored in the profile.</summary>
    public string BackgroundColour { get; set; }
}

public class ScreenshotEncodeRequest
{
    /// <summary>File holding <see cref="Width" /> * <see cref="Height" /> * 4 bytes of RGBA.</summary>
    public string FramePath { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Optional maximum width; wider frames are downscaled preserving aspect ratio.</summary>
    public int? MaxWidth { get; set; }

    /// <summary>Image format: "png" (default) or "jpeg".</summary>
    public string Format { get; set; } = "png";
}

public class ScreenshotImage
{
    public byte[] Data { get; set; }
    public string MimeType { get; set; }

    /// <summary>Normalised format name: "png" or "jpeg".</summary>
    public string Format { get; set; }

    /// <summary>Dimensions of the encoded image, after any downscaling.</summary>
    public int Width { get; set; }

    public int Height { get; set; }
}
