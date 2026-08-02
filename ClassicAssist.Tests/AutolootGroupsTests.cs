using System;
using System.IO;
using System.Linq;
using ClassicAssist.Data;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UI.ViewModels.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class AutolootGroupsTests
    {
        private string _originalStartupPath;
        private string _tempDir;

        [TestInitialize]
        public void SetUp()
        {
            _originalStartupPath = Engine.StartupPath;
            _tempDir = Path.Combine( Path.GetTempPath(), "ClassicAssistTests", Guid.NewGuid().ToString( "N" ) );
            Directory.CreateDirectory( Path.Combine( _tempDir, "Data" ) );
            Engine.StartupPath = _tempDir;
        }

        [TestCleanup]
        public void TearDown()
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

        private void WritePropertiesFile()
        {
            File.WriteAllText( Path.Combine( _tempDir, "Data", "Properties.json" ),
                "[{\"Name\":\"Faster Casting\",\"ShortName\":\"FC\",\"Clilocs\":[1060413],\"ClilocIndex\":0,\"ConstraintType\":0}]" );
        }

        [TestMethod]
        public void WillParseSimpleCsv()
        {
            CsvReader reader = new CsvReader( new StringReader( "ID,Name,FC\r\n0x0F6A,Sword,1\r\n0x0F6B,Axe,2\r\n" ) );
            reader.ReadHeader();

            Assert.AreEqual( "ID", reader.HeaderRecord[0] );
            Assert.IsTrue( reader.Read() );
            Assert.IsTrue( reader.TryGetField( "ID", out string id ) );
            Assert.AreEqual( "0x0F6A", id );
            Assert.IsTrue( reader.TryGetField( "Name", out string name ) );
            Assert.AreEqual( "Sword", name );
            Assert.IsTrue( reader.Read() );
            Assert.IsTrue( reader.TryGetField( "FC", out string fc ) );
            Assert.AreEqual( "2", fc );
            Assert.IsFalse( reader.Read() );
        }

        [TestMethod]
        public void WillParseQuotedCsvFields()
        {
            CsvReader reader = new CsvReader( new StringReader( "ID,Name\r\n0x1,\"A, B\"\"C\"\r\n" ) );
            reader.ReadHeader();

            Assert.IsTrue( reader.Read() );
            Assert.IsTrue( reader.TryGetField( "Name", out string name ) );
            Assert.AreEqual( "A, B\"C", name );
        }

        [TestMethod]
        public void WillSerializeAndDeserializeGroupsAndPriority()
        {
            WritePropertiesFile();

            AutolootViewModel vm = new AutolootViewModel();

            AutolootEntry high = new AutolootEntry
            {
                Name = "High Item",
                ID = 0x1000,
                Priority = AutolootPriority.High,
                Constraints = new System.Collections.ObjectModel.ObservableCollection<AutolootConstraintEntry>()
            };

            AutolootEntry top = new AutolootEntry
            {
                Name = "Top Item",
                ID = 0x2000,
                Priority = AutolootPriority.Top,
                Constraints = new System.Collections.ObjectModel.ObservableCollection<AutolootConstraintEntry>()
            };

            AutolootGroup group = new AutolootGroup { Name = "Group-1" };

            vm.Items.Add( high );
            vm.Items.Add( top );
            vm.SelectedItem = high;
            vm.MoveToGroupCommand.Execute( group );
            vm.RequeueFailedItems = true;
            vm.LootHumanoids = false;

            JObject json = new JObject();
            vm.Serialize( json );

            AutolootViewModel reloaded = new AutolootViewModel();
            reloaded.Deserialize( json, Options.CurrentOptions );

            Assert.AreEqual( 2, reloaded.Items.Count );
            Assert.IsTrue( reloaded.RequeueFailedItems );
            Assert.IsFalse( reloaded.LootHumanoids );

            AutolootEntry reloadedHigh = reloaded.Items.FirstOrDefault( i => i.Name == "High Item" );
            AutolootEntry reloadedTop = reloaded.Items.FirstOrDefault( i => i.Name == "Top Item" );

            Assert.IsNotNull( reloadedHigh );
            Assert.AreEqual( AutolootPriority.High, reloadedHigh.Priority );
            Assert.AreEqual( AutolootPriority.Top, reloadedTop.Priority );

            AutolootGroup reloadedGroup = reloaded.Draggables.OfType<AutolootGroup>().FirstOrDefault();
            Assert.IsNotNull( reloadedGroup );
            Assert.AreEqual( "Group-1", reloadedGroup.Name );
            Assert.AreEqual( 1, reloadedGroup.Children.Count );
            Assert.AreEqual( "High Item", ( (AutolootEntry) reloadedGroup.Children[0] ).Name );
            Assert.AreEqual( reloadedGroup, reloadedHigh.Group );
        }

        [TestMethod]
        public void WillKeepUngroupedEntryAtRootOnSerialize()
        {
            WritePropertiesFile();

            AutolootViewModel vm = new AutolootViewModel();

            vm.Items.Add( new AutolootEntry { Name = "Root Item", ID = 0x1000 } );

            JObject json = new JObject();
            vm.Serialize( json );

            AutolootViewModel reloaded = new AutolootViewModel();
            reloaded.Deserialize( json, Options.CurrentOptions );

            Assert.AreEqual( 1, reloaded.Items.Count );
            Assert.IsNull( reloaded.Items[0].Group );
            Assert.AreEqual( 1, reloaded.Draggables.Count );
        }

        [TestMethod]
        public void WillMoveEntryBetweenGroups()
        {
            WritePropertiesFile();

            AutolootViewModel vm = new AutolootViewModel();
            AutolootEntry entry = new AutolootEntry { Name = "Move Me", ID = 0x1000 };

            vm.Items.Add( entry );

            AutolootGroup groupA = new AutolootGroup { Name = "A" };
            AutolootGroup groupB = new AutolootGroup { Name = "B" };

            vm.Draggables.Add( groupA );
            vm.Draggables.Add( groupB );

            vm.SelectedItem = entry;
            vm.MoveToGroupCommand.Execute( groupA );

            Assert.AreEqual( 1, groupA.Children.Count );
            Assert.AreEqual( groupA, entry.Group );

            vm.SelectedItem = entry;
            vm.MoveToGroupCommand.Execute( groupB );

            Assert.AreEqual( 0, groupA.Children.Count );
            Assert.AreEqual( 1, groupB.Children.Count );
            Assert.AreEqual( groupB, entry.Group );
        }

        [TestMethod]
        public void WillRemoveGroupAndRehomeChildren()
        {
            WritePropertiesFile();

            AutolootViewModel vm = new AutolootViewModel();
            AutolootEntry entry = new AutolootEntry { Name = "Child", ID = 0x1000 };

            vm.Items.Add( entry );

            AutolootGroup group = new AutolootGroup { Name = "G" };
            vm.Draggables.Add( group );

            vm.SelectedItem = entry;
            vm.MoveToGroupCommand.Execute( group );

            vm.RemoveGroupCommand.Execute( group );

            Assert.IsFalse( vm.Draggables.Contains( group ) );
            Assert.IsTrue( vm.Draggables.Contains( entry ) );
            Assert.IsNull( entry.Group );
        }

        [TestMethod]
        public void WillMoveNonSelectedEntryToGroup()
        {
            WritePropertiesFile();

            AutolootViewModel vm = new AutolootViewModel();
            AutolootEntry entry = new AutolootEntry { Name = "Drag Me", ID = 0x1000 };

            vm.Items.Add( entry );

            AutolootGroup group = new AutolootGroup { Name = "Target" };
            vm.Draggables.Add( group );

            vm.MoveToGroup( entry, group );

            Assert.AreEqual( 1, group.Children.Count );
            Assert.AreEqual( group, entry.Group );
            Assert.IsFalse( vm.Draggables.Contains( entry ) );
        }

        [TestMethod]
        public void WillAppendItemIdToDisplayName()
        {
            AutolootEntry entry = new AutolootEntry { Name = "Iron Sword", ID = 0x1BF2 };

            Assert.AreEqual( "Iron Sword - 0x1bf2", entry.DisplayName );
        }

        [TestMethod]
        public void WillShowMatchAnyIdAs0xFFFF()
        {
            AutolootEntry entry = new AutolootEntry { Name = "Any", ID = -1 };

            Assert.AreEqual( "Any - 0xffff", entry.DisplayName );
        }

        [TestMethod]
        public void WillMoveEntryOutOfGroupToRoot()
        {
            WritePropertiesFile();

            AutolootViewModel vm = new AutolootViewModel();
            AutolootEntry entry = new AutolootEntry { Name = "Ungroup Me", ID = 0x1000 };

            vm.Items.Add( entry );

            AutolootGroup group = new AutolootGroup { Name = "G" };
            vm.Draggables.Add( group );

            vm.SelectedItem = entry;
            vm.MoveToGroupCommand.Execute( group );

            Assert.AreEqual( 1, group.Children.Count );
            Assert.AreEqual( group, entry.Group );

            vm.MoveToRoot( entry );

            Assert.AreEqual( 0, group.Children.Count );
            Assert.IsNull( entry.Group );
            Assert.IsTrue( vm.Draggables.Contains( entry ) );
        }
    }
}
