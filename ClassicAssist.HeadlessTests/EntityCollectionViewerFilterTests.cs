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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ClassicAssist.Avalonia.Views;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Shared;
using ClassicAssist.UI.Models;
using ClassicAssist.UI.ViewModels;
// Avalonia.Controls has an ItemCollection of its own; the UO one is what the viewer takes.
using ItemCollection = ClassicAssist.UO.Objects.ItemCollection;
using Xunit;

namespace ClassicAssist.HeadlessTests;

/// <summary>
///     The Entity Collection Viewer's filter editor, driven through the real window.
///     <para>
///         These cover what a view model test cannot see: whether editing the filter through the
///         actual view leaves the profile intact. The Property column's constraint list is bound by
///         walking to the window, and such a binding reports null whenever its cell leaves the visual
///         tree - selecting another group swaps the grid's ItemsSource, and an emptied ComboBox used to
///         push that null back through SelectedItem into every condition's Property. A filter then
///         matched everything, and saving dropped the conditions entirely.
///     </para>
/// </summary>
public class EntityCollectionViewerFilterTests
{
    // The fixture is a real FilterProfiles.json: one branch group with two leaf sub-groups, the
    // second OR'd against the first, five conditions between them - the shape that broke.
    private const string MultiGroupProfiles = "Fixtures/FilterProfiles.MultiGroup.json";

    [Fact]
    public Task VisitingEveryGroupKeepsConditionProperties()
    {
        return Headless.Run( () =>
        {
            using Harness harness = Harness.Open( MultiGroupProfiles );

            EntityCollectionFilterGroup root = harness.ViewModel.SelectedProfile.Groups[0];

            harness.SelectGroup( root.Children[0] );
            harness.SelectGroup( root.Children[1] );
            harness.SelectGroup( root );
            harness.SelectGroup( root.Children[0] );

            AssertFixtureConditions( harness.ViewModel.SelectedProfile );
        } );
    }

    [Fact]
    public Task SwitchingProfilesKeepsConditionProperties()
    {
        return Headless.Run( () =>
        {
            using Harness harness = Harness.Open( MultiGroupProfiles );

            FilterProfile multiGroup = harness.ViewModel.SelectedProfile;

            harness.SelectGroup( multiGroup.Groups[0].Children[1] );

            harness.ViewModel.SelectedProfile = harness.ViewModel.Profiles.First( p => p.Name == "House Signs" );
            Headless.Settle();

            harness.ViewModel.SelectedProfile = multiGroup;
            Headless.Settle();

            AssertFixtureConditions( multiGroup );
        } );
    }

    /// <summary>
    ///     End to end over the symptom that showed up as sub-groups reading "(0 filters)" after a
    ///     restart: the window saves on close, and everything it saves has to come back.
    /// </summary>
    [Fact]
    public Task ClosingAfterVisitingEveryGroupSavesEveryCondition()
    {
        return Headless.Run( () =>
        {
            using Harness harness = Harness.Open( MultiGroupProfiles );

            EntityCollectionFilterGroup root = harness.ViewModel.SelectedProfile.Groups[0];

            harness.SelectGroup( root.Children[0] );
            harness.SelectGroup( root.Children[1] );
            harness.SelectGroup( root );

            harness.CloseWindow();

            EntityCollectionViewerViewModel reloaded = new( new ItemCollection( 0 ) );

            try
            {
                AssertFixtureConditions( reloaded.Profiles.First( p => p.Name == "Faster Casting" ) );
            }
            finally
            {
                reloaded.Cleanup();
            }
        } );
    }

