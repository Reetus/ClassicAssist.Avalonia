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
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     Bold while a macro is running in the foreground. A background run gets
///     <see cref="FontStyle.Italic" /> instead (see <see cref="MacroRunningToFontStyleConverter" />) -
///     the two flags together pick one of three mutually exclusive display states, which a single
///     <c>IValueConverter</c> bound to either flag alone can't express.
/// </summary>
public class MacroRunningToFontWeightConverter : IMultiValueConverter
{
    public object Convert( IList<object> values, Type targetType, object parameter, CultureInfo culture )
    {
        bool isRunning = values.Count > 0 && values[0] is true;
        bool isBackground = values.Count > 1 && values[1] is true;

        return isRunning && !isBackground ? FontWeight.Bold : FontWeight.Normal;
    }
}

/// <summary>
///     Italic while a macro is running with "Run in background" enabled - see
///     <see cref="MacroRunningToFontWeightConverter" /> for the foreground/bold counterpart.
/// </summary>
public class MacroRunningToFontStyleConverter : IMultiValueConverter
{
    public object Convert( IList<object> values, Type targetType, object parameter, CultureInfo culture )
    {
        bool isRunning = values.Count > 0 && values[0] is true;
        bool isBackground = values.Count > 1 && values[1] is true;

        return isRunning && isBackground ? FontStyle.Italic : FontStyle.Normal;
    }
}
