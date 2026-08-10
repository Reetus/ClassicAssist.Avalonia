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

using ClassicAssist.Shared.UI;

namespace ClassicAssist.Data.Filters;

/// <summary>
///     One cliloc replacement for <see cref="ClilocFilter" />. Lives beside the filter rather than in
///     the configure view model (where WPF keeps it) so the data layer doesn't reach into the UI layer.
/// </summary>
public class FilterClilocEntry : SetPropertyNotifyChanged
{
    public int Cliloc
    {
        get;
        set
        {
            SetProperty( ref field, value );
            OnPropertyChanged( nameof( Original ) );
        }
    }

    public int Hue
    {
        get;
        set => SetProperty( ref field, value );
    } = -1;

    public string Original => UO.Data.Cliloc.GetProperty( Cliloc );

    public string Replacement
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool ShowOverhead
    {
        get;
        set => SetProperty( ref field, value );
    }
}
