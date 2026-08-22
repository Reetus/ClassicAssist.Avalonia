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
using ClassicAssist.UI.ViewModels;

namespace ClassicAssist.Avalonia.Views;

public partial class EntityCollectionViewer : Window
{
    public EntityCollectionViewer()
    {
        InitializeComponent();
    }

    private EntityCollectionViewerViewModel ViewModel => DataContext as EntityCollectionViewerViewModel;

    /// <summary>
    ///     Filter profiles have no explicit Save button - edits (renames, added/removed conditions) are
    ///     only persisted on close.
    ///     <para>
    ///         Still deliberately here rather than in <see cref="OnClosed" />, even though the detach
    ///         that made it necessary is handled at its source now: closing tears down the filter
    ///         DataGrid, and each Property cell's ComboBox gets its constraint list through an
    ///         ancestor binding that reports null once the cell is gone. That used to empty the
    ///         ComboBox, drop its selection, and write Property = null back through the two-way
    ///         binding, so a save after teardown persisted conditions with no property at all
    ///         (RetainedItemsSource is what keeps the list in place now). Saving before teardown means
    ///         this never depends on that behaviour in the first place.
    ///     </para>
    /// </summary>
    protected override void OnClosing( WindowClosingEventArgs e )
    {
        ViewModel?.SaveFilterProfiles();

        base.OnClosing( e );
    }

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

        if ( viewModel == null || sender is not ListBox listBox )
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
        if ( sender is not ListBox listBox ||
             !e.GetCurrentPoint( listBox ).Properties.IsRightButtonPressed )
        {
            return;
        }

        ListBoxItem item = ( e.Source as Control )?.FindAncestorOfType<ListBoxItem>( true );

        if ( item?.DataContext is not EntityCollectionData data || listBox.SelectedItems.Contains( data ) )
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

        if ( viewModel == null || item?.DataContext is not EntityCollectionData data )
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
