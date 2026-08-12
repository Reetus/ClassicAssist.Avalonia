using ClassicAssist.Shared;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UI.Misc;
using ClassicAssist.UO.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests.MacroCommands
{
    [TestClass]
    public class MainCommandsTests
    {
        [TestInitialize]
        public void Initialize()
        {
            // See MacroCommandsTests.Initialize - MacroManager.Items is null until something
            // populates it, which a bare test process never does on its own.
            MacroManager.GetInstance().Items ??= new ObservableCollectionEx<MacroEntry>();
        }

        [TestCleanup]
        public void Cleanup()
        {
            Engine.Player = null;
            MacroManager.QuietMode = false;
            Engine.Journal.Clear();
        }

        [TestMethod]
        public void SetQuietModeWillSetMacroManagerQuietMode()
        {
            MainCommands.SetQuietMode( true );

            Assert.IsTrue( MacroManager.QuietMode );

            MainCommands.SetQuietMode( false );

            Assert.IsFalse( MacroManager.QuietMode );
        }

        [TestMethod]
        public void InvokeVirtueWillSendPacketForValidVirtue()
        {
            byte[] sent = null;

            void OnSent( byte[] data, int length )
            {
                sent = data;
            }

            Engine.InternalPacketSentEvent += OnSent;

            MainCommands.InvokeVirtue( "Honor" );

            Engine.InternalPacketSentEvent -= OnSent;

            Assert.IsNotNull( sent );
            Assert.AreEqual( 0x12, sent[0] );
            Assert.AreEqual( 0xF4, sent[3] );
            Assert.AreEqual( (byte) Virtues.Honor, sent[4] );
        }

        [TestMethod]
        public void InvokeVirtueWillThrowForUnrecognisedName()
        {
            // Utility.GetEnumValueByName<T> throws rather than returning a sentinel for a name
            // that matches no Virtues member - unlike MovementCommands' Direction, Virtues has no
            // "Invalid" member for InvokeVirtue to guard against, so this propagates as-is (the
            // macro engine's own top-level handler is what turns it into a "macro error" message
            // for a real macro; nothing in InvokeVirtue itself catches it).
            Assert.ThrowsException<System.InvalidOperationException>(
                () => MainCommands.InvokeVirtue( "NotARealVirtue" ) );
        }

        [TestMethod]
        public void WarModeWillSendOnWhenOff()
        {
            Engine.Player = new PlayerMobile( 0x01 ) { Status = MobileStatus.None };

            byte[] sent = null;

            void OnSent( byte[] data, int length )
            {
                sent = data;
            }

            Engine.InternalPacketSentEvent += OnSent;

            MainCommands.WarMode( "on" );

            Engine.InternalPacketSentEvent -= OnSent;

            Assert.IsNotNull( sent );
            Assert.AreEqual( 0x72, sent[0] );
            Assert.AreEqual( 1, sent[1] );
        }

        [TestMethod]
        public void WarModeWontSendWhenAlreadyOn()
        {
            Engine.Player = new PlayerMobile( 0x01 ) { Status = MobileStatus.WarMode };

            bool sent = false;

            void OnSent( byte[] data, int length )
            {
                sent = true;
            }

            Engine.InternalPacketSentEvent += OnSent;

            MainCommands.WarMode( "on" );

            Engine.InternalPacketSentEvent -= OnSent;

            Assert.IsFalse( sent );
        }

        [TestMethod]
        public void WarModeToggleWillSendOff()
        {
            Engine.Player = new PlayerMobile( 0x01 ) { Status = MobileStatus.WarMode };

            byte[] sent = null;

            void OnSent( byte[] data, int length )
            {
                sent = data;
            }

            Engine.InternalPacketSentEvent += OnSent;

            MainCommands.WarMode();

            Engine.InternalPacketSentEvent -= OnSent;

            Assert.IsNotNull( sent );
            Assert.AreEqual( 0, sent[1] );
        }

        [TestMethod]
        public void WarModeWontThrowWithoutPlayer()
        {
            Engine.Player = null;

            MainCommands.WarMode( "on" );
        }

        [TestMethod]
        public void HotkeysOnWillEnableAndMessage()
        {
            HotkeyManager manager = HotkeyManager.GetInstance();
            manager.Enabled = false;

            MainCommands.Hotkeys( "on" );

            Assert.IsTrue( manager.Enabled );
            Assert.IsTrue( JournalCommands.InJournal( "Hotkeys enabled", "system" ) );
        }

        [TestMethod]
        public void HotkeysOffWillDisableAndMessage()
        {
            HotkeyManager manager = HotkeyManager.GetInstance();
            manager.Enabled = true;

            MainCommands.Hotkeys( "off" );

            Assert.IsFalse( manager.Enabled );
            Assert.IsTrue( JournalCommands.InJournal( "Hotkeys disabled", "system" ) );
        }

        [TestMethod]
        public void HotkeysToggleWillFlip()
        {
            HotkeyManager manager = HotkeyManager.GetInstance();
            manager.Enabled = false;

            MainCommands.Hotkeys();

            Assert.IsTrue( manager.Enabled );

            MainCommands.Hotkeys();

            Assert.IsFalse( manager.Enabled );
        }

        [TestMethod]
        public void PlayingWillBeFalseWithNoCurrentMacro()
        {
            MacroManager manager = MacroManager.GetInstance();
            manager.CurrentMacro = null;
            manager.Replay = false;

            Assert.IsFalse( MainCommands.Playing() );
        }

        [TestMethod]
        public void PlayingWillBeTrueWhenCurrentMacroRunning()
        {
            MacroManager manager = MacroManager.GetInstance();
            MacroEntry macro = new MacroEntry { Name = "PlayingCurrentTest", IsRunning = true };
            manager.CurrentMacro = macro;

            Assert.IsTrue( MainCommands.Playing() );

            manager.CurrentMacro = null;
        }

        [TestMethod]
        public void PlayingWithNameWillFindRunningMacro()
        {
            MacroManager manager = MacroManager.GetInstance();
            MacroEntry macro = new MacroEntry { Name = "PlayingNamedTest", IsRunning = true };
            manager.Items.Add( macro );

            Assert.IsTrue( MainCommands.Playing( "PlayingNamedTest" ) );

            macro.IsRunning = false;

            Assert.IsFalse( MainCommands.Playing( "PlayingNamedTest" ) );

            manager.Items.Remove( macro );
        }

        [TestMethod]
        public void PlayingWithUnknownNameWillBeFalse()
        {
            Assert.IsFalse( MainCommands.Playing( "ThisMacroDoesNotExist" ) );
        }
    }
}
