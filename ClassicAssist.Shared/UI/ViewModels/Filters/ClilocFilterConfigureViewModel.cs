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

using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data.Filters;
using ClassicAssist.Misc;
using ClassicAssist.Shared.UI.ViewModels.Autoloot;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;

namespace ClassicAssist.Shared.UI.ViewModels.Filters
{
    /// <summary>
    ///     Edits <see cref="ClilocFilter.Filters" /> on a copy; the live list is only replaced when OK is
    ///     pressed, so Cancel discards.
    /// </summary>
    public class ClilocFilterConfigureViewModel : BaseViewModel
    {
        private ICommand _addItemCommand;
        private ICommand _chooseClilocCommand;
        private ObservableCollection<FilterClilocEntry> _items = new ObservableCollection<FilterClilocEntry>();
        private ICommand _okCommand;
        private ICommand _removeItemCommand;
        private FilterClilocEntry _selectedItem;
        private ICommand _selectHueCommand;

        public ClilocFilterConfigureViewModel()
        {
            foreach ( FilterClilocEntry filter in ClilocFilter.Filters )
            {
                Items.Add( new FilterClilocEntry
                {
                    Cliloc = filter.Cliloc,
                    Replacement = filter.Replacement,
                    Hue = filter.Hue,
                    ShowOverhead = filter.ShowOverhead
                } );
            }
        }

        public ICommand AddItemCommand => _addItemCommand ?? ( _addItemCommand = new RelayCommand( AddItem ) );

        public ICommand ChooseClilocCommand =>
            _chooseClilocCommand ?? ( _chooseClilocCommand = new RelayCommandAsync( ChooseCliloc, o => o != null ) );

        public ObservableCollection<FilterClilocEntry> Items
        {
            get => _items;
            set => SetProperty( ref _items, value );
        }

        public ICommand OKCommand => _okCommand ?? ( _okCommand = new RelayCommand( OK ) );

        public ICommand RemoveItemCommand =>
            _removeItemCommand ?? ( _removeItemCommand = new RelayCommand( RemoveItem, o => o != null ) );

        public FilterClilocEntry SelectedItem
        {
            get => _selectedItem;
            set => SetProperty( ref _selectedItem, value );
        }

        public ICommand SelectHueCommand =>
            _selectHueCommand ?? ( _selectHueCommand = new RelayCommandAsync( SelectHue, o => o != null ) );

        private void OK( object obj )
        {
            ClilocFilter.Filters.Clear();

            foreach ( FilterClilocEntry entry in Items )
            {
                if ( ClilocFilter.Filters.All( e => e.Cliloc != entry.Cliloc ) )
                {
                    ClilocFilter.Filters.Add( new FilterClilocEntry
                    {
                        Cliloc = entry.Cliloc,
                        Replacement = entry.Replacement,
                        Hue = entry.Hue,
                        ShowOverhead = entry.ShowOverhead
                    } );
                }
            }
        }

        private static async Task ChooseCliloc( object obj )
        {
            if ( !( obj is FilterClilocEntry entry ) )
            {
                return;
            }

            ClilocSelectionViewModel vm = new ClilocSelectionViewModel();

            await Engine.UIInvoker.InvokeDialog( "ClilocSelectionWindow", dataContext: vm );

            if ( vm.DialogResult != MessageBoxResult.OK || vm.SelectedCliloc == null )
            {
                return;
            }

            entry.Cliloc = vm.SelectedCliloc.Key;
            entry.Replacement = vm.SelectedCliloc.Value;
        }

        private static async Task SelectHue( object obj )
        {
            if ( !( obj is FilterClilocEntry entry ) )
            {
                return;
            }

            int hue = await Engine.UIInvoker.GetHueAsync();

            if ( hue == -1 )
            {
                return;
            }

            entry.Hue = hue;
        }

        private void RemoveItem( object obj )
        {
            if ( obj is FilterClilocEntry entry )
            {
                Items.Remove( entry );
            }
        }

        private void AddItem( object obj )
        {
            Items.Add( new FilterClilocEntry
            {
                Cliloc = 500000, Replacement = Cliloc.GetProperty( 500000 ), Hue = -1
            } );
        }
    }
}
