using System.Threading;
using ClassicAssist.Shared;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.UI.Misc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MC = ClassicAssist.Data.Macros.Commands.MacroCommands;

namespace ClassicAssist.Tests.MacroCommands
{
    [TestClass]
    public class MacroCommandsTests
    {
        [TestInitialize]
        public void Initialize()
        {
            // MacroManager.Items has no default initializer - it's populated from disk on a real
            // app start, which never happens in a bare test process, so it's null unless some
            // earlier test in the process happened to touch it first. MacroEntry.Name's setter
            // reads it unconditionally (uniqueness check), so anything that constructs a
            // MacroEntry needs this guaranteed non-null regardless of run order.
            MacroManager.GetInstance().Items ??= new ObservableCollectionEx<MacroEntry>();
        }

        [TestCleanup]
        public void Cleanup()
        {
            Engine.Journal.Clear();
        }

        [TestMethod]
        public void PlayMacroWillMessageForUnknownMacro()
        {
            MC.PlayMacro( "ThisMacroDoesNotExist" );

            Assert.IsTrue( JournalCommands.InJournal( "Unknown macro", "system" ) );
        }

        [TestMethod]
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

            Assert.IsTrue( result );
            Assert.IsNotNull( receivedArgs );
            Assert.AreEqual( "one", receivedArgs[0] );
            Assert.AreEqual( 2, receivedArgs[1] );
        }

        [TestMethod]
        public void IsRunningWillMessageForUnknownMacro()
        {
            bool result = MC.IsRunning( "ThisMacroDoesNotExist" );

            Assert.IsFalse( result );
            Assert.IsTrue( JournalCommands.InJournal( "Unknown macro", "system" ) );
        }

        [TestMethod]
        public void IsRunningWillReflectMacroState()
        {
            MacroEntry macro = new MacroEntry { Name = "IsRunningTest", IsRunning = true };

            MacroManager manager = MacroManager.GetInstance();
            manager.Items.Add( macro );

            Assert.IsTrue( MC.IsRunning( "IsRunningTest" ) );

            macro.IsRunning = false;

            Assert.IsFalse( MC.IsRunning( "IsRunningTest" ) );

            manager.Items.Remove( macro );
        }

        [TestMethod]
        public void StopWillStopNamedMacro()
        {
            MacroEntry macro = new MacroEntry { Name = "StopNamedTest", IsRunning = true };

            MacroManager manager = MacroManager.GetInstance();
            manager.Items.Add( macro );

            MC.Stop( "StopNamedTest" );

            Assert.IsFalse( macro.IsRunning );

            manager.Items.Remove( macro );
        }

        [TestMethod]
        public void StopAllWillStopEveryRunningMacro()
        {
            MacroEntry macroA = new MacroEntry { Name = "StopAllA", IsRunning = true };
            MacroEntry macroB = new MacroEntry { Name = "StopAllB", IsRunning = true };

            MacroManager manager = MacroManager.GetInstance();
            manager.Items.Add( macroA );
            manager.Items.Add( macroB );

            MC.StopAll();

            Assert.IsFalse( macroA.IsRunning );
            Assert.IsFalse( macroB.IsRunning );

            manager.Items.Remove( macroA );
            manager.Items.Remove( macroB );
        }

        [TestMethod]
        public void ReplayWontThrowWithNoCurrentMacro()
        {
            MC.Replay();
        }
    }
}
