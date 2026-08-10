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
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using ClassicAssist.Avalonia.Controls;
using ClassicAssist.Avalonia.Misc;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UO.Data;

namespace ClassicAssist.Avalonia.Views.Autoloot;

/// <summary>
///     Chooses the Value-column editor for an autoloot/ECV constraint based on its
///     <see cref="PropertyEntry" />: an enum ComboBox (Layer / skill bonus), a multi-value selector
///     (ID (Multiple) / Cliloc (Multiple)), an organizer-entry combo (Autoloot Match), or a plain
///     editable int.
/// </summary>
public partial class AutolootValueControl : UserControl
{
    private static readonly IntToEnumConverter _intToEnumConverter = new();

    private AutolootConstraintEntry _entry;
    private Panel _grid;

    public AutolootValueControl()
    {
        InitializeComponent();

        _grid = this.FindControl<Panel>( "grid" );
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged( object sender, EventArgs e )
    {
        if ( _entry != null )
        {
            _entry.PropertyChanged -= OnEntryPropertyChanged;
        }

        _entry = DataContext as AutolootConstraintEntry;

        if ( _entry != null )
        {
            _entry.PropertyChanged += OnEntryPropertyChanged;
        }

        Rebuild();
    }

    private void OnEntryPropertyChanged( object sender, PropertyChangedEventArgs e )
    {
        if ( e.PropertyName == nameof( AutolootConstraintEntry.Property ) )
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _grid.Children.Clear();

        if ( _entry?.Property == null )
        {
            return;
        }

        PropertyEntry property = _entry.Property;

        // SkillBonusSkills is deliberately absent: it names the skill, which the predicate reads from
        // Additional, while Value carries the numeric bonus. Binding it here wrote the skill's enum
        // ordinal over the bonus amount and never set the skill at all.
        if ( property.AllowedValuesEnum == typeof( Layer ) || property.AllowedValuesEnum == typeof( TileFlags ) )
        {
            ComboBox comboBox = new() { ItemsSource = Enum.GetValues( property.AllowedValuesEnum ), HorizontalAlignment = HorizontalAlignment.Stretch };

            comboBox.Bind( ComboBox.SelectedItemProperty, new Binding
            {
                Source = _entry,
                Path = nameof( AutolootConstraintEntry.Value ),
                Mode = BindingMode.TwoWay,
                Converter = _intToEnumConverter,
                ConverterParameter = property.AllowedValuesEnum
            } );

            _grid.Children.Add( comboBox );

            return;
        }

        if ( property.Name == Strings.Cliloc__Multiple_ )
        {
            MultiClilocSelector control = new() { MinWidth = 40, HorizontalAlignment = HorizontalAlignment.Stretch };

            control.Bind( MultiClilocSelector.ValuesProperty, new Binding
            {
                Source = _entry,
                Path = nameof( AutolootConstraintEntry.Values ),
                Mode = BindingMode.TwoWay
            } );

            _grid.Children.Add( control );

            return;
        }

        if ( property.UseMultipleValues )
        {
            MultiItemIDSelector control = new() { MinWidth = 40, HorizontalAlignment = HorizontalAlignment.Stretch };

            control.Bind( MultiItemIDSelector.ValuesProperty, new Binding
            {
                Source = _entry,
                Path = nameof( AutolootConstraintEntry.Values ),
                Mode = BindingMode.TwoWay
            } );

            _grid.Children.Add( control );

            return;
        }

        if ( property.Name == Strings.Autoloot_Match )
        {
            ComboBox comboBox = new()
            {
                ItemsSource = AutolootManager.GetInstance().GetEntries().Select( ale => ale.Name ).ToList(),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            comboBox.Bind( ComboBox.SelectedItemProperty, new Binding
            {
                Source = _entry,
                Path = nameof( AutolootConstraintEntry.Additional ),
                Mode = BindingMode.TwoWay
            } );

            _grid.Children.Add( comboBox );

            return;
        }

        if ( property.Options != null )
        {
            ComboBox comboBox = new() { ItemsSource = property.Options, HorizontalAlignment = HorizontalAlignment.Stretch };

            comboBox.Bind( ComboBox.SelectedItemProperty, new Binding
            {
                Source = _entry,
                Path = nameof( AutolootConstraintEntry.Additional ),
                Mode = BindingMode.TwoWay
            } );

            _grid.Children.Add( comboBox );

            return;
        }

        // Everything left edits a single field as text. Constraints whose input is a string keep it in
        // Additional - Value is an int and silently discards anything typed into it.
        bool editsAdditional = property.Name == Strings.Name;

        EditTextBlock editTextBlock = new() { ShowIcon = true, HorizontalAlignment = HorizontalAlignment.Stretch };

        editTextBlock.Bind( EditTextBlock.TextProperty, new Binding
        {
            Source = _entry,
            Path = editsAdditional
                ? nameof( AutolootConstraintEntry.Additional )
                : nameof( AutolootConstraintEntry.Value ),
            Mode = BindingMode.TwoWay
        } );

        _grid.Children.Add( editTextBlock );
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}
