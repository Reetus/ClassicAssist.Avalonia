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
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ClassicAssist.Data;
using ClassicAssist.Misc;
using ClassicAssist.Shared.UI.ViewModels.Debug;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Avalonia.Views;

/// <summary>
///     Debug tabs persist their settings to <see cref="AssistantOptions.DebugWindowOptions" />, which
///     is written to Assistant.json - the same contract as the WPF window, so a shared Assistant.json
///     carries the same debug state either way.
/// </summary>
public partial class DebugWindow : Window
{
    /// <summary>
    ///     Set once <see cref="OnClosed" /> has torn the tab view models down;
    ///     <see cref="NavigateTo" /> must not re-populate a disposed view model.
    /// </summary>
    private bool _closed;

    public DebugWindow()
    {
        InitializeComponent();

        foreach ( ISettingProvider provider in GetSettingProviders() )
        {
            provider.Deserialize( AssistantOptions.DebugWindowOptions, Options.CurrentOptions );
        }

        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>
    ///     Opens the window already on the tab for <paramref name="viewModelType" />, populated with
    ///     <paramref name="value" /> - the WPF build's matching constructor. Delegates to
    ///     <see cref="NavigateTo" />.
    /// </summary>
    public DebugWindow( Type viewModelType, object value ) : this()
    {
        NavigateTo( viewModelType, value );
    }

    /// <summary>
    ///     Selects the tab whose content's DataContext is <paramref name="viewModelType" /> and, if it
    ///     derives from <see cref="DebugBaseViewModel" />, hands it <paramref name="value" /> - e.g.
    ///     Object Inspector double-clicking a Properties row opens straight to the Property tab
    ///     pre-populated with that entity's properties.
    ///     <para>
    ///         Also called from <see cref="AvaloniaUIInvoker" />'s single-instance reuse, which
    ///         re-targets this window with the second request's constructor parameters instead of
    ///         constructing a second window and dropping them - "Inspect object" while the Debug
    ///         window is already open must still navigate, not merely focus. Inert once
    ///         <see cref="OnClosed" /> has run: tab view models are disposed there and must not be
    ///         re-populated. A live window's view models are always safe to target - disposal only
    ///         ever happens on close, and the reuse registry drops the window when it closes.
    ///     </para>
    /// </summary>
    public void NavigateTo( Type viewModelType, object value )
    {
        if ( _closed )
        {
            return;
        }

        if ( Content is not TabControl tabControl )
        {
            return;
        }

        foreach ( object item in tabControl.Items )
        {
            if ( item is not TabItem tabItem || tabItem.Content is not Control control ||
                 control.DataContext?.GetType() != viewModelType )
            {
                continue;
            }

            tabControl.SelectedItem = tabItem;

            if ( control.DataContext is DebugBaseViewModel viewModel )
            {
                viewModel.Object = value;
            }

            break;
        }
    }

    private void OnClosing( object sender, CancelEventArgs e )
    {
        Closing -= OnClosing;

        // Anything thrown from a Closing handler is unhandled and takes the process with it, so a
        // debug tab that can't save its settings must not be fatal.
        try
        {
            JObject options = [];

            foreach ( ISettingProvider provider in GetSettingProviders() )
            {
                provider.Serialize( options );
            }

            AssistantOptions.DebugWindowOptions = options;

            // WPF leaves the write to its own shutdown path; do it here too, so the settings
            // survive the UI process being killed alongside the game rather than closed cleanly.
            AssistantOptions.Save();
        }
        catch ( Exception ex )
        {
            Console.WriteLine( $"Failed to save debug window settings: {ex}" );
        }
    }

    /// <summary>
    ///     Tears down every view model this window constructed - the window's own DebugViewModel and
    ///     the tab view models - now that the window is really gone. Their subscriptions into Engine
    ///     events, packet handlers and managers are process-wide statics or singletons, so without
    ///     this each closed window's graph keeps handling live packet traffic forever (and
    ///     DebugViewModel's queue thread keeps running).
    ///     <para>
    ///         Timing and double-dispose: settings were already serialised in <see cref="OnClosing" />,
    ///         so the intact state a disposed view model leaves behind still round-trips; <c>Closed</c>
    ///         fires exactly once per window; and the walk below de-duplicates by reference (the Main
    ///         tab's Grid inherits the window's DataContext, exactly as in
    ///         <see cref="GetSettingProviders" />), so no instance is disposed twice even though every
    ///         <c>Dispose</c> is idempotent anyway.
    ///     </para>
    /// </summary>
    private void OnClosed( object sender, EventArgs e )
    {
        Closed -= OnClosed;
        _closed = true;

        HashSet<IDisposable> seen = new( ReferenceEqualityComparer.Instance );

        if ( DataContext is IDisposable windowViewModel )
        {
            seen.Add( windowViewModel );
            windowViewModel.Dispose();
        }

        if ( Content is not TabControl tabControl )
        {
            return;
        }

        foreach ( object item in tabControl.Items )
        {
            if ( item is TabItem tabItem && tabItem.Content is Control control &&
                 control.DataContext is IDisposable tabViewModel && seen.Add( tabViewModel ) )
            {
                tabViewModel.Dispose();
            }
        }
    }

    /// <summary>
    ///     Every tab whose content carries an <see cref="ISettingProvider" /> DataContext, plus the
    ///     window's own view model, which owns the Main tab.
    ///     <para>
    ///         De-duplicated by reference: the Main tab is declared inline in the window, so its Grid
    ///         has no DataContext of its own and inherits the window's <c>DebugViewModel</c> - which
    ///         means it would otherwise be yielded twice and <c>Serialize</c> would be called twice
    ///         against the same JObject, throwing on the duplicate key and killing the app as the
    ///         window closed. WPF doesn't hit this because each of its tabs hosts its own view model.
    ///     </para>
    /// </summary>
    private IEnumerable<ISettingProvider> GetSettingProviders()
    {
        HashSet<ISettingProvider> seen =
            new( ReferenceEqualityComparer.Instance );

        if ( DataContext is ISettingProvider windowProvider && seen.Add( windowProvider ) )
        {
            yield return windowProvider;
        }

        if ( Content is not TabControl tabControl )
        {
            yield break;
        }

        foreach ( object item in tabControl.Items )
        {
            if ( item is TabItem tabItem && tabItem.Content is Control control &&
                 control.DataContext is ISettingProvider provider && seen.Add( provider ) )
            {
                yield return provider;
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}
