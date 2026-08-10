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

using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ClassicAssist.Data.Macros;
using ClassicAssist.Shared;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.Avalonia.Views.Macros;

/// <summary>
///     Floating Resume/Step/Stop overlay + resizable frame-variable panel shown while a macro is
///     paused at a breakpoint. Ported from the WPF tree's <c>MacrosDebuggerControl</c>.
/// </summary>
public partial class MacrosDebuggerControl : UserControl
{
    public static readonly StyledProperty<MacroEntry> MacroEntryProperty =
        AvaloniaProperty.Register<MacrosDebuggerControl, MacroEntry>( nameof( MacroEntry ) );

    public static readonly StyledProperty<double> OverlayWidthProperty =
        AvaloniaProperty.Register<MacrosDebuggerControl, double>( nameof( OverlayWidth ), 250.0 );
    private double _startWidth;

    public MacrosDebuggerControl()
    {
        InitializeComponent();
    }

    public MacroEntry MacroEntry
    {
        get => GetValue( MacroEntryProperty );
        set => SetValue( MacroEntryProperty, value );
    }

    public double OverlayWidth
    {
        get => GetValue( OverlayWidthProperty );
        set => SetValue( OverlayWidthProperty, value );
    }

    public ICommand ResumeCommand => field ??= new RelayCommand( _ => MacroEntry?.Resume() );

    public ICommand StepCommand => field ??= new RelayCommand( _ => MacroEntry?.Step() );

    public ICommand StopCommand => field ??= new RelayCommand( _ => MacroEntry?.Stop() );

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }

    private void OnVariablesGridDoubleTapped( object sender, TappedEventArgs e )
    {
        if ( sender is not DataGrid grid ||
             grid.SelectedItem is not KeyValuePair<string, object> kvp || kvp.Value is not Entity entity )
        {
            return;
        }

        _ = Engine.UIInvoker.Invoke( "ObjectInspectorWindow", null, typeof( ObjectInspectorViewModel ),
            [entity] );
    }

    private async void OnVariablesGridKeyDown( object sender, KeyEventArgs e )
    {
        if ( e.Key != Key.C || e.KeyModifiers != KeyModifiers.Control )
        {
            return;
        }

        if ( sender is not DataGrid grid || grid.SelectedItem is not KeyValuePair<string, object> kvp )
        {
            return;
        }

        Engine.UIInvoker.SetClipboardText( MacroInvoker.GetDisplayValue( kvp.Value, true ) );

        e.Handled = true;

        await Task.CompletedTask;
    }

    private void ResizeThumb_DragStarted( object sender, VectorEventArgs e )
    {
        _startWidth = OverlayWidth;
    }

    private void ResizeThumb_DragDelta( object sender, VectorEventArgs e )
    {
        // Move left increases width, move right decreases (the panel is anchored to the right edge).
        double delta = -e.Vector.X;

        OverlayWidth = _startWidth + delta;

        _startWidth = OverlayWidth;
    }
}
