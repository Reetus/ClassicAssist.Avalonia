using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Shared.UI.ViewModels.Agents;

namespace ClassicAssist.Avalonia.Views.Agents;

public partial class AutolootTabControl : UserControl
{
    private const double DRAG_THRESHOLD = 5;

    private static readonly DataFormat<string> DragFormat =
        DataFormat.CreateStringApplicationFormat( "ClassicAssist.AutolootEntry" );

    private AutolootEntry _draggedEntry;
    private AutolootEntry _dragEntry;
    private PointerPressedEventArgs _dragStartArgs;
    private Point _dragStartPoint;

    public AutolootTabControl()
    {
        this.InitializeComponent();

        TreeView treeView = this.FindControl<TreeView>( "treeView" );

        if ( treeView != null )
        {
            DragDrop.SetAllowDrop( treeView, true );
            treeView.AddHandler( DragDrop.DragOverEvent, OnTreeDragOver );
            treeView.AddHandler( DragDrop.DropEvent, OnTreeDrop );
        }

        // handledEventsToo: the TreeView marks PointerPressed as handled for its own selection logic,
        // which would otherwise stop the event bubbling to this drag-initiation handler.
        AddHandler( PointerPressedEvent, OnEntryPointerPressed, RoutingStrategies.Bubble, true );
        AddHandler( PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble, true );
    }

    private AutolootViewModel ViewModel => DataContext as AutolootViewModel;

    /// <summary>
    ///     The Insert button opens its own context menu (ID from target / Match any ID) rather than
    ///     running one action directly - matches the WPF autoloot tab, where the button is a menu
    ///     trigger and both insertion modes live in the menu.
    /// </summary>
    private void OnInsertClick( object sender, RoutedEventArgs e )
    {
        ContextMenu menu = this.FindControl<ContextMenu>( "insertMenu" );
        Button button = this.FindControl<Button>( "insertButton" );

        if ( menu != null && button != null )
        {
            menu.Open( button );
        }
    }

    private void OnEntryPointerPressed( object sender, PointerPressedEventArgs e )
    {
        if ( !e.GetCurrentPoint( this ).Properties.IsLeftButtonPressed )
        {
            return;
        }

        TreeViewItem item = ( e.Source as Control )?.FindAncestorOfType<TreeViewItem>( true );

        if ( item?.DataContext is AutolootEntry entry )
        {
            _dragEntry = entry;
            _dragStartArgs = e;
            _dragStartPoint = e.GetPosition( this );
        }
    }

    private async void OnPointerMoved( object sender, PointerEventArgs e )
    {
        if ( _dragEntry == null || _dragStartArgs == null )
        {
            return;
        }

        Point position = e.GetPosition( this );

        if ( Math.Abs( position.X - _dragStartPoint.X ) < DRAG_THRESHOLD &&
             Math.Abs( position.Y - _dragStartPoint.Y ) < DRAG_THRESHOLD )
        {
            return;
        }

        PointerPressedEventArgs startArgs = _dragStartArgs;

        _draggedEntry = _dragEntry;
        _dragEntry = null;
        _dragStartArgs = null;

        // DataTransfer only carries strings and bytes, so the payload is just a marker that the drag
        // came from this tree; the entry itself stays in _draggedEntry until the drag ends.
        DataTransfer data = new();
        data.Add( DataTransferItem.Create( DragFormat, string.Empty ) );

        try
        {
            await DragDrop.DoDragDropAsync( startArgs, data, DragDropEffects.Move );
        }
        finally
        {
            _draggedEntry = null;
        }
    }

    private void OnTreeDragOver( object sender, DragEventArgs e )
    {
        e.DragEffects = DragDropEffects.None;

        if ( _draggedEntry == null || !e.DataTransfer.Contains( DragFormat ) )
        {
            return;
        }

        TreeViewItem item = ( e.Source as Control )?.FindAncestorOfType<TreeViewItem>( true );

        if ( item?.DataContext is AutolootGroup || item == null )
        {
            // Over a group row -> move into it; over empty tree space -> move back to root.
            e.DragEffects = DragDropEffects.Move;
        }
    }

    private void OnTreeDrop( object sender, DragEventArgs e )
    {
        if ( _draggedEntry is not { } entry || !e.DataTransfer.Contains( DragFormat ) )
        {
            return;
        }

        TreeViewItem item = ( e.Source as Control )?.FindAncestorOfType<TreeViewItem>( true );

        if ( item?.DataContext is AutolootGroup group )
        {
            ViewModel?.MoveToGroup( entry, group );
        }
        else if ( item == null )
        {
            ViewModel?.MoveToRoot( entry );
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}
