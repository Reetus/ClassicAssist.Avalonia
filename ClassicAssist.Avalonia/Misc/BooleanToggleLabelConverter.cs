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

namespace ClassicAssist.Avalonia.Misc
{
    /// <summary>
    ///     Relabels a start/stop toggle button: values[0] is the running bool, values[1] the label to
    ///     show while running, values[2] the label to show while idle. WPF does this per-button with a
    ///     DataTrigger pair on Content; this is the same thing as one reusable converter, driven off
    ///     localized <c>Strings.*</c> bindings rather than baking English text into a ConverterParameter.
    /// </summary>
    public class BooleanToggleLabelConverter : IMultiValueConverter
    {
        public object Convert( IList<object> values, Type targetType, object parameter, CultureInfo culture )
        {
            bool isRunning = values.Count > 0 && values[0] is true;
            string runningLabel = values.Count > 1 ? values[1] as string : null;
            string idleLabel = values.Count > 2 ? values[2] as string : null;

            return isRunning ? runningLabel : idleLabel;
        }
    }
}
