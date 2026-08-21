using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.Models;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Round-trips the ECV filter groups through FilterProfiles.json. Value alone isn't enough:
    ///     string constraints keep their input in Additional and multi-value ones in Values, and dropping
    ///     either on save leaves a condition that loads back matching nothing. The file is written in
    ///     WPF's nested Groups/Children shape so the two sides can share profiles.
    /// </summary>
    [TestClass]
    public class FilterProfileRoundTripTests
    {
        private string _originalStartupPath;
        private string _tempDir;

        [TestInitialize]
        public void Initialize()
        {
            _originalStartupPath = Engine.StartupPath;
            _tempDir = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );

            Directory.CreateDirectory( _tempDir );

            Engine.StartupPath = _tempDir;
        }

        [TestCleanup]
        public void Cleanup()
        {
            Engine.StartupPath = _originalStartupPath;

            try
            {
                Directory.Delete( _tempDir, true );
            }
            catch ( IOException )
            {
                // best effort
            }
        }

        [TestMethod]
        public void WillRoundTripAdditionalAndValues()
        {
            EntityCollectionViewerViewModel viewModel = new EntityCollectionViewerViewModel( new ItemCollection( 0 ) );

            try
            {
                PropertyEntry name = Find( viewModel, Strings.Name );
                PropertyEntry multipleIds = Find( viewModel, Strings.ID__Multiple_ );

                // The default profile is flat (no groups) - conditions are edited directly, and a flat
                // profile is written as a single And group, so it comes back in tree mode.
                ObservableCollection<AutolootConstraintEntry> conditions = viewModel.SelectedProfile.Conditions;

                conditions.Add( new AutolootConstraintEntry
                {
                    Property = name, Operator = AutolootOperator.Equal, Additional = "vanquishing"
                } );

                // Values starts null rather than empty, which is why saving guards on it.
                conditions.Add( new AutolootConstraintEntry
                {
                    Property = multipleIds,
                    Operator = AutolootOperator.Equal,
                    Values = new ObservableCollection<int> { 0x0EED, 0x1F14 }
                } );

                viewModel.SaveFilterProfiles();
            }
            finally
            {
                viewModel.Cleanup();
            }

            EntityCollectionViewerViewModel reloaded = new EntityCollectionViewerViewModel( new ItemCollection( 0 ) );

            try
            {
                Assert.AreEqual( 1, reloaded.SelectedProfile.Groups.Count );

                ObservableCollection<AutolootConstraintEntry> loadedConditions =
                    reloaded.SelectedProfile.Groups[0].Items;

                Assert.AreEqual( 2, loadedConditions.Count );

                AutolootConstraintEntry loadedName =
                    loadedConditions.FirstOrDefault( c => c.Property?.Name == Strings.Name );

                Assert.IsNotNull( loadedName );
                Assert.AreEqual( "vanquishing", loadedName.Additional );

                AutolootConstraintEntry loadedIds =
                    loadedConditions.FirstOrDefault( c => c.Property?.Name == Strings.ID__Multiple_ );

                Assert.IsNotNull( loadedIds );
                CollectionAssert.AreEqual( new[] { 0x0EED, 0x1F14 }, loadedIds.Values.ToArray() );
            }
            finally
            {
                reloaded.Cleanup();
            }
        }

        /// <summary>
        ///     A WPF-written FilterProfiles.json (nested Groups, items keyed by Constraint.Name) must load
        ///     with its group structure intact - this is the compatibility guarantee that drove the port.
        /// </summary>
        [TestMethod]
        public void WillLoadWpfNestedGroups()
        {
            File.WriteAllText( Path.Combine( _tempDir, "FilterProfiles.json" ), @"{
                ""LastProfileID"": ""11111111-1111-1111-1111-111111111111"",
                ""Profiles"": [ { ""ID"": ""11111111-1111-1111-1111-111111111111"", ""Name"": ""WPF"",
                    ""Groups"": [
                        { ""Operation"": 0, ""Items"": [ ], ""Children"": [
                            { ""Operation"": 0, ""Items"": [ { ""Constraint"": { ""Name"": ""Name"" },
                                ""Operator"": 0, ""Value"": 100, ""Additional"": null, ""Enabled"": true } ] },
                            { ""Operation"": 1, ""Items"": [ { ""Constraint"": { ""Name"": ""ID (Multiple)"" },
                                ""Operator"": 0, ""Value"": 5, ""Additional"": null, ""Enabled"": false } ] }
                        ] }
                    ] } ] }" );

            EntityCollectionViewerViewModel viewModel = new EntityCollectionViewerViewModel( new ItemCollection( 0 ) );

            try
            {
                FilterProfile profile = viewModel.Profiles.Single();
                Assert.AreEqual( "WPF", profile.Name );
                Assert.AreEqual( 1, profile.Groups.Count );

                EntityCollectionFilterGroup branch = profile.Groups[0];
                Assert.IsTrue( branch.HasChildren );
                Assert.AreEqual( 2, branch.Children.Count );
                Assert.AreEqual( BooleanOperation.And, branch.Children[0].Operation );
                Assert.AreEqual( 1, branch.Children[0].Items.Count );
                Assert.AreEqual( "Name", branch.Children[0].Items[0].Property?.Name );
                Assert.AreEqual( 100, branch.Children[0].Items[0].Value );
                Assert.AreEqual( BooleanOperation.Or, branch.Children[1].Operation );
                Assert.IsFalse( branch.Children[1].Items[0].Enabled );
            }
            finally
            {
                viewModel.Cleanup();
            }
        }

        /// <summary>
        ///     A condition whose property no longer resolves must be dropped, not silently rebound to
        ///     whichever constraint happens to sort first while keeping this one's operator and value.
        ///     Written in the legacy flat Conditions shape, which must still load flat (no groups - the
        ///     tree stays hidden).
        /// </summary>
        [TestMethod]
        public void WillDropConditionsWhoseConstraintIsUnknown()
        {
            File.WriteAllText( Path.Combine( _tempDir, "FilterProfiles.json" ), @"{
                ""Profiles"": [ { ""Name"": ""Default"", ""Conditions"": [
                    { ""Property"": ""Constraint From An Unloaded Plugin"", ""Operator"": 0, ""Value"": 42 }
                ] } ] }" );

            EntityCollectionViewerViewModel viewModel = new EntityCollectionViewerViewModel( new ItemCollection( 0 ) );

            try
            {
                Assert.AreEqual( 0, viewModel.SelectedProfile.Groups.Count );
                Assert.AreEqual( 0, viewModel.SelectedProfile.Conditions.Count );
            }
            finally
            {
                viewModel.Cleanup();
            }
        }

        private static PropertyEntry Find( EntityCollectionViewerViewModel viewModel, string name )
        {
            PropertyEntry entry = viewModel.Constraints.FirstOrDefault( c => c.Name == name );

            Assert.IsNotNull( entry, $"constraint '{name}' should be registered" );

            return entry;
        }
    }
}