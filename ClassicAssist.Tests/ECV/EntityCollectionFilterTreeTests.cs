using System.IO;
using ClassicAssist.Shared;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests.ECV
{
    /// <summary>
    ///     When the filter editor shows its group tree: only when there's something to navigate - more
    ///     than one top-level group, or any sub-group (a single branch group still needs the tree to
    ///     reach its children). Matches WPF's HasSubgroups flat-vs-split decision, plus multi-group.
    /// </summary>
    [TestClass]
    public class EntityCollectionFilterTreeTests
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
        public void NoGroupsHidesTree()
        {
            EntityCollectionViewerViewModel viewModel = new( new ItemCollection( 0 ) );

            try
            {
                Assert.IsFalse( viewModel.ShowGroupTree );
            }
            finally
            {
                viewModel.Cleanup();
            }
        }

        [TestMethod]
        public void SingleLeafGroupHidesTree()
        {
            EntityCollectionViewerViewModel viewModel = new( new ItemCollection( 0 ) );

            try
            {
                viewModel.AddGroupCommand.Execute( null );

                Assert.AreEqual( 1, viewModel.SelectedProfile.Groups.Count );
                Assert.IsFalse( viewModel.ShowGroupTree );
            }
            finally
            {
                viewModel.Cleanup();
            }
        }

        [TestMethod]
        public void MultipleGroupsShowTree()
        {
            EntityCollectionViewerViewModel viewModel = new( new ItemCollection( 0 ) );

            try
            {
                viewModel.AddGroupCommand.Execute( null );
                viewModel.AddGroupCommand.Execute( null );

                Assert.AreEqual( 2, viewModel.SelectedProfile.Groups.Count );
                Assert.IsTrue( viewModel.ShowGroupTree );
            }
            finally
            {
                viewModel.Cleanup();
            }
        }

        [TestMethod]
        public void SingleBranchGroupShowsTree()
        {
            EntityCollectionViewerViewModel viewModel = new( new ItemCollection( 0 ) );

            try
            {
                viewModel.AddGroupCommand.Execute( null );
                viewModel.AddSubGroupCommand.Execute( null );

                Assert.AreEqual( 1, viewModel.SelectedProfile.Groups.Count );
                Assert.IsTrue( viewModel.SelectedProfile.Groups[0].HasChildren );
                Assert.IsTrue( viewModel.ShowGroupTree );
            }
            finally
            {
                viewModel.Cleanup();
            }
        }

        [TestMethod]
        public void RemovingLastSubgroupHidesTree()
        {
            EntityCollectionViewerViewModel viewModel = new( new ItemCollection( 0 ) );

            try
            {
                viewModel.AddGroupCommand.Execute( null );
                viewModel.AddSubGroupCommand.Execute( null );

                Assert.IsTrue( viewModel.ShowGroupTree );

                viewModel.RemoveGroupCommand.Execute( null );

                Assert.AreEqual( 1, viewModel.SelectedProfile.Groups.Count );
                Assert.IsFalse( viewModel.SelectedProfile.Groups[0].HasChildren );
                Assert.IsFalse( viewModel.ShowGroupTree );
            }
            finally
            {
                viewModel.Cleanup();
            }
        }
    }
}