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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassicAssist.Avalonia.Controls;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.Avalonia.Views.Autoloot
{
    /// <summary>
    ///     A <see cref="MultiValueSelector" /> for item IDs: values are shown in hex and added by
    ///     targeting an item in the world.
    /// </summary>
    public partial class MultiItemIDSelector : UserControl
    {
        public static readonly StyledProperty<ObservableCollection<int>> ValuesProperty =
            AvaloniaProperty.Register<MultiItemIDSelector, ObservableCollection<int>>( nameof( Values ),
                new ObservableCollection<int>() );

        public MultiItemIDSelector()
        {
            InitializeComponent();

            MultiValueSelector selector = this.FindControl<MultiValueSelector>( "selector" );
            selector.Bind( MultiValueSelector.ValuesProperty, this.GetObservable( ValuesProperty ) );
        }

        public ObservableCollection<int> Values
        {
            get => GetValue( ValuesProperty );
            set => SetValue( ValuesProperty, value );
        }

        private async void OnTargetClick( object sender, RoutedEventArgs e )
        {
            ( _, _, int serial, int _, int _, int _, int itemId ) =
                await Commands.GetTargetInfoAsync( Strings.Target_object___, 90000, true );

            if ( itemId > 0 )
            {
                Add( itemId );
            }
            else if ( serial > 0 )
            {
                Item item = Engine.Items.GetItem( serial );

                if ( item != null )
                {
                    Add( item.ID );
                }
            }
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
