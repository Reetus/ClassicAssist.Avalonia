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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ClassicAssist.Data.Filters;
using ClassicAssist.UI.ViewModels;

namespace ClassicAssist.Shared.UI.ViewModels.Filters;

/// <summary>
///     Edits <see cref="SoundFilter.Items" /> in place (the dialog has only a Close button, as in WPF)
///     and exposes them grouped by category. Avalonia has no <c>CollectionViewSource</c>, so the
///     grouping WPF does in XAML is materialised here instead.
/// </summary>
public class SoundFilterConfigureViewModel : BaseViewModel
{
    public SoundFilterConfigureViewModel()
    {
    }

    public SoundFilterConfigureViewModel( ObservableCollection<SoundFilterEntry> items )
    {
        Items = items;

        foreach ( IGrouping<string, SoundFilterEntry> grouping in items.GroupBy( i => i.Category ) )
        {
            Categories.Add( new SoundFilterCategory( grouping.Key, grouping ) );
        }
    }

    public ObservableCollection<SoundFilterCategory> Categories
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ObservableCollection<SoundFilterEntry> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];
}

public class SoundFilterCategory
{
    public SoundFilterCategory( string name, IEnumerable<SoundFilterEntry> entries )
    {
        Name = name;
        Entries = new ObservableCollection<SoundFilterEntry>( entries );
    }

    public ObservableCollection<SoundFilterEntry> Entries { get; }
    public string Name { get; }
}
