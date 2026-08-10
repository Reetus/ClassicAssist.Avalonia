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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ClassicAssist.Avalonia.Views;

/// <summary>
///     The loading splash, shown from <see cref="App.OnFrameworkInitializationCompleted" /> until the
///     main window is built. Its job is to make it obvious the assistant has not finished loading, so
///     the client is not logged into before its hotkeys, macros and filters are live.
///     <para>
///         WPF gets a shaped window from <c>AllowsTransparency</c>, which is always available on
///         Windows. Here transparency depends on a running compositor - fine on macOS and on a Wayland
///         or composited X11 desktop, but a bare X11 session has none, and the logo's alpha would then
///         composite against black. <see cref="OnOpened" /> checks what the platform actually granted
///         and paints an opaque backdrop when the request was refused.
///     </para>
/// </summary>
public partial class SplashWindow : Window
{
    /// <summary>Matches ThemeBackgroundBrush; only used when the platform refuses transparency.</summary>
    private static readonly IBrush _opaqueBackdrop = new SolidColorBrush( Color.FromRgb( 0x27, 0x27, 0x27 ) );

    private Border _backdrop;

    public SplashWindow()
    {
        InitializeComponent();

        _backdrop = this.FindControl<Border>( "backdrop" );
    }

    protected override void OnOpened( EventArgs e )
    {
        base.OnOpened( e );

        ApplyTransparencyFallback();
    }

    protected override void OnPropertyChanged( AvaloniaPropertyChangedEventArgs change )
    {
        base.OnPropertyChanged( change );

        // X11 can hand transparency back late (or take it away when a compositor stops), and the
        // level granted at Opened is not always the final answer.
        if ( change.Property == ActualTransparencyLevelProperty )
        {
            ApplyTransparencyFallback();
        }
    }

    private void ApplyTransparencyFallback()
    {
        if ( _backdrop == null )
        {
            return;
        }

        bool transparent = ActualTransparencyLevel != WindowTransparencyLevel.None;

        _backdrop.Background = transparent ? Brushes.Transparent : _opaqueBackdrop;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}
