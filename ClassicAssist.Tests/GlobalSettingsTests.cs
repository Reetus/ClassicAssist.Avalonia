using System;
using System.IO;
using System.Linq;
using ClassicAssist.Data;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Hotkeys.Commands;
using ClassicAssist.Data.Macros;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.ViewModels;
using Xunit;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Tests
{
    public class GlobalSettingsTests : IDisposable
    {
        private string _globalDirectory;

        public GlobalSettingsTests()
        {
            _globalDirectory = Path.Combine( Path.GetTempPath(), $"cal-test-{Guid.NewGuid():N}" );
            Directory.CreateDirectory( _globalDirectory );
            AssistantOptions.GlobalDirectory = _globalDirectory;
        }

        public void Dispose()
        {
            AssistantOptions.GlobalDirectory = ".";

            if ( Directory.Exists( _globalDirectory ) )
            {
                Directory.Delete( _globalDirectory, true );
            }
        }

        [Fact]
        public void GlobalMacroDeserializeForcesGlobalFlag()
        {
            // A file that predates the Global flag, or was written by a build that didn't persist it -
            // the flag must be asserted on load, not trusted from the file.
            JArray globalFile = new JArray
            {
                new JObject
                {
                    { "Name", "TestMacro" },
                    { "Macro", "print(1)" },
                    { "Keys", new JObject { { "Keys", 0 }, { "Modifier", 0 }, { "Mouse", 0 } } },
                    { "Aliases", new JArray() }
                }
            };

            File.WriteAllText( Path.Combine( _globalDirectory, "Macros.json" ), globalFile.ToString() );

            MacrosTabViewModel vm = new MacrosTabViewModel();
            vm.Deserialize( null, new Options() );

            MacroEntry entry = vm.Items.FirstOrDefault( e => e.Name == "TestMacro" );
            Assert.NotNull( entry );
            Assert.True( entry.Global );
        }

        [Fact]
        public void GlobalMacroSerializeWritesPortableFormat()
        {
            MacrosTabViewModel vm = new MacrosTabViewModel();

            MacroEntry macro = new MacroEntry
            {
                Name = "PortableMacro",
                Macro = "print(2)",
                Global = true,
                Hotkey = new ShortcutKeys( Key.None, Key.F1 )
            };

            macro.Aliases.Add( "self", 0x00000001 );
            vm.Items.Add( macro );

            vm.Serialize( new JObject() );

            string file = Path.Combine( _globalDirectory, "Macros.json" );
            Assert.True( File.Exists( file ) );

            JArray json = JArray.Parse( File.ReadAllText( file ) );
            JObject entry = json.FirstOrDefault( t => t["Name"]?.ToObject<string>() == "PortableMacro" ) as JObject;

            Assert.NotNull( entry );
            Assert.True( entry["Global"].ToObject<bool>() );
            Assert.NotNull( entry["Keys"] );
            Assert.Null( entry["IsRunning"] );
            Assert.Single( entry["Aliases"] );
        }

        [Fact]
        public void HotkeyGlobalPassSeparatesIsGlobalEntries()
        {
            HotkeysTabViewModel vm = new HotkeysTabViewModel();
            Options options = new Options();

            vm.Deserialize( new JObject(), options );

            HotkeyCommand category =
                HotkeyManager.GetInstance().Items.FirstOrDefault( c => c.IsCategory && c.Name == Strings.Commands && c.Children?.Count > 0 );

            HotkeyEntry globalEntry = category?.Children.FirstOrDefault( e => !e.IsCategory );
            Assert.NotNull( globalEntry );

            globalEntry.Hotkey = new ShortcutKeys( Key.None, Key.F1 );
            globalEntry.IsGlobal = true;

            JObject globalJson = new JObject();
            vm.Serialize( globalJson, true );

            JObject profileJson = new JObject();
            vm.Serialize( profileJson, false );

            JArray globalCommands = (JArray) globalJson["Hotkeys"]?["Commands"];
            JArray profileCommands = (JArray) profileJson["Hotkeys"]?["Commands"];

            Assert.NotNull( globalCommands );
            Assert.Contains( globalCommands, t => t["Type"]?.ToObject<string>() == globalEntry.GetType().FullName );
            Assert.DoesNotContain( profileCommands, t => t["Type"]?.ToObject<string>() == globalEntry.GetType().FullName );

            vm.Deserialize( globalJson, options, true );

            Assert.True( globalEntry.IsGlobal );
            Assert.Equal( globalEntry.Hotkey, new ShortcutKeys( Key.None, Key.F1 ) );
        }
    }
}
