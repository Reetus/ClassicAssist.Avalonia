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

using System.Threading.Tasks;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.UI.Models;
using Xunit;

namespace ClassicAssist.HeadlessTests;

/// <summary>
///     The Entity Collection Viewer's filter editor in group mode, driven through the real window.
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
    // A real FilterProfiles.json: one branch group with two leaf sub-groups, the second OR'd against
    // the first, five conditions between them - the shape that broke.
    private const string MultiGroupProfiles = "Fixtures/FilterProfiles.MultiGroup.json";

    private const string MultiGroupProfileName = "Faster Casting";

    [Fact]
    public Task VisitingEveryGroupKeepsConditionProperties()
    {
        return Headless.Run( () =>
        {
            using EntityCollectionViewerHarness harness =
                EntityCollectionViewerHarness.Open( MultiGroupProfiles );

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
            using EntityCollectionViewerHarness harness =
                EntityCollectionViewerHarness.Open( MultiGroupProfiles );

            FilterProfile multiGroup = harness.ViewModel.SelectedProfile;

            harness.SelectGroup( multiGroup.Groups[0].Children[1] );

            harness.SelectProfile( "House Signs" );
            harness.SelectProfile( MultiGroupProfileName );

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
            using EntityCollectionViewerHarness harness =
                EntityCollectionViewerHarness.Open( MultiGroupProfiles );

            EntityCollectionFilterGroup root = harness.ViewModel.SelectedProfile.Groups[0];

            harness.SelectGroup( root.Children[0] );
            harness.SelectGroup( root.Children[1] );
            harness.SelectGroup( root );

            harness.CloseWindow();

            AssertFixtureConditions( EntityCollectionViewerHarness.Reload( MultiGroupProfileName ) );
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
            using EntityCollectionViewerHarness harness =
                EntityCollectionViewerHarness.Open( MultiGroupProfiles );

            EntityCollectionFilterGroup root = harness.ViewModel.SelectedProfile.Groups[0];

            harness.SelectGroup( root.Children[0] );

            Assert.True( harness.ViewModel.ShowGroupTree );
            Assert.True( harness.GroupTree.IsEffectivelyVisible );
            Assert.False( harness.ViewModel.SelectedGroupIsBranch );
            Assert.True( harness.ConditionGrid.IsEffectivelyVisible );

            harness.SelectGroup( root );

            Assert.True( harness.ViewModel.SelectedGroupIsBranch );
            Assert.False( harness.ConditionGrid.IsEffectivelyVisible );
        } );
    }

    private static void AssertFixtureConditions( FilterProfile profile )
    {
        EntityCollectionFilterGroup root = Assert.Single( profile.Groups );

        Assert.Equal( 2, root.Children.Count );
        Assert.Empty( root.Items );

        FilterAssert.Conditions( root.Children[0], BooleanOperation.And,
            ( "Faster Casting", AutolootOperator.GreaterThan, 1 ),
            ( "Faster Cast Recovery", AutolootOperator.GreaterThan, 3 ) );

        FilterAssert.Conditions( root.Children[1], BooleanOperation.Or,
            ( "Faster Casting", AutolootOperator.GreaterThan, 1 ),
            ( "Faster Cast Recovery", AutolootOperator.GreaterThan, 2 ),
            ( "Defense Chance Increase", AutolootOperator.GreaterThan, 10 ) );
    }
}
