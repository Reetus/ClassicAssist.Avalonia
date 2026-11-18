using System;
using System.IO;
using System.Linq;
using ClassicAssist.Data;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UI.ViewModels.Agents;
using Xunit;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Tests
{
    public class AutolootGroupsTests : IDisposable
    {
        private string _originalStartupPath;
        private string _tempDir;

        public AutolootGroupsTests()
        {
            _originalStartupPath = Engine.StartupPath;
            _tempDir = Path.Combine( Path.GetTempPath(), "ClassicAssistTests", Guid.NewGuid().ToString( "N" ) );
            Directory.CreateDirectory( Path.Combine( _tempDir, "Data" ) );
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

        private void WritePropertiesFile()
        {
            File.WriteAllText( Path.Combine( _tempDir, "Data", "Properties.json" ),
                "[{\"Name\":\"Faster Casting\",\"ShortName\":\"FC\",\"Clilocs\":[1060413],\"ClilocIndex\":0,\"ConstraintType\":0}]" );
        }

        [Fact]
        public void WillParseSimpleCsv()
        {
            CsvReader reader = new CsvReader( new StringReader( "ID,Name,FC\r\n0x0F6A,Sword,1\r\n0x0F6B,Axe,2\r\n" ) );
            reader.ReadHeader();

            Assert.Equal( "ID", reader.HeaderRecord[0] );
            Assert.True( reader.Read() );
            Assert.True( reader.TryGetField( "ID", out string id ) );
            Assert.Equal( "0x0F6A", id );
            Assert.True( reader.TryGetField( "Name", out string name ) );
            Assert.Equal( "Sword", name );
            Assert.True( reader.Read() );
            Assert.True( reader.TryGetField( "FC", out string fc ) );
            Assert.Equal( "2", fc );
            Assert.False( reader.Read() );
        }

        [Fact]
        public void WillParseQuotedCsvFields()
        {
            CsvReader reader = new CsvReader( new StringReader( "ID,Name\r\n0x1,\"A, B\"\"C\"\r\n" ) );
            reader.ReadHeader();

            Assert.True( reader.Read() );
            Assert.True( reader.TryGetField( "Name", out string name ) );
            Assert.Equal( "A, B\"C", name );
        }

        [Fact]
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

            Assert.Equal( 2, reloaded.Items.Count );
            Assert.True( reloaded.RequeueFailedItems );
            Assert.False( reloaded.LootHumanoids );

            AutolootEntry reloadedHigh = reloaded.Items.FirstOrDefault( i => i.Name == "High Item" );
            AutolootEntry reloadedTop = reloaded.Items.FirstOrDefault( i => i.Name == "Top Item" );

            Assert.NotNull( reloadedHigh );
            Assert.Equal( AutolootPriority.High, reloadedHigh.Priority );
            Assert.Equal( AutolootPriority.Top, reloadedTop.Priority );

            AutolootGroup reloadedGroup = reloaded.Draggables.OfType<AutolootGroup>().FirstOrDefault();
            Assert.NotNull( reloadedGroup );
            Assert.Equal( "Group-1", reloadedGroup.Name );
            Assert.Single( reloadedGroup.Children );
            Assert.Equal( "High Item", ( (AutolootEntry) reloadedGroup.Children[0] ).Name );
            Assert.Equal( reloadedGroup, reloadedHigh.Group );
        }

        [Fact]
        public void WillKeepUngroupedEntryAtRootOnSerialize()
        {
            WritePropertiesFile();

            AutolootViewModel vm = new AutolootViewModel();

            vm.Items.Add( new AutolootEntry { Name = "Root Item", ID = 0x1000 } );

            JObject json = new JObject();
            vm.Serialize( json );

            AutolootViewModel reloaded = new AutolootViewModel();
            reloaded.Deserialize( json, Options.CurrentOptions );

            Assert.Single( reloaded.Items );
            Assert.Null( reloaded.Items[0].Group );
            Assert.Single( reloaded.Draggables );
        }

        [Fact]
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

            Assert.Single( groupA.Children );
            Assert.Equal( groupA, entry.Group );

            vm.SelectedItem = entry;
            vm.MoveToGroupCommand.Execute( groupB );

            Assert.Empty( groupA.Children );
            Assert.Single( groupB.Children );
            Assert.Equal( groupB, entry.Group );
        }

        [Fact]
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

            Assert.DoesNotContain( group, vm.Draggables );
            Assert.Contains( entry, vm.Draggables );
            Assert.Null( entry.Group );
        }

        [Fact]
        public void WillMoveNonSelectedEntryToGroup()
        {
            WritePropertiesFile();

            AutolootViewModel vm = new AutolootViewModel();
            AutolootEntry entry = new AutolootEntry { Name = "Drag Me", ID = 0x1000 };

            vm.Items.Add( entry );

            AutolootGroup group = new AutolootGroup { Name = "Target" };
            vm.Draggables.Add( group );

            vm.MoveToGroup( entry, group );

            Assert.Single( group.Children );
            Assert.Equal( group, entry.Group );
            Assert.DoesNotContain( entry, vm.Draggables );
        }

        [Fact]
        public void WillAppendItemIdToDisplayName()
        {
            AutolootEntry entry = new AutolootEntry { Name = "Iron Sword", ID = 0x1BF2 };

            Assert.Equal( "Iron Sword - 0x1bf2", entry.DisplayName );
        }

        [Fact]
        public void WillShowMatchAnyIdAs0xFFFF()
        {
            AutolootEntry entry = new AutolootEntry { Name = "Any", ID = -1 };

            Assert.Equal( "Any - 0xffff", entry.DisplayName );
        }

        [Fact]
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

            Assert.Single( group.Children );
            Assert.Equal( group, entry.Group );

            vm.MoveToRoot( entry );

            Assert.Empty( group.Children );
            Assert.Null( entry.Group );
            Assert.Contains( entry, vm.Draggables );
        }
    }
}
