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
    ///     Round-trips the ECV filter conditions through FilterProfiles.json. Value alone isn't enough:
    ///     string constraints keep their input in Additional and multi-value ones in Values, and dropping
    ///     either on save leaves a condition that loads back matching nothing.
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

                viewModel.FilterConditions.Add( new AutolootConstraintEntry
                {
                    Property = name, Operator = AutolootOperator.Equal, Additional = "vanquishing"
                } );

                // Values starts null rather than empty, which is why saving guards on it.
                viewModel.FilterConditions.Add( new AutolootConstraintEntry
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
                Assert.AreEqual( 2, reloaded.FilterConditions.Count );

                AutolootConstraintEntry loadedName =
                    reloaded.FilterConditions.FirstOrDefault( c => c.Property?.Name == Strings.Name );

                Assert.IsNotNull( loadedName );
                Assert.AreEqual( "vanquishing", loadedName.Additional );

                AutolootConstraintEntry loadedIds =
                    reloaded.FilterConditions.FirstOrDefault( c => c.Property?.Name == Strings.ID__Multiple_ );

                Assert.IsNotNull( loadedIds );
                CollectionAssert.AreEqual( new[] { 0x0EED, 0x1F14 }, loadedIds.Values.ToArray() );
            }
            finally
            {
                reloaded.Cleanup();
            }
        }

        /// <summary>
        ///     A condition whose property no longer resolves must be dropped, not silently rebound to
        ///     whichever constraint happens to sort first while keeping this one's operator and value.
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
                Assert.AreEqual( 0, viewModel.FilterConditions.Count );
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
