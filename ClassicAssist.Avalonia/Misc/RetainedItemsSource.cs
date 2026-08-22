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

using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     Feeds an <see cref="ItemsControl" />'s <see cref="ItemsControl.ItemsSource" /> from a binding
///     while ignoring the nulls that binding reports when the control detaches - use it in place of
///     ItemsSource inside item templates.
///     <para>
///         A list bound by walking to an ancestor (<c>$parent[Window].DataContext.Something</c>) goes
///         null the moment the control leaves the visual tree, which happens routinely inside item
///         templates: a DataGrid swapping its ItemsSource, a TreeView collapsing a node. An emptied
///         ComboBox clears its SelectedItem, and a two-way SelectedItem binding then writes that null
///         back into the model, silently wiping what the user picked (the ECV filter editor lost every
///         condition's Property this way as soon as another group was selected).
///     </para>
/// </summary>
public static class RetainedItemsSource
{
    public static readonly AttachedProperty<IEnumerable> SourceProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, IEnumerable>( "Source", typeof( RetainedItemsSource ) );

    static RetainedItemsSource()
    {
        SourceProperty.Changed.AddClassHandler<ItemsControl, IEnumerable>( OnSourceChanged );
    }

    public static IEnumerable GetSource( ItemsControl control )
    {
        return control.GetValue( SourceProperty );
    }

    public static void SetSource( ItemsControl control, IEnumerable value )
    {
        control.SetValue( SourceProperty, value );
    }

    private static void OnSourceChanged( ItemsControl control, AvaloniaPropertyChangedEventArgs<IEnumerable> e )
    {
        // The whole point: the last real list stays in place, so the selection is never cleared and
        // nothing is written back. These lists live as long as the view model, so there is no case
        // where clearing one is what was actually meant.
        if ( e.NewValue.Value == null )
        {
            return;
        }

        control.ItemsSource = e.NewValue.Value;
    }
}
