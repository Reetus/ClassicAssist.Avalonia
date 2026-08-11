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
using Avalonia.Data.Converters;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     True when a bound collection count is greater than zero. Used to collapse a submenu (e.g.
///     ECV's "Move to set") when there's nothing to populate it with, matching WPF's
///     <c>DataTrigger Binding="{Binding ...Count}" Value="0"</c> pattern.
/// </summary>
public class CountToBooleanConverter : IValueConverter
{
    public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
    {
        return value is int count && count > 0;
    }

    public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}
