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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Shared.Resources;

namespace ClassicAssist.UI.ViewModels.Hotkeys
{
    /// <summary>
    ///     Backs the hotkey Options dialog: reflects every <see cref="HotkeyConfigurationAttribute" />
    ///     property on the selected entry into a bindable row, and writes the chosen values back on OK.
    ///     <para>
    ///         WPF builds this dialog imperatively - a Grid of TextBlock/ComboBox pairs assembled in the
    ///         window's constructor. Avalonia binds an ItemsControl to <see cref="Entries" /> instead, so
    ///         the shape of the dialog stays in XAML and this stays testable without a UI thread.
    ///     </para>
    /// </summary>
    public class HotkeyOptionsViewModel : BaseViewModel
    {
        private readonly HotkeyEntry _hotkeyEntry;
        private ICommand _okCommand;

        /// <summary>
        ///     Parameterless ctor for the XAML designer only - a dialog opened this way has no rows.
        /// </summary>
        public HotkeyOptionsViewModel()
        {
        }

        public HotkeyOptionsViewModel( HotkeyEntry hotkeyEntry )
        {
            _hotkeyEntry = hotkeyEntry;

            if ( hotkeyEntry == null )
            {
                return;
            }

            IEnumerable<PropertyInfo> properties = hotkeyEntry.GetType().GetProperties()
                .Where( prop => prop.IsDefined( typeof( HotkeyConfigurationAttribute ), false ) );

            foreach ( PropertyInfo property in properties )
            {
                HotkeyConfigurationAttribute attribute =
                    property.GetCustomAttribute<HotkeyConfigurationAttribute>();

                // Only Enum is handled, matching upstream. Anything else is skipped rather than thrown
                // on: an unsupported property should cost you that one row, not the whole dialog.
                if ( attribute?.BaseType != typeof( Enum ) || attribute.Type == null || !attribute.Type.IsEnum )
                {
                    continue;
                }

                Entries.Add( new HotkeyOptionEntry( property, attribute, property.GetValue( hotkeyEntry ) ) );
            }
        }

        public ObservableCollection<HotkeyOptionEntry> Entries { get; } =
            new ObservableCollection<HotkeyOptionEntry>();

        public ICommand OkCommand => _okCommand ?? ( _okCommand = new RelayCommand( Ok, o => true ) );

        private void Ok( object obj )
        {
            if ( _hotkeyEntry == null )
            {
                return;
            }

            foreach ( HotkeyOptionEntry entry in Entries )
            {
                entry.Apply( _hotkeyEntry );
            }
        }
    }

    /// <summary>
    ///     One configurable property: its label, the values it can take, and the value currently chosen.
    /// </summary>
    public class HotkeyOptionEntry : BaseViewModel
    {
        private readonly PropertyInfo _property;
        private HotkeyOptionValue _selectedValue;

        public HotkeyOptionEntry( PropertyInfo property, HotkeyConfigurationAttribute attribute, object value )
        {
            _property = property;

            // Upstream shows the attribute name verbatim rather than through the resource manager, and
            // there is no resource for "Cure Type" to look up, so this matches.
            Name = attribute.Name ?? property.Name;

            foreach ( object enumValue in Enum.GetValues( attribute.Type ) )
            {
                HotkeyOptionValue optionValue = new HotkeyOptionValue( enumValue );

                Values.Add( optionValue );

                if ( Equals( enumValue, value ) )
                {
                    _selectedValue = optionValue;
                }
            }

            // A stored value outside the enum leaves nothing selected; fall back to the first entry so
            // the ComboBox is never blank.
            if ( _selectedValue == null )
            {
                _selectedValue = Values.FirstOrDefault();
            }
        }

        public string Name { get; }

        public HotkeyOptionValue SelectedValue
        {
            get => _selectedValue;
            set => SetProperty( ref _selectedValue, value );
        }

        public ObservableCollection<HotkeyOptionValue> Values { get; } =
            new ObservableCollection<HotkeyOptionValue>();

        public void Apply( object target )
        {
            if ( SelectedValue != null )
            {
                _property.SetValue( target, SelectedValue.Value );
            }
        }
    }

    /// <summary>
    ///     An enum member paired with the text to show for it.
    /// </summary>
    public class HotkeyOptionValue
    {
        public HotkeyOptionValue( object value )
        {
            Value = value;

            DescriptionAttribute description = value.GetType().GetMember( value.ToString() ).FirstOrDefault()
                ?.GetCustomAttribute<DescriptionAttribute>();

            string name = description?.Description ?? value.ToString();

            // Upstream throws when a name has no resource. Here a missing translation falls back to the
            // description itself - a dialog that refuses to open is worse than an untranslated label,
            // especially as the exception would be swallowed by the invoker.
            string localized = Strings.ResourceManager.GetString( name );

            DisplayName = string.IsNullOrEmpty( localized ) ? name : localized;
        }

        public string DisplayName { get; }

        public object Value { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
