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
