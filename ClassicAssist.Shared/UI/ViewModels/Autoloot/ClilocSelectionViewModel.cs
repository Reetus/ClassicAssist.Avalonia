#region License

// Copyright (C) 2020 Reetus
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
using System.Windows.Input;
using ClassicAssist.Misc;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;

namespace ClassicAssist.Shared.UI.ViewModels.Autoloot;

public class ClilocSelectionViewModel : BaseViewModel
{
    public ClilocSelectionViewModel()
    {
        foreach ( KeyValuePair<int, string> kvp in Cliloc.GetItems() )
        {
            AllClilocs.Add( new ClilocEntry { Key = kvp.Key, Value = kvp.Value } );
        }

        UpdateEntries( FilterText );
    }

    public ObservableCollection<ClilocEntry> AllClilocs
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public MessageBoxResult DialogResult
    {
        get;
        set => SetProperty( ref field, value );
    } = MessageBoxResult.Cancel;

    public ObservableCollection<ClilocEntry> FilteredClilocs
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public string FilterText
    {
        get;
        set
        {
            SetProperty( ref field, value );
            UpdateEntries( value );
        }
    }

    public ICommand OKCommand => field ??= new RelayCommand( OK, o => SelectedCliloc != null );

    public ClilocEntry SelectedCliloc
    {
        get;
        set => SetProperty( ref field, value );
    }

    private void OK( object obj )
    {
        DialogResult = MessageBoxResult.OK;
    }

    private void UpdateEntries( string filterText )
    {
        IEnumerable<ClilocEntry> matches = AllClilocs.Where( m =>
            string.IsNullOrEmpty( filterText ) || m.Key.ToString().Contains( filterText ) ||
            m.Value.ToLower().Contains( filterText.ToLower() ) );

        FilteredClilocs.Clear();

        foreach ( ClilocEntry clilocEntry in matches )
        {
            FilteredClilocs.Add( clilocEntry );
        }
    }

    public class ClilocEntry
    {
        public int Key { get; set; }
        public string Value { get; set; }
    }
}