    /// <summary>
    ///     The group tree only earns its space when there is something to navigate, and a branch group
    ///     has no conditions of its own to edit - so selecting one has to swap the condition grid out
    ///     for the placeholder. This is the state change that swaps the grid's ItemsSource.
    /// </summary>
    [Fact]
    public Task BranchGroupShowsPlaceholderInsteadOfConditionGrid()
    {
        return Headless.Run( () =>
        {
            using Harness harness = Harness.Open( MultiGroupProfiles );

            EntityCollectionFilterGroup root = harness.ViewModel.SelectedProfile.Groups[0];

            harness.SelectGroup( root.Children[0] );

            Assert.True( harness.ViewModel.ShowGroupTree );
            Assert.False( harness.ViewModel.SelectedGroupIsBranch );
            Assert.True( harness.ConditionGrid.IsVisible );

            harness.SelectGroup( root );

            Assert.True( harness.ViewModel.SelectedGroupIsBranch );
            Assert.False( harness.ConditionGrid.IsVisible );
        } );
    }

    private static void AssertFixtureConditions( FilterProfile profile )
    {
        EntityCollectionFilterGroup root = Assert.Single( profile.Groups );

        Assert.Equal( 2, root.Children.Count );
        Assert.Empty( root.Items );

        AssertConditions( root.Children[0], BooleanOperation.And,
            ( "Faster Casting", AutolootOperator.GreaterThan, 1 ),
            ( "Faster Cast Recovery", AutolootOperator.GreaterThan, 3 ) );

        AssertConditions( root.Children[1], BooleanOperation.Or,
            ( "Faster Casting", AutolootOperator.GreaterThan, 1 ),
            ( "Faster Cast Recovery", AutolootOperator.GreaterThan, 2 ),
            ( "Defense Chance Increase", AutolootOperator.GreaterThan, 10 ) );
    }

    private static void AssertConditions( EntityCollectionFilterGroup group, BooleanOperation operation,
        params ( string Name, AutolootOperator Operator, int Value )[] expected )
    {
        Assert.Equal( operation, group.Operation );

        List<( string, AutolootOperator, int )> actual = group.Items
            .Select( i => ( i.Property?.Name, i.Operator, i.Value ) ).ToList();

        Assert.Equal( expected.Select( e => ( e.Name, e.Operator, e.Value ) ).ToList(), actual );
    }

    /// <summary>
    ///     A real EntityCollectionViewer over a temporary startup directory - the view model reads and
    ///     writes FilterProfiles.json beside the app, so each test gets its own copy of the fixture to
    ///     edit and save.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        private readonly string _originalStartupPath;
        private readonly string _tempDirectory;
        private bool _closed;

        private Harness( string tempDirectory, string originalStartupPath, EntityCollectionViewer window,
            EntityCollectionViewerViewModel viewModel )
        {
            _tempDirectory = tempDirectory;
            _originalStartupPath = originalStartupPath;

            Window = window;
            ViewModel = viewModel;
        }

        public EntityCollectionViewer Window { get; }
        public EntityCollectionViewerViewModel ViewModel { get; }

        public DataGrid ConditionGrid =>
            Window.GetVisualDescendants().OfType<DataGrid>().Single();

        private TreeView GroupTree =>
            Window.GetVisualDescendants().OfType<TreeView>().Single();

        public static Harness Open( string fixture )
        {
            string originalStartupPath = Engine.StartupPath;
            string tempDirectory = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );

            Directory.CreateDirectory( Path.Combine( tempDirectory, "Data" ) );

            // The constraint names the fixture refers to are resolved against Data/Properties.json,
            // and a condition naming a constraint that isn't registered is dropped on load.
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

            return new Harness( tempDirectory, originalStartupPath, window, viewModel );
        }

        /// <summary>
        ///     Selects a group the way clicking it in the tree does, through the TreeView rather than
        ///     the view model, so the bindings the view relies on are the ones under test.
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
                Assert.Equal( group.Items.Count,
                    ConditionGrid.GetVisualDescendants().OfType<DataGridRow>().Count() );
            }
        }

        /// <summary>Closes the window, which is the only thing that saves filter profiles.</summary>
        public void CloseWindow()
        {
            Window.Close();

            Headless.Settle();

            _closed = true;
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
}
