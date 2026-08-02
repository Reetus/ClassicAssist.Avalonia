using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ClassicAssist.Data;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UI.ViewModels.Agents;
using ClassicAssist.UO.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class AutolootMultiValueTests
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
        public void WillRegisterSpecialProperties()
        {
            ObservableCollection<PropertyEntry> constraints = new ObservableCollection<PropertyEntry>();
            AutolootPropertyRegistration.LoadSpecialProperties( constraints );

            string[] expected =
            {
                "Layer", "Skill Bonus", "ID (Multiple)", "Cliloc (Multiple)", "Autoloot Match",
                "Talisman Skill Bonus", "Talisman Exceptional Skill Bonus"
            };

            foreach ( string name in expected )
            {
                Assert.IsTrue( constraints.Any( c => c.Name == name ), $"Missing special property: {name}" );
            }
        }

        [TestMethod]
        public void WillMatchIdMultipleValues()
        {
            ObservableCollection<PropertyEntry> constraints = new ObservableCollection<PropertyEntry>();
            AutolootPropertyRegistration.LoadSpecialProperties( constraints );

            PropertyEntry idMulti = constraints.First( c => c.Name == "ID (Multiple)" );
            Item item = new Item( 0x40000001 ) { ID = 0x0F6A };

            AutolootConstraintEntry entry = new AutolootConstraintEntry
            {
                Property = idMulti,
                Operator = AutolootOperator.Equal,
                Values = new ObservableCollection<int> { 0x0F6A }
            };

            Assert.IsTrue( idMulti.Predicate( item, entry ) );

            entry.Values = new ObservableCollection<int> { 0x1234 };
            Assert.IsFalse( idMulti.Predicate( item, entry ) );

            entry.Operator = AutolootOperator.NotEqual;
            Assert.IsTrue( idMulti.Predicate( item, entry ) );
        }

        [TestMethod]
        public void WillMatchClilocMultipleValues()
        {
            ObservableCollection<PropertyEntry> constraints = new ObservableCollection<PropertyEntry>();
            AutolootPropertyRegistration.LoadSpecialProperties( constraints );

            PropertyEntry clilocMulti = constraints.First( c => c.Name == "Cliloc (Multiple)" );
            Item item = new Item( 0x40000001 ) { Properties = new[] { new Property { Cliloc = 1060401 } } };

            AutolootConstraintEntry entry = new AutolootConstraintEntry
            {
                Property = clilocMulti,
                Operator = AutolootOperator.Equal,
                Values = new ObservableCollection<int> { 1060401 }
            };

            Assert.IsTrue( clilocMulti.Predicate( item, entry ) );

            entry.Values = new ObservableCollection<int> { 1060402 };
            Assert.IsFalse( clilocMulti.Predicate( item, entry ) );
        }

        [TestMethod]
        public void WillSerializeConstraintValuesRoundTrip()
        {
            WritePropertiesFile();

            AutolootViewModel vm = new AutolootViewModel();
            PropertyEntry clilocMulti = vm.Constraints.First( c => c.Name == "Cliloc (Multiple)" );

            AutolootEntry entry = new AutolootEntry { Name = "Test", ID = 0x0F6A };
            entry.Constraints.Add( new AutolootConstraintEntry
            {
                Property = clilocMulti,
                Operator = AutolootOperator.Equal,
                Values = new ObservableCollection<int> { 1060401, 1060402 }
            } );

            vm.Items.Add( entry );

            JObject json = new JObject();
            vm.Serialize( json );

            AutolootViewModel reloaded = new AutolootViewModel();
            reloaded.Deserialize( json, Options.CurrentOptions );

            AutolootConstraintEntry reloadedConstraint = reloaded.Items[0].Constraints[0];

            Assert.IsNotNull( reloadedConstraint.Values );
            Assert.AreEqual( 2, reloadedConstraint.Values.Count );
            Assert.IsTrue( reloadedConstraint.Values.Contains( 1060401 ) );
            Assert.IsTrue( reloadedConstraint.Values.Contains( 1060402 ) );
        }
    }
}
