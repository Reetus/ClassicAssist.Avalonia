using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared;
using Microsoft.Scripting.Hosting;
using Xunit;

namespace ClassicAssist.Tests.Macros
{
    /// <summary>
    ///     What the macro script sees around it: print output turned into system messages (decoded with
    ///     the encoding the DLR actually used, which differs per platform), and the Modules folder beside
    ///     the app on the import path. Both have broken in the port - mis-decoded output and modules that
    ///     could not be imported - and neither shows up until a user's macro runs.
    /// </summary>
    public class MacroScriptEnvironmentTests : IDisposable
    {
        private readonly MacroHarness _harness = new MacroHarness();
        private readonly string _originalStartupPath = Engine.StartupPath;
        private readonly List<string> _tempDirectories = new List<string>();

        public void Dispose()
        {
            _harness.Dispose();
            Engine.StartupPath = _originalStartupPath;

            foreach ( string directory in _tempDirectories )
            {
                try
                {
                    Directory.Delete( directory, true );
                }
                catch ( IOException )
                {
                    // Best effort; a temp dir is cleaned up eventually anyway.
                }
            }
        }

        private static ScriptEngine Engine_()
        {
            return (ScriptEngine) typeof( MacroInvoker ).GetField( "_engine", BindingFlags.Static | BindingFlags.NonPublic )
                ?.GetValue( null );
        }

        [Fact]
        public void PrintWillBecomeASystemMessage()
        {
            MacroInvoker invoker = _harness.RunToEnd( "print('Hello from the macro')" );

            Assert.False( invoker.IsFaulted, invoker.Exception?.ToString() );
            Assert.True( JournalCommands.InJournal( "Hello from the macro", "system" ) );
        }

        /// <summary>
        ///     Decoding with the wrong encoding turns non-ASCII output into mojibake (or plain ASCII into
        ///     CJK on Linux), so check characters outside ASCII and outside Latin-1 survive.
        /// </summary>
        [Fact]
        public void PrintWillKeepNonAsciiText()
        {
            MacroInvoker invoker = _harness.RunToEnd( "print(u'Caf\\u00e9 \\u2713 \\u4e2d')" );

            Assert.False( invoker.IsFaulted, invoker.Exception?.ToString() );
            // e-acute (Latin-1), check mark (BMP symbol), and a CJK ideograph.
            string expected = "Caf" + (char) 0xE9 + " " + (char) 0x2713 + " " + (char) 0x4E2D;

            Assert.True( JournalCommands.InJournal( expected, "system" ) );
        }

        [Fact]
        public void StderrWillAlsoBecomeASystemMessage()
        {
            MacroInvoker invoker = _harness.RunToEnd( "sys.stderr.write('written to stderr\\n')" );

            Assert.False( invoker.IsFaulted, invoker.Exception?.ToString() );
            Assert.True( JournalCommands.InJournal( "written to stderr", "system" ) );
        }

        /// <summary>
        ///     Blank lines (print() on its own) are dropped rather than sent as empty system messages.
        /// </summary>
        [Fact]
        public void WontSendEmptySystemMessagesForBlankLines()
        {
            JournalCommands.ClearJournal();

            MacroInvoker invoker = _harness.RunToEnd( "print()\nprint('')" );

            Assert.False( invoker.IsFaulted, invoker.Exception?.ToString() );
            Assert.False( Engine.Journal.FindAny( je => je.Text != null && je.Text.Trim().Length == 0, "main" ) );
        }

        [Fact]
        public void WillImportAModuleFromTheModulesFolderBesideTheApp()
        {
            string startup = Path.Combine( Path.GetTempPath(), "CAModules" + Guid.NewGuid().ToString( "N" ) );
            string modules = Path.Combine( startup, "Modules" );
            _tempDirectories.Add( startup );
            Directory.CreateDirectory( modules );

            string moduleName = "camod_" + Guid.NewGuid().ToString( "N" ).Substring( 0, 8 );

            File.WriteAllText( Path.Combine( modules, moduleName + ".py" ),
                "from ClassicAssist.Data.Macros.Commands.AliasCommands import SetAlias\n" +
                "def mark(value):\n    SetAlias('module', value)\n" );

            Engine.StartupPath = startup;

            // The search path is taken when the invoker is built, i.e. with the entry.
            MacroInvoker invoker = _harness.RunToEnd( $"import {moduleName}\n{moduleName}.mark(17)" );

            Assert.False( invoker.IsFaulted, invoker.Exception?.ToString() );
            Assert.Equal( 17, MacroHarness.Alias( "module" ) );
        }

        [Fact]
        public void WontAddTheModulesFolderToTheSearchPathTwice()
        {
            string startup = Path.Combine( Path.GetTempPath(), "CAModules" + Guid.NewGuid().ToString( "N" ) );
            _tempDirectories.Add( startup );
            Engine.StartupPath = startup;

            string modules = Path.Combine( startup, "Modules" );

            _ = new MacroInvoker();
            _ = new MacroInvoker();

            ScriptEngine engine = Engine_();

            Assert.NotNull( engine );
            Assert.Single( engine.GetSearchPaths(), p => p == modules );
        }
    }
}
