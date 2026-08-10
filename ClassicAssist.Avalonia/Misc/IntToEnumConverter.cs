#region License

// Copyright (C) 2026 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     Converts an int constraint value to an enum for a <see cref="Avalonia.Controls.ComboBox" />
///     SelectedItem binding (ConverterParameter = the enum type) and back.
/// </summary>
public class IntToEnumConverter : IValueConverter
{
    public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
    {
        if ( value is int intValue && parameter is Type enumType )
        {
            return Enum.ToObject( enumType, intValue );
        }

        return BindingNotification.UnsetValue;
    }

    public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
    {
        if ( value != null && value.GetType().IsEnum )
        {
            // Not (int) value: unboxing an object holding an enum straight to int throws, and Avalonia
            // swallows converter exceptions as a failed binding - so the selection silently never
            // reached the constraint.
            return System.Convert.ToInt32( value );
        }

        return BindingNotification.UnsetValue;
    }
}
