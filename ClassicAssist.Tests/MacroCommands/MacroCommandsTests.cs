using System.Threading;
using ClassicAssist.Shared;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.UI.Misc;
using System;
using Xunit;
using MC = ClassicAssist.Data.Macros.Commands.MacroCommands;

namespace ClassicAssist.Tests.MacroCommands
{
    public class MacroCommandsTests : IDisposable
    {
        public MacroCommandsTests()
        {
            // MacroManager.Items has no default initializer - it's populated from disk on a real
            // app start, which never happens in a bare test process, so it's null unless some
            // earlier test in the process happened to touch it first. MacroEntry.Name's setter
            // reads it unconditionally (uniqueness check), so anything that constructs a
            // MacroEntry needs this guaranteed non-null regardless of run order.
            MacroManager.GetInstance().Items ??= new ObservableCollectionEx<MacroEntry>();
        }

        public void Dispose()
        {
            Engine.Journal.Clear();
        }

        [Fact]
        public void PlayMacroWillMessageForUnknownMacro()
        {
            MC.PlayMacro( "ThisMacroDoesNotExist" );

            Assert.True( JournalCommands.InJournal( "Unknown macro", "system" ) );
        }

        [Fact]
        public void PlayMacroWillInvokeMatchingMacroAction()
        {
            AutoResetEvent are = new AutoResetEvent( false );
            object[] receivedArgs = null;

            MacroEntry macro = new MacroEntry
            {
                Name = "PlayMacroInvokeTest",
                Action = ( entry, args ) =>
                {
                    receivedArgs = args;
                    are.Set();
                }
            };

            MacroManager manager = MacroManager.GetInstance();
            manager.Items.Add( macro );

            MC.PlayMacro( "PlayMacroInvokeTest", "one", 2 );

            bool result = are.WaitOne( 5000 );

            manager.Items.Remove( macro );

            Assert.True( result );
            Assert.NotNull( receivedArgs );
            Assert.Equal( "one", receivedArgs[0] );
            Assert.Equal( 2, receivedArgs[1] );
        }

        [Fact]
        public void IsRunningWillMessageForUnknownMacro()
        {
            bool result = MC.IsRunning( "ThisMacroDoesNotExist" );

            Assert.False( result );
            Assert.True( JournalCommands.InJournal( "Unknown macro", "system" ) );
        }

        [Fact]
        public void IsRunningWillReflectMacroState()
        {
            MacroEntry macro = new MacroEntry { Name = "IsRunningTest", IsRunning = true };

            MacroManager manager = MacroManager.GetInstance();
            manager.Items.Add( macro );

            Assert.True( MC.IsRunning( "IsRunningTest" ) );

            macro.IsRunning = false;

            Assert.False( MC.IsRunning( "IsRunningTest" ) );

            manager.Items.Remove( macro );
        }

        [Fact]
        public void StopWillStopNamedMacro()
        {
            MacroEntry macro = new MacroEntry { Name = "StopNamedTest", IsRunning = true };

            MacroManager manager = MacroManager.GetInstance();
            manager.Items.Add( macro );

            MC.Stop( "StopNamedTest" );

            Assert.False( macro.IsRunning );

            manager.Items.Remove( macro );
        }

        [Fact]
        public void StopAllWillStopEveryRunningMacro()
        {
            MacroEntry macroA = new MacroEntry { Name = "StopAllA", IsRunning = true };
            MacroEntry macroB = new MacroEntry { Name = "StopAllB", IsRunning = true };

            MacroManager manager = MacroManager.GetInstance();
            manager.Items.Add( macroA );
            manager.Items.Add( macroB );

            MC.StopAll();

            Assert.False( macroA.IsRunning );
            Assert.False( macroB.IsRunning );

            manager.Items.Remove( macroA );
            manager.Items.Remove( macroB );
        }

        [Fact]
        public void ReplayWontThrowWithNoCurrentMacro()
        {
            MC.Replay();
        }
    }
}
