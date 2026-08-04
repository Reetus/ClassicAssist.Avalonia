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
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Avalonia.Views
{
    /// <summary>
    ///     Debug tabs persist their settings to <see cref="AssistantOptions.DebugWindowOptions" />, which
    ///     is written to Assistant.json - the same contract as the WPF window, so a shared Assistant.json
    ///     carries the same debug state either way.
    /// </summary>
    public partial class DebugWindow : Window
    {
        public DebugWindow()
        {
            InitializeComponent();

            foreach ( ISettingProvider provider in GetSettingProviders() )
            {
                provider.Deserialize( AssistantOptions.DebugWindowOptions, Options.CurrentOptions );
            }

            Closing += OnClosing;
        }

        private void OnClosing( object sender, CancelEventArgs e )
        {
            Closing -= OnClosing;

            // Anything thrown from a Closing handler is unhandled and takes the process with it, so a
            // debug tab that can't save its settings must not be fatal.
            try
            {
                JObject options = new JObject();

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
                new HashSet<ISettingProvider>( ReferenceEqualityComparer.Instance );

            if ( DataContext is ISettingProvider windowProvider && seen.Add( windowProvider ) )
            {
                yield return windowProvider;
            }

            if ( !( Content is TabControl tabControl ) )
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
}
