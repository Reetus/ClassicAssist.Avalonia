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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassicAssist.UI.ViewModels;

namespace ClassicAssist.Avalonia.Controls;

/// <summary>
///     A compact multi-value editor used by the multi-value autoloot/ECV constraints: a collapsed
///     comma-joined summary, a popup to add values (typed, or via injected buttons like target-item
///     or choose-cliloc) and remove existing ones. Backed by an <see cref="ObservableCollection{T}" />
///     of ints.
/// </summary>
public partial class MultiValueSelector : UserControl
{
    public static readonly StyledProperty<ObservableCollection<int>> ValuesProperty =
        AvaloniaProperty.Register<MultiValueSelector, ObservableCollection<int>>( nameof( Values ),
            [] );

    public static readonly StyledProperty<bool> HexDisplayProperty =
        AvaloniaProperty.Register<MultiValueSelector, bool>( nameof( HexDisplay ) );

    public static readonly StyledProperty<int> PopupWidthProperty =
        AvaloniaProperty.Register<MultiValueSelector, int>( nameof( PopupWidth ), 260 );

    public static readonly StyledProperty<object> ButtonsProperty =
        AvaloniaProperty.Register<MultiValueSelector, object>( nameof( Buttons ) );

    private RelayCommand _removeItemCommand;
    private Popup _popup;
    private TextBox _textBox;
    private TextBlock _valuesTextBlock;

    public MultiValueSelector()
    {
        InitializeComponent();

        _valuesTextBlock = this.FindControl<TextBlock>( "valuesTextBlock" );
        _textBox = this.FindControl<TextBox>( "textBox" );
        _popup = this.FindControl<Popup>( "popup" );

        RebuildDisplayItems();
    }

    /// <summary>Custom per-value display; defaults to hex (when <see cref="HexDisplay" />) or decimal.</summary>
    public Func<int, string> ItemDisplayFactory { get; set; }

    public object Buttons
    {
        get => GetValue( ButtonsProperty );
        set => SetValue( ButtonsProperty, value );
    }

    public ObservableCollection<MultiValueDisplayItem> DisplayItems { get; } = [];

    public bool HexDisplay
    {
        get => (bool) GetValue( HexDisplayProperty );
        set => SetValue( HexDisplayProperty, value );
    }

    public int PopupWidth
    {
        get => (int) GetValue( PopupWidthProperty );
        set => SetValue( PopupWidthProperty, value );
    }

    public ObservableCollection<int> Values
    {
        get => GetValue( ValuesProperty );
        set => SetValue( ValuesProperty, value );
    }

    public ICommand RemoveItemCommand =>
        _removeItemCommand ??= new RelayCommand( v =>
        {
            if ( v is MultiValueDisplayItem item )
            {
                Values?.Remove( item.Value );
            }
        } );

    protected override void OnPropertyChanged( AvaloniaPropertyChangedEventArgs change )
    {
        base.OnPropertyChanged( change );

        if ( change.Property == ValuesProperty )
        {
            if ( change.OldValue is ObservableCollection<int> oldCollection )
            {
                oldCollection.CollectionChanged -= OnValuesChanged;
            }

            if ( change.NewValue is ObservableCollection<int> newCollection )
            {
                newCollection.CollectionChanged += OnValuesChanged;
            }

            RebuildDisplayItems();
        }
        else if ( change.Property == HexDisplayProperty )
        {
            RebuildDisplayItems();
        }
    }

    private void OnValuesChanged( object sender, NotifyCollectionChangedEventArgs e )
    {
        RebuildDisplayItems();
    }

    private void RebuildDisplayItems()
    {
        DisplayItems.Clear();

        if ( Values == null )
        {
            return;
        }

        foreach ( int value in Values )
        {
            string display = ItemDisplayFactory != null
                ? ItemDisplayFactory( value )
                : HexDisplay ? $"0x{value:x}" : $"{value}";

            DisplayItems.Add( new MultiValueDisplayItem { Value = value, Display = display } );
        }

        _valuesTextBlock?.Text = string.Join( ", ", DisplayItems.Select( i => i.Display ) );
    }

    private void OnEllipsisClick( object sender, RoutedEventArgs e )
    {
        _popup.IsOpen = !_popup.IsOpen;

        if ( _popup.IsOpen )
        {
            _textBox.Focus();
        }
    }

    private void OnTextBoxKeyDown( object sender, KeyEventArgs e )
    {
        if ( e.Key != Key.Enter )
        {
            return;
        }

        string text = _textBox.Text?.Trim();

        if ( !string.IsNullOrEmpty( text ) && int.TryParse( text.StartsWith( "0x", StringComparison.CurrentCultureIgnoreCase )
                ? text[2..]
                : text,
            text.StartsWith( "0x", StringComparison.CurrentCultureIgnoreCase ) ? NumberStyles.HexNumber : NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value ) )
        {
            Values ??= [];

            if ( !Values.Contains( value ) )
            {
                Values.Add( value );
            }
        }

        _textBox.Text = string.Empty;
        e.Handled = true;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}

public class MultiValueDisplayItem
{
    public string Display { get; set; }
    public int Value { get; set; }
}
