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
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace ClassicAssist.Avalonia.Controls;

/// <summary>
///     Flows <see cref="Items" /> top-to-bottom into as few columns as will fit the available height,
///     adding a column only while each one can still be at least <see cref="MinColumnWidth" /> wide.
///     Ported from the WPF tree's <c>ClassicAssist.Controls.ResponsiveGrid</c>.
///     <para>
///         Items live in their own collection rather than in <see cref="Panel.Children" /> because the
///         grid owns its children: each rebuild it generates one <see cref="StackPanel" /> per column
///         and reparents the items into it. Avalonia throws when a control is added to a second parent,
///         so an item is always detached from its previous column first - WPF is lenient here and the
///         original relied on it.
///     </para>
///     <para>
///         <see cref="MinColumnWidth" /> is a hard floor here. WPF instead raised its own MinWidth to
///         the widest item's desired width, which can't be reproduced directly: Avalonia measures a
///         wrapping TextBlock unconstrained as one long line, so the "natural" width of an options
///         group comes out large enough to collapse the layout to a single column.
///     </para>
/// </summary>
public class ResponsiveGrid : Grid
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<ResponsiveGrid, double>( nameof( MinColumnWidth ), 200 );

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ResponsiveGrid, double>( nameof( Spacing ), 5 );

    private bool _attached;
    private Size _lastRebuildSize;
    private bool _rebuilding;

    public ResponsiveGrid()
    {
        Items.CollectionChanged += OnItemsChanged;

        // Bounds, not SizeChanged: the WPF original hooked SizeChanged only after its first layout
        // pass to avoid rebuilding before ActualWidth/ActualHeight meant anything. Bounds gives the
        // same signal without the ordering dance, and Rebuild no-ops until it has a real size.
        this.GetObservable( BoundsProperty ).Subscribe( new AnonymousObserver<Rect>( _ => Rebuild() ) );
    }

    /// <summary>
    ///     The controls to lay out, populated from XAML via <c>&lt;ResponsiveGrid.Items&gt;</c>.
    /// </summary>
    [Content]
    public AvaloniaList<Control> Items { get; } = [];

    /// <summary>
    ///     A column is never made narrower than this; once the available width can't be split further
    ///     the layout stops adding columns and lets the columns scroll instead.
    /// </summary>
    public double MinColumnWidth
    {
        get => GetValue( MinColumnWidthProperty );
        set => SetValue( MinColumnWidthProperty, value );
    }

    /// <summary>Gap between columns, and between items within a column.</summary>
    public double Spacing
    {
        get => GetValue( SpacingProperty );
        set => SetValue( SpacingProperty, value );
    }

    private void OnItemsChanged( object sender, NotifyCollectionChangedEventArgs e )
    {
        _attached = false;
        _lastRebuildSize = default;

        Rebuild();
    }

    private void Rebuild()
    {
        if ( _rebuilding || Items.Count == 0 )
        {
            return;
        }

        double availableWidth = Bounds.Width;
        double availableHeight = Bounds.Height;

        if ( availableWidth <= 0 || availableHeight <= 0 )
        {
            return;
        }

        // Reparenting invalidates layout, which fires Bounds again; without this the grid would
        // rebuild itself forever.
        if ( Math.Abs( _lastRebuildSize.Width - availableWidth ) < 1 &&
             Math.Abs( _lastRebuildSize.Height - availableHeight ) < 1 )
        {
            return;
        }

        _lastRebuildSize = new Size( availableWidth, availableHeight );
        _rebuilding = true;

        try
        {
            // Items declared in XAML have no parent until we add them, and an unparented control
            // inherits no DataContext - measuring one would size an empty template and always come
            // out as a single column. Park them in the tree first so bindings resolve, then measure.
            if ( !_attached )
            {
                Populate( [[.. Items]], false );
                _attached = true;
            }

            int maxColumns = Math.Max( 1, (int) ( availableWidth / Math.Max( 1, MinColumnWidth ) ) );

            Dictionary<Control, Size> sizes = MeasureItems( availableWidth, availableHeight, maxColumns );

            List<List<Control>> columns = Distribute( sizes, availableHeight, maxColumns, out bool overflows );

            Populate( columns, overflows );
        }
        finally
        {
            _rebuilding = false;
        }
    }

    /// <summary>
    ///     Measures every item at the narrowest column width the layout could settle on, so the heights
    ///     used for packing are the heights items will actually have once their text has wrapped.
    /// </summary>
    private Dictionary<Control, Size> MeasureItems( double availableWidth, double availableHeight,
        int maxColumns )
    {
        double columnWidth = ( availableWidth - Spacing * ( maxColumns - 1 ) ) / maxColumns;

        Dictionary<Control, Size> sizes = new( Items.Count );

        foreach ( Control item in Items )
        {
            if ( sizes.ContainsKey( item ) )
            {
                continue;
            }

            item.InvalidateMeasure();
            item.Measure( new Size( columnWidth, availableHeight ) );

            sizes[item] = item.DesiredSize;
        }

        return sizes;
    }

    /// <summary>
    ///     Fills a column until the next item would overflow the available height, then starts another
    ///     one - but only while <see cref="MinColumnWidth" /> still allows it.
    ///     <paramref name="overflows" /> reports that the items didn't fit even at the maximum column
    ///     count, which is what puts the columns in scroll viewers.
    /// </summary>
    private List<List<Control>> Distribute( IReadOnlyDictionary<Control, Size> sizes, double availableHeight,
        int maxColumns, out bool overflows )
    {
        List<List<Control>> columns = [[]];

        double currentHeight = 0;

        overflows = false;

        // Iterate Items, not the dictionary: the declared order is the order the options appear in.
        foreach ( Control item in Items )
        {
            double itemHeight = sizes[item].Height + ( columns[columns.Count - 1].Count > 0 ? Spacing : 0 );

            if ( currentHeight > 0 && currentHeight + itemHeight > availableHeight )
            {
                if ( columns.Count < maxColumns )
                {
                    columns.Add( [] );
                    currentHeight = 0;
                    itemHeight = sizes[item].Height;
                }
                else
                {
                    overflows = true;
                }
            }

            columns[columns.Count - 1].Add( item );
            currentHeight += itemHeight;
        }

        return columns;
    }

    private void Populate( List<List<Control>> columns, bool overflows )
    {
        // Detach first: an item is still a child of the previous rebuild's StackPanel, and Avalonia
        // throws rather than silently reparenting.
        foreach ( Control item in Items )
        {
            ( item.Parent as Panel )?.Children.Remove( item );
        }

        Children.Clear();
        ColumnDefinitions.Clear();

        for ( int i = 0; i < columns.Count; i++ )
        {
            ColumnDefinitions.Add( new ColumnDefinition( 1, GridUnitType.Star ) );

            StackPanel stackPanel = new() { Spacing = Spacing };

            foreach ( Control item in columns[i] )
            {
                stackPanel.Children.Add( item );
            }

            Control columnRoot = overflows
                ? new ScrollViewer
                {
                    Content = stackPanel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                }
                : stackPanel;

            // Every column but the last gets a right gutter, so columns don't touch.
            columnRoot.Margin = i + 1 < columns.Count
                ? new Thickness( 0, 0, Spacing, 0 )
                : new Thickness( 0 );

            SetColumn( columnRoot, i );
            Children.Add( columnRoot );
        }
    }

    /// <summary>
    ///     Avalonia's IObservable overloads want an IObserver; there is no Action-taking Subscribe
    ///     without System.Reactive, which this project doesn't reference.
    /// </summary>
    private sealed class AnonymousObserver<T> : IObserver<T>
    {
        private readonly Action<T> _onNext;

        public AnonymousObserver( Action<T> onNext )
        {
            _onNext = onNext;
        }

        public void OnCompleted()
        {
        }

        public void OnError( Exception error )
        {
        }

        public void OnNext( T value )
        {
            _onNext( value );
        }
    }
}
