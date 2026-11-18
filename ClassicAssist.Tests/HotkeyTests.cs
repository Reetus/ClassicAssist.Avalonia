using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ClassicAssist.Shared;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Hotkeys.Commands;
using ClassicAssist.UI.Misc;
using ClassicAssist.UI.ViewModels.Hotkeys;
using Xunit;
using Newtonsoft.Json.Linq;
using Key = ClassicAssist.Misc.Key;

namespace ClassicAssist.Tests
{
    public class HotkeyTests
    {
        [Fact]
        public void WillInstantiateAllHotkeysCommandsNoExceptions()
        {
            IEnumerable<Type> hotkeyCommands = Assembly.GetAssembly( typeof( Engine ) ).GetTypes()
                .Where( i => i.IsSubclassOf( typeof( HotkeyCommand ) ) );

            ObservableCollectionEx<HotkeyCommand> hotkeys = new ObservableCollectionEx<HotkeyCommand>();

            foreach ( Type hotkeyCommand in hotkeyCommands )
            {
                HotkeyCommand hkc = (HotkeyCommand) Activator.CreateInstance( hotkeyCommand );

                hotkeys.Add( hkc );
            }
        }

        [Fact]
        public void WillBuildConfigurationEntryFromHotkeyConfigurationAttribute()
        {
            GreaterHealCureSelf command = new GreaterHealCureSelf { CureType = CureType.ArchCure };

            HotkeyOptionsViewModel vm = new HotkeyOptionsViewModel( command );

            Assert.True( command.Configurable );
            Assert.Single( vm.Entries );

            HotkeyOptionEntry entry = vm.Entries[0];

            Assert.Equal( "Cure Type", entry.Name );

            // Both members of CureType, labelled from their [Description] via the resource manager.
            Assert.Equivalent( new[] { "Cure", "Arch Cure" },
                entry.Values.Select( v => v.DisplayName ).ToArray(), strict: true );

            // The dialog opens on whatever the command currently holds.
            Assert.Equal( CureType.ArchCure, entry.SelectedValue.Value );
        }

        [Fact]
        public void WillWriteConfigurationValueBackOnOk()
        {
            MiniHealCureSelf command = new MiniHealCureSelf { CureType = CureType.Cure };

            HotkeyOptionsViewModel vm = new HotkeyOptionsViewModel( command );

            HotkeyOptionEntry entry = vm.Entries[0];

            entry.SelectedValue = entry.Values.First( v => Equals( v.Value, CureType.ArchCure ) );

            // Nothing is written until OK - closing the dialog any other way leaves the command alone.
            Assert.Equal( CureType.Cure, command.CureType );

            vm.OkCommand.Execute( null );

            Assert.Equal( CureType.ArchCure, command.CureType );
        }
    }
}
namespace ClassicAssist.Tests
{
    public class ShortcutKeysTests
    {
        [Fact]
        public void WillReadSdlModifierFromClassicAssistProfile()
        {
            // WPF writes SDLModifier (KMOD_LALT = 0x0100 = 256) instead of "Modifier". The Keys value is
            // the shared System.Windows.Input.Key-aligned enum (50 = G), so it reads straight through.
            JObject json = new JObject
            {
                { "Keys", 50 }, { "SDLModifier", 256 }, { "Mouse", 7 }
            };

            ShortcutKeys keys = new ShortcutKeys( json );

            Assert.Equal( Key.G, keys.Key );
            Assert.Equal( Key.LeftAlt, keys.Modifier );
            Assert.Equal( MouseOptions.None, keys.Mouse );
        }

        [Fact]
        public void WillReadSdlModifierCtrlShift()
        {
            JObject json = new JObject
            {
                { "Keys", 66 }, { "SDLModifier", 1 | 64 }, { "Mouse", 0 }
            };

            ShortcutKeys keys = new ShortcutKeys( json );

            Assert.Equal( Key.W, keys.Key );
            Assert.Equal( Key.LeftCtrl, keys.Modifier );
        }

        [Fact]
        public void WillReadLegacyModifierForBackCompat()
        {
            JObject json = new JObject
            {
                { "Keys", 66 }, { "Modifier", (int) Key.LeftShift }, { "Mouse", 0 }
            };

            ShortcutKeys keys = new ShortcutKeys( json );

            Assert.Equal( Key.W, keys.Key );
            Assert.Equal( Key.LeftShift, keys.Modifier );
        }

        [Fact]
        public void WillSerializeSdlModifierLikeWpf()
        {
            ShortcutKeys keys = new ShortcutKeys( Key.LeftAlt, Key.G ) { Mouse = MouseOptions.None };

            JObject json = keys.ToJObject();

            Assert.Equal( 50, (int) json["Keys"] );
            Assert.Equal( 256, (int) json["SDLModifier"] );
            Assert.Null( json["Modifier"] );
        }

        [Fact]
        public void WillRoundTripSdlModifier()
        {
            ShortcutKeys original = new ShortcutKeys( Key.LeftAlt, Key.G );

            ShortcutKeys reloaded = new ShortcutKeys( original.ToJObject() );

            Assert.Equal( original, reloaded );
        }
    }
}
