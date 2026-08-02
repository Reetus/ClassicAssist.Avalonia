using System;
using System.IO;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UI.ViewModels.Autoloot;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class CustomPropertiesViewModelTests
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

        private string CustomPropertiesFile => Path.Combine( _tempDir, "Data", "Properties.Custom.json" );

        [TestMethod]
        public void WillLoadCustomPropertiesFromFile()
        {
            PropertyEntry[] entries =
            {
                new PropertyEntry { Name = "Faster Casting", Clilocs = new[] { 1060401 }, ClilocIndex = -1 },
                new PropertyEntry { Name = "Lower Reagent Cost", Clilocs = new[] { 1060408 }, ClilocIndex = 0 }
            };

            File.WriteAllText( CustomPropertiesFile, JsonConvert.SerializeObject( entries ) );

            CustomPropertiesViewModel vm = new CustomPropertiesViewModel();

            Assert.AreEqual( 2, vm.Properties.Count );
            Assert.AreEqual( "Faster Casting", vm.Properties[0].Name );
            Assert.IsFalse( vm.Properties[0].Arguments );
            Assert.AreEqual( -1, vm.Properties[0].ArgumentIndex );
            Assert.AreEqual( "Lower Reagent Cost", vm.Properties[1].Name );
            Assert.IsTrue( vm.Properties[1].Arguments );
            Assert.AreEqual( 0, vm.Properties[1].ArgumentIndex );
        }

        [TestMethod]
        public void WillSaveCustomPropertiesRoundTrip()
        {
            File.WriteAllText( CustomPropertiesFile, "[]" );

            CustomPropertiesViewModel vm = new CustomPropertiesViewModel();

            vm.Properties.Add( new CustomProperty { Name = "Hit Lightning", Cliloc = 1060450 } );

            vm.SaveCommand.Execute( null );

            PropertyEntry[] parsed = JsonConvert.DeserializeObject<PropertyEntry[]>( File.ReadAllText( CustomPropertiesFile ) );

            Assert.IsNotNull( parsed );
            Assert.AreEqual( 1, parsed.Length );
            Assert.AreEqual( "Hit Lightning", parsed[0].Name );
            Assert.AreEqual( 1060450, parsed[0].Clilocs[0] );
            Assert.AreEqual( -1, parsed[0].ClilocIndex );

            CustomPropertiesViewModel reloaded = new CustomPropertiesViewModel();

            Assert.AreEqual( 1, reloaded.Properties.Count );
            Assert.AreEqual( "Hit Lightning", reloaded.Properties[0].Name );
            Assert.AreEqual( -1, reloaded.Properties[0].ArgumentIndex );
        }

        [TestMethod]
        public void WillPersistArgumentIndexWhenEdited()
        {
            File.WriteAllText( CustomPropertiesFile, "[]" );

            CustomPropertiesViewModel vm = new CustomPropertiesViewModel();

            CustomProperty property = new CustomProperty { Name = "Lower Mana Cost", Cliloc = 1060409, Arguments = true };
            Assert.AreEqual( 0, property.ArgumentIndex );

            property.ArgumentIndex = 1;
            vm.Properties.Add( property );

            vm.SaveCommand.Execute( null );

            PropertyEntry[] parsed = JsonConvert.DeserializeObject<PropertyEntry[]>( File.ReadAllText( CustomPropertiesFile ) );

            Assert.AreEqual( 1, parsed[0].ClilocIndex );
        }

        [TestMethod]
        public void WillRaiseSavedEventOnSave()
        {
            File.WriteAllText( CustomPropertiesFile, "[]" );

            CustomPropertiesViewModel vm = new CustomPropertiesViewModel();
            bool raised = false;

            CustomPropertiesViewModel.Saved += ( sender, args ) => raised = true;

            vm.SaveCommand.Execute( null );

            Assert.IsTrue( raised );
        }
    }
}
