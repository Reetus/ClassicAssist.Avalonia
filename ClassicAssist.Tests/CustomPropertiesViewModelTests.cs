using System;
using System.IO;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UI.ViewModels.Autoloot;
using Xunit;
using Newtonsoft.Json;

namespace ClassicAssist.Tests
{
    public class CustomPropertiesViewModelTests : IDisposable
    {
        private string _originalStartupPath;
        private string _tempDir;

        public CustomPropertiesViewModelTests()
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

        private string CustomPropertiesFile => Path.Combine( _tempDir, "Data", "Properties.Custom.json" );

        [Fact]
        public void WillLoadCustomPropertiesFromFile()
        {
            PropertyEntry[] entries =
            {
                new PropertyEntry { Name = "Faster Casting", Clilocs = new[] { 1060401 }, ClilocIndex = -1 },
                new PropertyEntry { Name = "Lower Reagent Cost", Clilocs = new[] { 1060408 }, ClilocIndex = 0 }
            };

            File.WriteAllText( CustomPropertiesFile, JsonConvert.SerializeObject( entries ) );

            CustomPropertiesViewModel vm = new CustomPropertiesViewModel();

            Assert.Equal( 2, vm.Properties.Count );
            Assert.Equal( "Faster Casting", vm.Properties[0].Name );
            Assert.False( vm.Properties[0].Arguments );
            Assert.Equal( -1, vm.Properties[0].ArgumentIndex );
            Assert.Equal( "Lower Reagent Cost", vm.Properties[1].Name );
            Assert.True( vm.Properties[1].Arguments );
            Assert.Equal( 0, vm.Properties[1].ArgumentIndex );
        }

        [Fact]
        public void WillSaveCustomPropertiesRoundTrip()
        {
            File.WriteAllText( CustomPropertiesFile, "[]" );

            CustomPropertiesViewModel vm = new CustomPropertiesViewModel();

            vm.Properties.Add( new CustomProperty { Name = "Hit Lightning", Cliloc = 1060450 } );

            vm.SaveCommand.Execute( null );

            PropertyEntry[] parsed = JsonConvert.DeserializeObject<PropertyEntry[]>( File.ReadAllText( CustomPropertiesFile ) );

            Assert.NotNull( parsed );
            Assert.Single( parsed );
            Assert.Equal( "Hit Lightning", parsed[0].Name );
            Assert.Equal( 1060450, parsed[0].Clilocs[0] );
            Assert.Equal( -1, parsed[0].ClilocIndex );

            CustomPropertiesViewModel reloaded = new CustomPropertiesViewModel();

            Assert.Single( reloaded.Properties );
            Assert.Equal( "Hit Lightning", reloaded.Properties[0].Name );
            Assert.Equal( -1, reloaded.Properties[0].ArgumentIndex );
        }

        [Fact]
        public void WillPersistArgumentIndexWhenEdited()
        {
            File.WriteAllText( CustomPropertiesFile, "[]" );

            CustomPropertiesViewModel vm = new CustomPropertiesViewModel();

            CustomProperty property = new CustomProperty { Name = "Lower Mana Cost", Cliloc = 1060409, Arguments = true };
            Assert.Equal( 0, property.ArgumentIndex );

            property.ArgumentIndex = 1;
            vm.Properties.Add( property );

            vm.SaveCommand.Execute( null );

            PropertyEntry[] parsed = JsonConvert.DeserializeObject<PropertyEntry[]>( File.ReadAllText( CustomPropertiesFile ) );

            Assert.Equal( 1, parsed[0].ClilocIndex );
        }

        [Fact]
        public void WillRaiseSavedEventOnSave()
        {
            File.WriteAllText( CustomPropertiesFile, "[]" );

            CustomPropertiesViewModel vm = new CustomPropertiesViewModel();
            bool raised = false;

            void OnSaved( object sender, EventArgs args )
            {
                raised = true;
            }

            CustomPropertiesViewModel.Saved += OnSaved;

            try
            {
                vm.SaveCommand.Execute( null );
            }
            finally
            {
                CustomPropertiesViewModel.Saved -= OnSaved;
            }

            Assert.True( raised );
        }
    }
}
