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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassicAssist.Avalonia.Controls;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UI.ViewModels.Autoloot;
using ClassicAssist.Shared.UO;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.Avalonia.Views.Autoloot
{
    /// <summary>
    ///     A <see cref="MultiValueSelector" /> for clilocs: entries show their localized text and are
    ///     added by choosing from the cliloc list or picking a property off a targeted item.
    /// </summary>
    public partial class MultiClilocSelector : UserControl
    {
        public static readonly StyledProperty<ObservableCollection<int>> ValuesProperty =
            AvaloniaProperty.Register<MultiClilocSelector, ObservableCollection<int>>( nameof( Values ),
                new ObservableCollection<int>() );

        public MultiClilocSelector()
        {
            InitializeComponent();

            MultiValueSelector selector = this.FindControl<MultiValueSelector>( "selector" );
            selector.Bind( MultiValueSelector.ValuesProperty, this.GetObservable( ValuesProperty ) );
            selector.ItemDisplayFactory = v => $"{v} ({Cliloc.GetProperty( v )})";
        }

        public ObservableCollection<int> Values
        {
            get => GetValue( ValuesProperty );
            set => SetValue( ValuesProperty, value );
        }

        private async void OnChooseFromItemClick( object sender, RoutedEventArgs e )
        {
            int serial = await Commands.GetTargetSerialAsync( Strings.Target_object___, 90000 );

            if ( serial == 0 )
            {
                Commands.SystemMessage( Strings.Cannot_find_item___ );
                return;
            }

            Item item = Engine.Items.GetItem( serial );

            if ( item == null )
            {
                Commands.SystemMessage( Strings.Cannot_find_item___ );
                return;
            }

            if ( item.Properties == null )
            {
                Commands.SystemMessage( Strings.Item_properties_null_or_not_loaded___ );
                return;
            }

            PropertySelectionViewModel vm = new PropertySelectionViewModel( item.Properties );
            await Engine.UIInvoker.InvokeDialog( "PropertySelectionWindow", dataContext: vm );

            if ( vm.DialogResult != MessageBoxResult.OK )
            {
                return;
            }

            foreach ( SelectProperties property in vm.Properties.Where( p => p.Selected ) )
            {
                Add( property.Property.Cliloc );
            }
        }

        private async void OnChooseClilocClick( object sender, RoutedEventArgs e )
        {
            ClilocSelectionViewModel vm = new ClilocSelectionViewModel();

            // Must be awaited: InvokeDialog completes when the dialog closes, so without this the
            // DialogResult check below runs before the user has even seen the window and always
            // takes the early return.
            await Engine.UIInvoker.InvokeDialog( "ClilocSelectionWindow", dataContext: vm );

            if ( vm.DialogResult != MessageBoxResult.OK )
            {
                return;
            }

            Add( vm.SelectedCliloc.Key );
        }

        private void Add( int value )
        {
            if ( Values == null )
            {
                Values = new ObservableCollection<int>();
            }

            if ( !Values.Contains( value ) )
            {
                Values.Add( value );
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load( this );
        }
    }
}
