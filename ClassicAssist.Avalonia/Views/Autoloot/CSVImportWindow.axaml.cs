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

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ClassicAssist.Misc;

namespace ClassicAssist.Avalonia.Views.Autoloot;

/// <summary>
///     Interaction logic for CSVImportWindow.xaml
/// </summary>
public partial class CSVImportWindow : Window
{
    private const string WIKI_URL = "https://github.com/Reetus/ClassicAssist/wiki/Importing-Autoloot-Items";

    public CSVImportWindow()
    {
        InitializeComponent();
    }

    private void OnWikiLinkClick( object sender, PointerPressedEventArgs e )
    {
        ShellLauncher.OpenUrl( WIKI_URL );
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}
