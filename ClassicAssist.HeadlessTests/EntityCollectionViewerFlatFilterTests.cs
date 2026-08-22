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
///     The filter editor with no groups at all - "Faster Casting >= 1 and Faster Cast Recovery >= 3"
///     as two conditions on the profile itself, which is what a profile written before the boolean-tree
///     port looks like and what a new profile stays as until a group is added.
///     <para>
///         The group tree is hidden here, so there is nothing to select and the condition grid's
///         ItemsSource is the profile's own conditions - but the same ancestor-bound constraint list
///         feeds the Property column, and it still goes null when the grid is torn down by a profile
///         switch or by closing the window. Those are the two paths that used to lose the conditions
///         in flat mode.
///     </para>
/// </summary>
public class EntityCollectionViewerFlatFilterTests
{
    private const string FlatProfiles = "Fixtures/FilterProfiles.Flat.json";

    private const string FlatProfileName = "Faster Casting";

    private static readonly ( string, AutolootOperator, int )[] _expected =
    [
        ( "Faster Casting", AutolootOperator.GreaterThan, 1 ),
        ( "Faster Cast Recovery", AutolootOperator.GreaterThan, 3 )
    ];

    /// <summary>
    ///     A flat profile edits its conditions directly and hides the group tree - a tree with a single
    ///     node and nothing to navigate is just wasted space.
    /// </summary>
    [Fact]
    public Task FlatProfileHidesGroupTreeAndEditsConditionsDirectly()
    {
        return Headless.Run( () =>
        {
            using EntityCollectionViewerHarness harness = EntityCollectionViewerHarness.Open( FlatProfiles );

            FilterProfile profile = harness.ViewModel.SelectedProfile;

            Assert.Equal( FlatProfileName, profile.Name );
            Assert.Empty( profile.Groups );
            Assert.Null( harness.ViewModel.SelectedGroup );

            Assert.False( harness.ViewModel.ShowGroupTree );
            Assert.False( harness.GroupTree.IsEffectivelyVisible );

            // The grid edits the profile's own conditions, and every one of them is on screen.
            Assert.True( harness.ConditionGrid.IsEffectivelyVisible );
            Assert.Same( profile.Conditions, harness.ViewModel.FilterConditions );
            Assert.Equal( profile.Conditions.Count, harness.RealizedConditionRows );

            FilterAssert.Conditions( profile.Conditions, _expected );
        } );
    }

    [Fact]
    public Task SwitchingProfilesKeepsFlatConditionProperties()
    {
        return Headless.Run( () =>
        {
            using EntityCollectionViewerHarness harness = EntityCollectionViewerHarness.Open( FlatProfiles );

            FilterProfile profile = harness.ViewModel.SelectedProfile;

            harness.SelectProfile( "House Signs" );
            harness.SelectProfile( FlatProfileName );

            FilterAssert.Conditions( profile.Conditions, _expected );
        } );
    }

    /// <summary>
    ///     Closing saves, and a flat profile is written as a single And group so WPF reads it - so it
    ///     comes back as that one group rather than as flat conditions. What the user sees has to be
    ///     unchanged either way: one group and nothing to navigate still means no tree.
    /// </summary>
    [Fact]
    public Task ClosingFlatProfileSavesConditionsAsOneGroup()
    {
        return Headless.Run( () =>
        {
            using EntityCollectionViewerHarness harness = EntityCollectionViewerHarness.Open( FlatProfiles );

            harness.CloseWindow();

            FilterProfile reloaded = EntityCollectionViewerHarness.Reload( FlatProfileName );

            Assert.Empty( reloaded.Conditions );
            FilterAssert.Conditions( Assert.Single( reloaded.Groups ), BooleanOperation.And, _expected );

            // And the editor still shows it the way it showed the flat profile.
            using EntityCollectionViewerHarness reopened = EntityCollectionViewerHarness.Open( FlatProfiles );

            Assert.False( reopened.ViewModel.ShowGroupTree );
            Assert.False( reopened.GroupTree.IsEffectivelyVisible );
        } );
    }
}
