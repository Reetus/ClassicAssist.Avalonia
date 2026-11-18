using System.IO;
using ClassicAssist.Shared;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Objects;
using System;
using Xunit;

namespace ClassicAssist.Tests.ECV
{
    /// <summary>
    ///     When the filter editor shows its group tree: only when there's something to navigate - more
    ///     than one top-level group, or any sub-group (a single branch group still needs the tree to
    ///     reach its children). Matches WPF's HasSubgroups flat-vs-split decision, plus multi-group.
    /// </summary>
    public class EntityCollectionFilterTreeTests : IDisposable
    {
        private string _originalStartupPath;
        private string _tempDir;

        public EntityCollectionFilterTreeTests()
        {
            _originalStartupPath = Engine.StartupPath;
            _tempDir = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );

            Directory.CreateDirectory( _tempDir );

            Engine.StartupPath = _tempDir;
        }

        public void Dispose()
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

        [Fact]
        public void NoGroupsHidesTree()
        {
            EntityCollectionViewerViewModel viewModel = new( new ItemCollection( 0 ) );

            try
            {
                Assert.False( viewModel.ShowGroupTree );
            }
            finally
            {
                viewModel.Cleanup();
            }
        }

        [Fact]
        public void SingleLeafGroupHidesTree()
        {
            EntityCollectionViewerViewModel viewModel = new( new ItemCollection( 0 ) );

            try
            {
                viewModel.AddGroupCommand.Execute( null );

                Assert.Single( viewModel.SelectedProfile.Groups );
                Assert.False( viewModel.ShowGroupTree );
            }
            finally
            {
                viewModel.Cleanup();
            }
        }

        [Fact]
        public void MultipleGroupsShowTree()
        {
            EntityCollectionViewerViewModel viewModel = new( new ItemCollection( 0 ) );

            try
            {
                viewModel.AddGroupCommand.Execute( null );
                viewModel.AddGroupCommand.Execute( null );

                Assert.Equal( 2, viewModel.SelectedProfile.Groups.Count );
                Assert.True( viewModel.ShowGroupTree );
            }
            finally
            {
                viewModel.Cleanup();
            }
        }

        [Fact]
        public void SingleBranchGroupShowsTree()
        {
            EntityCollectionViewerViewModel viewModel = new( new ItemCollection( 0 ) );

            try
            {
                viewModel.AddGroupCommand.Execute( null );
                viewModel.AddSubGroupCommand.Execute( null );

                Assert.Single( viewModel.SelectedProfile.Groups );
                Assert.True( viewModel.SelectedProfile.Groups[0].HasChildren );
                Assert.True( viewModel.ShowGroupTree );
            }
            finally
            {
                viewModel.Cleanup();
            }
        }

        [Fact]
        public void RemovingLastSubgroupHidesTree()
        {
            EntityCollectionViewerViewModel viewModel = new( new ItemCollection( 0 ) );

            try
            {
                viewModel.AddGroupCommand.Execute( null );
                viewModel.AddSubGroupCommand.Execute( null );

                Assert.True( viewModel.ShowGroupTree );

                viewModel.RemoveGroupCommand.Execute( null );

                Assert.Single( viewModel.SelectedProfile.Groups );
                Assert.False( viewModel.SelectedProfile.Groups[0].HasChildren );
                Assert.False( viewModel.ShowGroupTree );
            }
            finally
            {
                viewModel.Cleanup();
            }
        }
    }
}