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
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ClassicAssist.Data;
using ClassicAssist.UI.ViewModels;

namespace ClassicAssist.Avalonia.Views
{
    public partial class EntityCollectionViewer : Window
    {
        public EntityCollectionViewer()
        {
            InitializeComponent();

            Topmost = Options.CurrentOptions.AlwaysOnTop;
        }

        private EntityCollectionViewerViewModel ViewModel => DataContext as EntityCollectionViewerViewModel;

        protected override void OnClosed( EventArgs e )
        {
            // The view model listens to a collection that outlives the window, so it has to let go here or
            // every item the server sends keeps rebuilding a list nobody is looking at.
            ViewModel?.Cleanup();

            base.OnClosed( e );
        }

        /// <summary>
        ///     Mirrors the list's selection into the view model, which the status bar counts.
        /// </summary>
        private void OnSelectionChanged( object sender, SelectionChangedEventArgs e )
        {
            EntityCollectionViewerViewModel viewModel = ViewModel;

            if ( viewModel == null || !( sender is ListBox listBox ) )
            {
                return;
            }

            viewModel.SelectedItems.Clear();

            foreach ( EntityCollectionData data in listBox.SelectedItems.OfType<EntityCollectionData>() )
            {
                viewModel.SelectedItems.Add( data );
            }
        }

        /// <summary>
        ///     Right-clicking an item outside the current selection collapses the selection to just that
        ///     item, so the context menu it opens operates on what you actually right-clicked rather than
        ///     whatever was left selected from before. Right-clicking within an existing multi-selection
        ///     leaves it untouched.
        /// </summary>
        private void OnItemPointerPressed( object sender, PointerPressedEventArgs e )
        {
            if ( !( sender is ListBox listBox ) ||
                 !e.GetCurrentPoint( listBox ).Properties.IsRightButtonPressed )
            {
                return;
            }

            ListBoxItem item = ( e.Source as Control )?.FindAncestorOfType<ListBoxItem>( true );

            if ( !( item?.DataContext is EntityCollectionData data ) || listBox.SelectedItems.Contains( data ) )
            {
                return;
            }

            listBox.SelectedItem = data;
        }

        private void OnItemDoubleTapped( object sender, TappedEventArgs e )
        {
            EntityCollectionViewerViewModel viewModel = ViewModel;

            // The tap lands on whatever part of the tile was hit, so walk up to the row it belongs to
            // rather than trusting the sender.
            ListBoxItem item = ( e.Source as Control )?.FindAncestorOfType<ListBoxItem>( true );

            if ( viewModel == null || !( item?.DataContext is EntityCollectionData data ) )
            {
                return;
            }

            if ( viewModel.ItemDoubleClickCommand.CanExecute( data ) )
            {
                viewModel.ItemDoubleClickCommand.Execute( data );
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load( this );
        }
    }
}
