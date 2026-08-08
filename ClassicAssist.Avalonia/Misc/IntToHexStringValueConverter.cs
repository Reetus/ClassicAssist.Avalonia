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
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ClassicAssist.Avalonia.Misc
{
    /// <summary>
    ///     Displays an int as a zero-padded hex literal and parses it back, hex or decimal. Ported from
    ///     WPF's <c>UI/Misc/ValueConverters/IntToHexStringValueConverter</c> for <see cref="Controls.GraphicEditTextBlock" />.
    /// </summary>
    public class IntToHexStringValueConverter : IValueConverter
    {
        public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
        {
            return value is int val ? $"0x{val:x8}" : value;
        }

        public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
        {
            if ( !( value is string val ) )
            {
                return value;
            }

            try
            {
                return val.StartsWith( "0x", StringComparison.OrdinalIgnoreCase )
                    ? System.Convert.ToInt32( val.Substring( 2 ), 16 )
                    : int.Parse( val, NumberStyles.Integer, culture );
            }
            catch ( Exception )
            {
                // Whatever was typed doesn't parse as a graphic ID - leave the bound value unchanged
                // rather than throwing out of the binding engine.
                return BindingOperations.DoNothing;
            }
        }
    }
}
