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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     A file path to a thumbnail of it, decoded at the width given as the converter parameter.
///     <para>
///         The gallery lists every screenshot ever taken, and decoding those at full size would hold a
///         desktop-sized bitmap per row - so they are decoded down instead, and cached by path so
///         scrolling does not re-decode. The cache is keyed on the file's write time too, so replacing a
///         file on disk still refreshes.
///     </para>
/// </summary>
public class PathToThumbnailValueConverter : IValueConverter
{
    private const int DEFAULT_WIDTH = 256;

    private static readonly Dictionary<string, Bitmap> _cache = new();

    public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
    {
        if ( value is not string path || !File.Exists( path ) )
        {
            return null;
        }

        int width = parameter is string text && int.TryParse( text, out int parsed ) ? parsed : DEFAULT_WIDTH;

        string key = $"{path}|{width}|{File.GetLastWriteTimeUtc( path ).Ticks}";

        if ( _cache.TryGetValue( key, out Bitmap cached ) )
        {
            return cached;
        }

        try
        {
            using FileStream stream = File.OpenRead( path );

            Bitmap thumbnail = Bitmap.DecodeToWidth( stream, width );

            _cache[key] = thumbnail;

            return thumbnail;
        }
        catch ( Exception )
        {
            // A half-written or corrupt file - the row just shows no image.
            return null;
        }
    }

    public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}
