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
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data.Filters;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.Shared.UI.ViewModels.Filters
{
    /// <summary>
    ///     Edits <see cref="ItemIDFilter.Items" /> in place (the dialog has only a Close button, as in WPF).
    ///     Unlike WPF there is no art-browser popup for picking an ID - IDs are typed in hex or taken from
    ///     a target, since this port has no tile-art renderer.
    /// </summary>
    public class ItemIDFilterConfigureViewModel : BaseViewModel
    {
        private ICommand _addCommand;
        private ObservableCollection<ItemIDFilterEntry> _items = new ObservableCollection<ItemIDFilterEntry>();
        private ICommand _removeCommand;
        private ItemIDFilterEntry _selectedItem;
        private ICommand _selectHueCommand;
        private ICommand _targetDestinationIDCommand;
        private ICommand _targetSourceIDCommand;

        public ItemIDFilterConfigureViewModel()
        {
        }

        public ItemIDFilterConfigureViewModel( ObservableCollection<ItemIDFilterEntry> items )
        {
            Items = items;
        }

        public ICommand AddCommand => _addCommand ?? ( _addCommand = new RelayCommand( Add, o => true ) );

        public ObservableCollection<ItemIDFilterEntry> Items
        {
            get => _items;
            set => SetProperty( ref _items, value );
        }

        public ICommand RemoveCommand =>
            _removeCommand ?? ( _removeCommand = new RelayCommand( Remove, o => o != null ) );

        public ItemIDFilterEntry SelectedItem
        {
            get => _selectedItem;
            set => SetProperty( ref _selectedItem, value );
        }

        public ICommand SelectHueCommand =>
            _selectHueCommand ?? ( _selectHueCommand = new RelayCommandAsync( SelectHue, o => o != null ) );

        public ICommand TargetDestinationIDCommand =>
            _targetDestinationIDCommand ?? ( _targetDestinationIDCommand =
                new RelayCommandAsync( TargetDestinationID, o => o != null && Engine.Connected ) );

        public ICommand TargetSourceIDCommand =>
            _targetSourceIDCommand ?? ( _targetSourceIDCommand =
                new RelayCommandAsync( TargetSourceID, o => o != null && Engine.Connected ) );

        private static async Task TargetSourceID( object arg )
        {
            if ( arg is ItemIDFilterEntry entry )
            {
                entry.SourceID = await TargetItemID();
            }
        }

        private static async Task TargetDestinationID( object arg )
        {
            if ( arg is ItemIDFilterEntry entry )
            {
                entry.DestinationID = await TargetItemID();
            }
        }

        /// <summary>
        ///     Targeting a static gives the item ID directly; targeting a world item gives only a serial, so
        ///     the ID has to come from the item cache.
        /// </summary>
        private static async Task<int> TargetItemID()
        {
            ( _, _, int serial, int _, int _, int _, int itemId ) =
                await Commands.GetTargetInfoAsync( Strings.Target_object___, 90000, true );

            if ( itemId > 0 )
            {
                return itemId;
            }

            if ( serial <= 0 )
            {
                return 0;
            }

            Item item = Engine.Items.GetItem( serial );

            return item?.ID ?? 0;
        }

        private static async Task SelectHue( object obj )
        {
            if ( !( obj is ItemIDFilterEntry entry ) )
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

        private void Add( object obj )
        {
            Items.Add( new ItemIDFilterEntry() );
        }

        private void Remove( object obj )
        {
            if ( obj is ItemIDFilterEntry entry )
            {
                Items.Remove( entry );
            }
        }
    }
}
