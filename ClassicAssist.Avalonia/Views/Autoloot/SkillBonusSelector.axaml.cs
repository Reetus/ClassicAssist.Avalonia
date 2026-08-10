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
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.UI.Misc;

namespace ClassicAssist.Avalonia.Views.Autoloot;

/// <summary>
///     Picks the skill for a skill-bonus constraint, which the predicate matches by name out of
///     <see cref="AutolootConstraintEntry.Additional" /> while Value carries the numeric bonus.
///     <para>
///         Sits under the Property ComboBox rather than in the Value column, because those constraints
///         need both fields at once and the Value column only has room for one editor - the same place
///         old puts it.
///     </para>
/// </summary>
public partial class SkillBonusSelector : UserControl
{
    private static readonly Lazy<List<string>> _skills = new( BuildSkillNames );

    private AutolootConstraintEntry _entry;

    public SkillBonusSelector()
    {
        InitializeComponent();

        ComboBox comboBox = this.FindControl<ComboBox>( "skills" );

        comboBox?.ItemsSource = _skills.Value;

        IsVisible = false;
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    ///     Enum names, preferring <see cref="DescriptionAttribute" /> where one is present, so the list
    ///     reads "Animal Lore" rather than "AnimalLore". These strings are what gets stored in
    ///     Additional, and what the predicate compares against the property text.
    /// </summary>
    private static List<string> BuildSkillNames()
    {
        List<string> names = [];

        foreach ( object value in Enum.GetValues( typeof( SkillBonusSkills ) ) )
        {
            FieldInfo fieldInfo = typeof( SkillBonusSkills ).GetField( value.ToString() );

            DescriptionAttribute description = fieldInfo
                ?.GetCustomAttributes( typeof( DescriptionAttribute ), false ).FirstOrDefault() as
                DescriptionAttribute;

            names.Add( description?.Description ?? value.ToString() );
        }

        return names;
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
        // Re-evaluated on every property change, not just when the DataContext is swapped: the row
        // stays put while the user picks a different constraint from the Property ComboBox.
        IsVisible = _entry?.Property?.AllowedValuesEnum == typeof( SkillBonusSkills );

        ComboBox comboBox = this.FindControl<ComboBox>( "skills" );

        if ( comboBox == null )
        {
            return;
        }

        if ( !IsVisible || _entry == null )
        {
            comboBox.ClearValue( SelectingItemsControl.SelectedItemProperty );

            return;
        }

        comboBox.Bind( SelectingItemsControl.SelectedItemProperty, new Binding
        {
            Source = _entry,
            Path = nameof( AutolootConstraintEntry.Additional ),
            Mode = BindingMode.TwoWay
        } );
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}
