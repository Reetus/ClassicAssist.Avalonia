using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ClassicAssist.Shared;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Hotkeys.Commands;
using ClassicAssist.UI.Misc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using Key = ClassicAssist.Misc.Key;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class HotkeyTests
    {
        [TestMethod]
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
    }
}
namespace ClassicAssist.Tests
{
    [TestClass]
    public class ShortcutKeysTests
    {
        [TestMethod]
        public void WillReadSdlModifierFromClassicAssistProfile()
        {
            // WPF writes SDLModifier (KMOD_LALT = 0x0100 = 256) instead of "Modifier". The Keys value is
            // the shared System.Windows.Input.Key-aligned enum (50 = G), so it reads straight through.
            JObject json = new JObject
            {
                { "Keys", 50 }, { "SDLModifier", 256 }, { "Mouse", 7 }
            };

            ShortcutKeys keys = new ShortcutKeys( json );

            Assert.AreEqual( Key.G, keys.Key );
            Assert.AreEqual( Key.LeftAlt, keys.Modifier );
            Assert.AreEqual( MouseOptions.None, keys.Mouse );
        }

        [TestMethod]
        public void WillReadSdlModifierCtrlShift()
        {
            JObject json = new JObject
            {
                { "Keys", 66 }, { "SDLModifier", 1 | 64 }, { "Mouse", 0 }
            };

            ShortcutKeys keys = new ShortcutKeys( json );

            Assert.AreEqual( Key.W, keys.Key );
            Assert.AreEqual( Key.LeftCtrl, keys.Modifier );
        }

        [TestMethod]
        public void WillReadLegacyModifierForBackCompat()
        {
            JObject json = new JObject
            {
                { "Keys", 66 }, { "Modifier", (int) Key.LeftShift }, { "Mouse", 0 }
            };

            ShortcutKeys keys = new ShortcutKeys( json );

            Assert.AreEqual( Key.W, keys.Key );
            Assert.AreEqual( Key.LeftShift, keys.Modifier );
        }

        [TestMethod]
        public void WillSerializeSdlModifierLikeWpf()
        {
            ShortcutKeys keys = new ShortcutKeys( Key.LeftAlt, Key.G ) { Mouse = MouseOptions.None };

            JObject json = keys.ToJObject();

            Assert.AreEqual( 50, (int) json["Keys"] );
            Assert.AreEqual( 256, (int) json["SDLModifier"] );
            Assert.IsNull( json["Modifier"] );
        }

        [TestMethod]
        public void WillRoundTripSdlModifier()
        {
            ShortcutKeys original = new ShortcutKeys( Key.LeftAlt, Key.G );

            ShortcutKeys reloaded = new ShortcutKeys( original.ToJObject() );

            Assert.AreEqual( original, reloaded );
        }
    }
}
