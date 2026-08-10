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
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;

namespace ClassicAssist.Avalonia.Controls;

/// <summary>
///     Label on the left, content filling the rest of the row. Ported from the WPF tree's
///     <c>ClassicAssist.Controls.Headered.HorizontalHeaderedContentControl</c>; the template lives in
///     HorizontalHeadered.Theme.xaml.
/// </summary>
public class HorizontalHeaderedContentControl : HeaderedContentControl
{
    public static readonly StyledProperty<Thickness> HeaderMarginProperty =
        AvaloniaProperty.Register<HorizontalHeaderedContentControl, Thickness>( nameof( HeaderMargin ),
            new Thickness( 0, 0, 5, 0 ) );

    public static readonly StyledProperty<double> HeaderMinWidthProperty =
        AvaloniaProperty.Register<HorizontalHeaderedContentControl, double>( nameof( HeaderMinWidth ) );

    public static readonly StyledProperty<double> HeaderWidthProperty =
        AvaloniaProperty.Register<HorizontalHeaderedContentControl, double>( nameof( HeaderWidth ),
            double.NaN );

    public Thickness HeaderMargin
    {
        get => GetValue( HeaderMarginProperty );
        set => SetValue( HeaderMarginProperty, value );
    }

    public double HeaderMinWidth
    {
        get => GetValue( HeaderMinWidthProperty );
        set => SetValue( HeaderMinWidthProperty, value );
    }

    public double HeaderWidth
    {
        get => GetValue( HeaderWidthProperty );
        set => SetValue( HeaderWidthProperty, value );
    }
}

/// <summary>
///     <see cref="HorizontalHeaderedContentControl" /> pre-filled with a TextBox bound to
///     <see cref="Text" />, so option rows don't have to spell out the label/box pair each time.
/// </summary>
public class HorizontalHeaderedTextBox : HorizontalHeaderedContentControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<HorizontalHeaderedTextBox, string>( nameof( Text ),
            defaultBindingMode: BindingMode.TwoWay );

    public HorizontalHeaderedTextBox()
    {
        TextBox textBox = new();
        textBox.Bind( TextBox.TextProperty,
            new Binding( nameof( Text ) ) { Source = this, Mode = BindingMode.TwoWay } );

        Content = textBox;
    }

    public string Text
    {
        get => GetValue( TextProperty );
        set => SetValue( TextProperty, value );
    }
}

/// <summary>
///     <see cref="HorizontalHeaderedContentControl" /> pre-filled with a ComboBox bound to
///     <see cref="ItemsSource" /> / <see cref="SelectedItem" />.
/// </summary>
public class HorizontalHeaderedComboBox : HorizontalHeaderedContentControl
{
    public static readonly StyledProperty<IEnumerable> ItemsSourceProperty =
        AvaloniaProperty.Register<HorizontalHeaderedComboBox, IEnumerable>( nameof( ItemsSource ) );

    public static readonly StyledProperty<object> SelectedItemProperty =
        AvaloniaProperty.Register<HorizontalHeaderedComboBox, object>( nameof( SelectedItem ),
            defaultBindingMode: BindingMode.TwoWay );

    public HorizontalHeaderedComboBox()
    {
        ComboBox comboBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

        comboBox.Bind( ItemsControl.ItemsSourceProperty,
            new Binding( nameof( ItemsSource ) ) { Source = this } );
        comboBox.Bind( SelectingItemsControl.SelectedItemProperty,
            new Binding( nameof( SelectedItem ) ) { Source = this, Mode = BindingMode.TwoWay } );

        Content = comboBox;
    }

    public IEnumerable ItemsSource
    {
        get => GetValue( ItemsSourceProperty );
        set => SetValue( ItemsSourceProperty, value );
    }

    public object SelectedItem
    {
        get => GetValue( SelectedItemProperty );
        set => SetValue( SelectedItemProperty, value );
    }
}
