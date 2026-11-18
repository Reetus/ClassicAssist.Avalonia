using ClassicAssist.Shared;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UI.Misc;
using ClassicAssist.UO.Objects;
using System;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class MainCommandsTests : IDisposable
    {
        public MainCommandsTests()
        {
            // See MacroCommandsTests.Initialize - MacroManager.Items is null until something
            // populates it, which a bare test process never does on its own.
            MacroManager.GetInstance().Items ??= new ObservableCollectionEx<MacroEntry>();
        }

        public void Dispose()
        {
            Engine.Player = null;
            MacroManager.QuietMode = false;
            Engine.Journal.Clear();
        }

        [Fact]
        public void SetQuietModeWillSetMacroManagerQuietMode()
        {
            MainCommands.SetQuietMode( true );

            Assert.True( MacroManager.QuietMode );

            MainCommands.SetQuietMode( false );

            Assert.False( MacroManager.QuietMode );
        }

        [Fact]
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

            Assert.NotNull( sent );
            Assert.Equal( 0x12, sent[0] );
            Assert.Equal( 0xF4, sent[3] );
            Assert.Equal( (byte) Virtues.Honor, sent[4] );
        }

        [Fact]
        public void InvokeVirtueWillThrowForUnrecognisedName()
        {
            // Utility.GetEnumValueByName<T> throws rather than returning a sentinel for a name
            // that matches no Virtues member - unlike MovementCommands' Direction, Virtues has no
            // "Invalid" member for InvokeVirtue to guard against, so this propagates as-is (the
            // macro engine's own top-level handler is what turns it into a "macro error" message
            // for a real macro; nothing in InvokeVirtue itself catches it).
            Assert.Throws<System.InvalidOperationException>(
                () => MainCommands.InvokeVirtue( "NotARealVirtue" ) );
        }

        [Fact]
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

            Assert.NotNull( sent );
            Assert.Equal( 0x72, sent[0] );
            Assert.Equal( 1, sent[1] );
        }

        [Fact]
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

            Assert.False( sent );
        }

        [Fact]
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

            Assert.NotNull( sent );
            Assert.Equal( 0, sent[1] );
        }

        [Fact]
        public void WarModeWontThrowWithoutPlayer()
        {
            Engine.Player = null;

            MainCommands.WarMode( "on" );
        }

        [Fact]
        public void HotkeysOnWillEnableAndMessage()
        {
            HotkeyManager manager = HotkeyManager.GetInstance();
            manager.Enabled = false;

            MainCommands.Hotkeys( "on" );

            Assert.True( manager.Enabled );
            Assert.True( JournalCommands.InJournal( "Hotkeys enabled", "system" ) );
        }

        [Fact]
        public void HotkeysOffWillDisableAndMessage()
        {
            HotkeyManager manager = HotkeyManager.GetInstance();
            manager.Enabled = true;

            MainCommands.Hotkeys( "off" );

            Assert.False( manager.Enabled );
            Assert.True( JournalCommands.InJournal( "Hotkeys disabled", "system" ) );
        }

        [Fact]
        public void HotkeysToggleWillFlip()
        {
            HotkeyManager manager = HotkeyManager.GetInstance();
            manager.Enabled = false;

            MainCommands.Hotkeys();

            Assert.True( manager.Enabled );

            MainCommands.Hotkeys();

            Assert.False( manager.Enabled );
        }

        [Fact]
        public void PlayingWillBeFalseWithNoCurrentMacro()
        {
            MacroManager manager = MacroManager.GetInstance();
            manager.CurrentMacro = null;
            manager.Replay = false;

            Assert.False( MainCommands.Playing() );
        }

        [Fact]
        public void PlayingWillBeTrueWhenCurrentMacroRunning()
        {
            MacroManager manager = MacroManager.GetInstance();
            MacroEntry macro = new MacroEntry { Name = "PlayingCurrentTest", IsRunning = true };
            manager.CurrentMacro = macro;

            Assert.True( MainCommands.Playing() );

            manager.CurrentMacro = null;
        }

        [Fact]
        public void PlayingWithNameWillFindRunningMacro()
        {
            MacroManager manager = MacroManager.GetInstance();
            MacroEntry macro = new MacroEntry { Name = "PlayingNamedTest", IsRunning = true };
            manager.Items.Add( macro );

            Assert.True( MainCommands.Playing( "PlayingNamedTest" ) );

            macro.IsRunning = false;

            Assert.False( MainCommands.Playing( "PlayingNamedTest" ) );

            manager.Items.Remove( macro );
        }

        [Fact]
        public void PlayingWithUnknownNameWillBeFalse()
        {
            Assert.False( MainCommands.Playing( "ThisMacroDoesNotExist" ) );
        }
    }
}
