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

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace ClassicAssist.Avalonia.Controls
{
    /// <summary>
    ///     A checkbox with an extra <see cref="ChildContent" /> control to its right - typically the
    ///     numeric field the option applies to - which is enabled only while the box is checked. Ported
    ///     from the WPF tree's <c>ClassicAssist.Controls.OptionedCheckBoxControl</c>.
    ///     <para>
    ///         WPF derived from CheckBox and rebuilt <c>Content</c> into a DockPanel in code, which meant
    ///         guarding against re-capturing its own composed content as the label, and left the child
    ///         inside the checkbox's hit area so clicking the textbox toggled the option. This is a
    ///         composite instead: the CheckBox and the child are siblings in the control theme
    ///         (OptionedCheckBox.Theme.xaml), so there is no content juggling and the child is not a
    ///         click target for the box.
    ///     </para>
    /// </summary>
    public class OptionedCheckBox : TemplatedControl
    {
        public static readonly StyledProperty<object> ChildContentProperty =
            AvaloniaProperty.Register<OptionedCheckBox, object>( nameof( ChildContent ) );

        public static readonly StyledProperty<ICommand> CommandProperty =
            AvaloniaProperty.Register<OptionedCheckBox, ICommand>( nameof( Command ) );

        public static readonly StyledProperty<object> CommandParameterProperty =
            AvaloniaProperty.Register<OptionedCheckBox, object>( nameof( CommandParameter ) );

        public static readonly StyledProperty<object> ContentProperty =
            AvaloniaProperty.Register<OptionedCheckBox, object>( nameof( Content ) );

        public static readonly StyledProperty<bool> IsCheckedProperty =
            AvaloniaProperty.Register<OptionedCheckBox, bool>( nameof( IsChecked ),
                defaultBindingMode: BindingMode.TwoWay );

        /// <summary>The control shown to the right of the label; disabled while unchecked.</summary>
        public object ChildContent
        {
            get => GetValue( ChildContentProperty );
            set => SetValue( ChildContentProperty, value );
        }

        public ICommand Command
        {
            get => GetValue( CommandProperty );
            set => SetValue( CommandProperty, value );
        }

        public object CommandParameter
        {
            get => GetValue( CommandParameterProperty );
            set => SetValue( CommandParameterProperty, value );
        }

        /// <summary>The checkbox label.</summary>
        public object Content
        {
            get => GetValue( ContentProperty );
            set => SetValue( ContentProperty, value );
        }

        public bool IsChecked
        {
            get => GetValue( IsCheckedProperty );
            set => SetValue( IsCheckedProperty, value );
        }
    }
}
