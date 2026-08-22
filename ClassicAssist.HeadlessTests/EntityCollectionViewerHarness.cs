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
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ClassicAssist.Avalonia.Views;
using ClassicAssist.Shared;
using ClassicAssist.UI.Models;
using ClassicAssist.UI.ViewModels;
using Xunit;
// Avalonia.Controls has an ItemCollection of its own; the UO one is what the viewer takes.
using ItemCollection = ClassicAssist.UO.Objects.ItemCollection;

namespace ClassicAssist.HeadlessTests;

/// <summary>
///     A real EntityCollectionViewer over a temporary startup directory - the view model reads and
///     writes FilterProfiles.json beside the app, so each test gets its own copy of a fixture to edit
///     and save.
/// </summary>
internal sealed class EntityCollectionViewerHarness : IDisposable
{
    private readonly string _originalStartupPath;
    private readonly string _tempDirectory;
    private bool _closed;

    private EntityCollectionViewerHarness( string tempDirectory, string originalStartupPath,
        EntityCollectionViewer window, EntityCollectionViewerViewModel viewModel )
    {
        _tempDirectory = tempDirectory;
        _originalStartupPath = originalStartupPath;

        Window = window;
        ViewModel = viewModel;
    }

    public EntityCollectionViewer Window { get; }
    public EntityCollectionViewerViewModel ViewModel { get; }

    public DataGrid ConditionGrid => Window.GetVisualDescendants().OfType<DataGrid>().Single();

    public TreeView GroupTree => Window.GetVisualDescendants().OfType<TreeView>().Single();

    /// <summary>The condition rows the grid has actually realized.</summary>
    public int RealizedConditionRows =>
        ConditionGrid.GetVisualDescendants().OfType<DataGridRow>().Count();

    public static EntityCollectionViewerHarness Open( string fixture )
    {
        string originalStartupPath = Engine.StartupPath;
        string tempDirectory = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );

        Directory.CreateDirectory( Path.Combine( tempDirectory, "Data" ) );

        // The constraint names the fixtures refer to are resolved against Data/Properties.json, and a
        // condition naming a constraint that isn't registered is dropped on load.
        File.Copy( Path.Combine( AppContext.BaseDirectory, "Data", "Properties.json" ),
            Path.Combine( tempDirectory, "Data", "Properties.json" ) );

        File.Copy( Path.Combine( AppContext.BaseDirectory, fixture ),
            Path.Combine( tempDirectory, "FilterProfiles.json" ) );

        Engine.StartupPath = tempDirectory;

        EntityCollectionViewerViewModel viewModel = new( new ItemCollection( 0 ) );
        EntityCollectionViewer window = new() { DataContext = viewModel };

        window.Show();

        // The filter panel is collapsed until the toolbar toggle is pressed, and a collapsed panel
        // realizes no cells - so without this the tests would pass by testing nothing.
        viewModel.ShowFilter = true;

        Headless.Settle();

        return new EntityCollectionViewerHarness( tempDirectory, originalStartupPath, window, viewModel );
    }

    /// <summary>
    ///     Selects a group the way clicking it in the tree does, through the TreeView rather than the
    ///     view model, so the bindings the view relies on are the ones under test.
    /// </summary>
    public void SelectGroup( EntityCollectionFilterGroup group )
    {
        GroupTree.SelectedItem = group;

        Headless.Settle();

        Assert.Same( group, ViewModel.SelectedGroup );

        // A leaf group's conditions have to be on screen for any of this to mean anything: an
        // unrealized grid has no cells, hence no ComboBox that can drop its selection.
        if ( !group.HasChildren )
        {
            Assert.Equal( group.Items.Count, RealizedConditionRows );
        }
    }

    /// <summary>
    ///     Selects a profile the way picking it from the toolbar's dropdown does.
    /// </summary>
    public void SelectProfile( string name )
    {
        ViewModel.SelectedProfile = ViewModel.Profiles.Single( p => p.Name == name );

        Headless.Settle();
    }

    /// <summary>Closes the window, which is the only thing that saves filter profiles.</summary>
    public void CloseWindow()
    {
        Window.Close();

        Headless.Settle();

        _closed = true;
    }

    /// <summary>
    ///     Loads the profiles back off disk the way the next session would, without a window.
    /// </summary>
    public static FilterProfile Reload( string profileName )
    {
        EntityCollectionViewerViewModel viewModel = new( new ItemCollection( 0 ) );

        try
        {
            return viewModel.Profiles.Single( p => p.Name == profileName );
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    public void Dispose()
    {
        if ( !_closed )
        {
            Window.Close();
            Headless.Settle();
        }

        Engine.StartupPath = _originalStartupPath;

        try
        {
            Directory.Delete( _tempDirectory, true );
        }
        catch ( IOException )
        {
            // best effort
        }
    }
}
