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
using System.Linq;
using Avalonia.Data.Converters;
using ClassicAssist.Data.Autoloot;

namespace ClassicAssist.Avalonia.Misc
{
    /// <summary>
    ///     Narrows the operator ComboBox to the operators a <see cref="PropertyEntry" /> allows
    ///     (<see cref="PropertyEntry.AllowedOperators" />); shows all when unrestricted.
    /// </summary>
    public class AutolootOperatorFilterValueConverter : IValueConverter
    {
        public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
        {
            List<AutolootOperator> allOperators = Enum.GetValues( typeof( AutolootOperator ) ).Cast<AutolootOperator>().ToList();

            if ( !( value is PropertyEntry entry ) || entry.AllowedOperators == AutolootAllowedOperators.All ||
                 entry.AllowedOperators == 0 )
            {
                return allOperators;
            }

            List<AutolootOperator> filtered = new List<AutolootOperator>();

            if ( entry.AllowedOperators.HasFlag( AutolootAllowedOperators.Equal ) )
            {
                filtered.Add( AutolootOperator.Equal );
            }

            if ( entry.AllowedOperators.HasFlag( AutolootAllowedOperators.NotEqual ) )
            {
                filtered.Add( AutolootOperator.NotEqual );
            }

            if ( entry.AllowedOperators.HasFlag( AutolootAllowedOperators.LessThan ) )
            {
                filtered.Add( AutolootOperator.LessThan );
            }

            if ( entry.AllowedOperators.HasFlag( AutolootAllowedOperators.GreaterThan ) )
            {
                filtered.Add( AutolootOperator.GreaterThan );
            }

            if ( entry.AllowedOperators.HasFlag( AutolootAllowedOperators.NotPresent ) )
            {
                filtered.Add( AutolootOperator.NotPresent );
            }

            return filtered;
        }

        public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
        {
            throw new NotImplementedException();
        }
    }
}
