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
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Avalonia.Data.Converters;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     WPF resolves an enum's [TypeConverter(typeof(EnumDescriptionTypeConverter))] automatically
///     wherever the value is bound to display text (see AutolootOperator, SkillBonusSkills); Avalonia's
///     binding engine doesn't consult TypeConverterAttribute, so that attribute is inert here and
///     every binding has to opt in to this converter explicitly instead.
/// </summary>
public class EnumDescriptionValueConverter : IValueConverter
{
    public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
    {
        if ( value == null )
        {
            return string.Empty;
        }

        FieldInfo fi = value.GetType().GetField( value.ToString() );

        if ( fi == null )
        {
            return value.ToString();
        }

        DescriptionAttribute[] attributes =
            (DescriptionAttribute[]) fi.GetCustomAttributes( typeof( DescriptionAttribute ), false );

        return attributes.Length > 0 && !string.IsNullOrEmpty( attributes[0].Description )
            ? attributes[0].Description
            : value.ToString();
    }

    public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}
