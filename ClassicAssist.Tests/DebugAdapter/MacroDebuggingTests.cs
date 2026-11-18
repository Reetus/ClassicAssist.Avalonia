using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClassicAssist.Data.Macros;
using ClassicAssist.DebugAdapter;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.DebugAdapter
{
    /// <summary>
    ///     End to end: a file-backed macro launched through the DAP session runs on the real macro engine
    ///     (MacroInvoker's trace hook), stops at breakpoints, exposes its stack and variables, evaluates
    ///     expressions, steps, and runs to completion - or is released when the editor disconnects or the
    ///     macro is stopped while paused. This is the path an editor user actually exercises, and the
    ///     only one where the session, DebugManager, VariableInspector and the macro thread meet.
    /// </summary>
    public class MacroDebuggingTests : IAsyncLifetime
    {
        private const string LOOP_MACRO =
            "total = 0\n" + // 1
            "for i in range(3):\n" + // 2
            "    total = total + i\n" + // 3
            "result = total * 10\n"; // 4

        private readonly string _directory = Path.Combine( Path.GetTempPath(), "ca-dap-" + Guid.NewGuid().ToString( "N" ) );
        private DapHarness _dap;
        private DebugAdapterGlobals _globals;
        private DebugManager _manager;

        public async ValueTask InitializeAsync()
        {
            EngineState.Reset();
            Directory.CreateDirectory( _directory );
            _globals = new DebugAdapterGlobals();
            _manager = new DebugManager();
            DebugAdapterGlobals.SetInstance( _manager );
            _dap = await DapHarness.StartAsync( _manager );
        }

        public async ValueTask DisposeAsync()
        {
            await _dap.DisposeAsync();
            _globals.Dispose();

            try
            {
                Directory.Delete( _directory, true );
            }
            catch ( IOException )
            {
                // best effort
            }
        }

        private MacroEntry AddMacro( string source )
        {
            string path = Path.Combine( _directory, "macro.py" );
            File.WriteAllText( path, source );

            MacroEntry entry = new MacroEntry
            {
                Name = "debug-" + Guid.NewGuid().ToString( "N" ), FilePath = path, Macro = source
            };

            MacroManager.GetInstance().Items.Add( entry );

            return entry;
        }

        /// <summary>The editor's launch sequence; returns the first stopped event.</summary>
        private async Task<JObject> LaunchToFirstStopAsync( MacroEntry macro, params int[] lines )
        {
            await _dap.RequestAsync( "initialize" );
            await _dap.RequestAsync( "launch", new { program = macro.FilePath } );
            await _dap.RequestAsync( "setBreakpoints",
                new { source = new { path = macro.FilePath }, breakpoints = lines.Select( l => new { line = l } ).ToArray() } );
            await _dap.RequestAsync( "configurationDone" );

            return await _dap.WaitForEventAsync( "stopped" );
        }

        private async Task<JArray> TopFrameLocalsAsync( int threadId )
        {
            JObject stack = await _dap.RequestAsync( "stackTrace", new { threadId } );
            int frameId = (int) stack["body"]["stackFrames"][0]["id"];
            JObject scopes = await _dap.RequestAsync( "scopes", new { frameId } );
            int locals = (int) scopes["body"]["scopes"][0]["variablesReference"];
            JObject variables = await _dap.RequestAsync( "variables", new { variablesReference = locals } );

            return (JArray) variables["body"]["variables"];
        }

        private static string Value( JArray variables, string name )
        {
            return (string) Assert.Single( variables, v => (string) v["name"] == name )["value"];
        }

        private async Task<int> StoppedLineAsync( int threadId )
        {
            JObject stack = await _dap.RequestAsync( "stackTrace", new { threadId } );

            return (int) stack["body"]["stackFrames"][0]["line"];
        }

        private async Task WaitForExitAsync( MacroEntry macro, int threadId )
        {
            await _dap.WaitForEventAsync( "thread",
                e => (string) e["body"]["reason"] == "exited" && (int) e["body"]["threadId"] == threadId );

            Assert.True( macro.MacroInvoker.Thread.Join( DapHarness.Timeout ), "macro thread did not finish" );
        }

        [Fact]
        public async Task WillStopAtABreakpointAndExposeTheMacrosStackAndVariables()
        {
            MacroEntry macro = AddMacro( LOOP_MACRO );

            JObject stopped = await LaunchToFirstStopAsync( macro, 3 );
            int threadId = (int) stopped["body"]["threadId"];

            Assert.Equal( "breakpoint", (string) stopped["body"]["reason"] );
            Assert.Equal( macro.MacroInvoker.Thread.ManagedThreadId, threadId );

            JObject threads = await _dap.RequestAsync( "threads" );
            Assert.Contains( (JArray) threads["body"]["threads"],
                t => (int) t["id"] == threadId && (string) t["name"] == macro.Name );

            JObject stack = await _dap.RequestAsync( "stackTrace", new { threadId } );
            JToken top = stack["body"]["stackFrames"][0];
            Assert.Equal( 3, (int) top["line"] );
            Assert.Equal( threadId * 1000, (int) top["id"] );
            Assert.Equal( "macro.py", (string) top["source"]["name"] );
            Assert.Equal( Path.GetFullPath( macro.FilePath ), (string) top["source"]["path"] );

            JArray locals = await TopFrameLocalsAsync( threadId );
            Assert.Equal( "0", Value( locals, "i" ) );
            Assert.Equal( "0", Value( locals, "total" ) );

            JObject evaluated = await _dap.RequestAsync( "evaluate",
                new { expression = "total + i + 100", frameId = threadId * 1000 } );
            Assert.Equal( "100", (string) evaluated["body"]["result"] );
        }

        [Fact]
        public async Task WillStopAgainOnTheNextHitAfterContinue()
        {
            MacroEntry macro = AddMacro( LOOP_MACRO );
            int threadId = (int) ( await LaunchToFirstStopAsync( macro, 3 ) )["body"]["threadId"];

            await _dap.RequestAsync( "continue", new { threadId } );
            await _dap.WaitForEventAsync( "continued" );
            await _dap.WaitForEventAsync( "stopped" );

            JArray locals = await TopFrameLocalsAsync( threadId );
            Assert.Equal( "1", Value( locals, "i" ) );
            Assert.Equal( "0", Value( locals, "total" ) );
        }

        [Fact]
        public async Task WillRunToCompletionOnceBreakpointsAreCleared()
        {
            MacroEntry macro = AddMacro( LOOP_MACRO );
            int threadId = (int) ( await LaunchToFirstStopAsync( macro, 3 ) )["body"]["threadId"];

            await _dap.RequestAsync( "setBreakpoints", new { source = new { path = macro.FilePath } } );
            await _dap.RequestAsync( "continue", new { threadId } );

            await WaitForExitAsync( macro, threadId );

            Assert.False( macro.MacroInvoker.IsFaulted, $"{macro.MacroInvoker.Exception}" );
            Assert.Single( _dap.Received, m => (string) m["event"] == "stopped" );
        }

        [Fact]
        public async Task WillStepLineByLine()
        {
            MacroEntry macro = AddMacro( LOOP_MACRO );
            int threadId = (int) ( await LaunchToFirstStopAsync( macro, 2 ) )["body"]["threadId"];

            Assert.Equal( 2, await StoppedLineAsync( threadId ) );

            await _dap.RequestAsync( "next", new { threadId } );
            await _dap.WaitForEventAsync( "stopped" );
            Assert.Equal( 3, await StoppedLineAsync( threadId ) );
            Assert.Equal( "0", Value( await TopFrameLocalsAsync( threadId ), "total" ) );

            await _dap.RequestAsync( "stepIn", new { threadId } );
            await _dap.WaitForEventAsync( "stopped" );
            Assert.Equal( 2, await StoppedLineAsync( threadId ) );

            await _dap.RequestAsync( "next", new { threadId } );
            await _dap.WaitForEventAsync( "stopped" );
            Assert.Equal( 3, await StoppedLineAsync( threadId ) );
            Assert.Equal( "1", Value( await TopFrameLocalsAsync( threadId ), "i" ) );
        }

        /// <summary>
        ///     IronPython reports a module's first statement as a "call" trace event, not a "line" event.
        ///     MacroInvoker.OnTrace only consults the DAP breakpoints on "line" events (its own in-app
        ///     breakpoint check above it accepts "call" too), so a breakpoint on line 1 - which
        ///     setBreakpoints reports as verified - never stops and the macro runs straight through.
        /// </summary>
        [Fact( Skip = "BUG: a DAP breakpoint on a macro's first line is never hit (MacroInvoker.OnTrace ignores the 'call' event)" )]
        public async Task WillStopAtABreakpointOnTheMacrosFirstLine()
        {
            MacroEntry macro = AddMacro( LOOP_MACRO );

            JObject stopped = await LaunchToFirstStopAsync( macro, 1 );

            Assert.Equal( 1, await StoppedLineAsync( (int) stopped["body"]["threadId"] ) );
        }

        /// <summary>A value written while paused is the one the rest of the macro computes with.</summary>
        [Fact]
        public async Task WillUseAVariableChangedWhilePaused()
        {
            MacroEntry macro = AddMacro( LOOP_MACRO + "end = 0\n" );
            int threadId = (int) ( await LaunchToFirstStopAsync( macro, 4 ) )["body"]["threadId"];

            JObject stack = await _dap.RequestAsync( "stackTrace", new { threadId } );
            JObject scopes = await _dap.RequestAsync( "scopes", new { frameId = (int) stack["body"]["stackFrames"][0]["id"] } );
            int locals = (int) scopes["body"]["scopes"][0]["variablesReference"];

            JObject set = await _dap.RequestAsync( "setVariable", new { variablesReference = locals, name = "total", value = "7" } );
            Assert.True( (bool) set["success"] );
            Assert.Equal( "7", (string) set["body"]["value"] );

            await _dap.RequestAsync( "next", new { threadId } );
            await _dap.WaitForEventAsync( "stopped" );

            Assert.Equal( "70", Value( await TopFrameLocalsAsync( threadId ), "result" ) );
        }

        [Fact]
        public async Task WillStopOnARaisedExceptionWhenAskedTo()
        {
            MacroEntry macro = AddMacro( "x = 1\nraise ValueError('boom')\n" );

            await _dap.RequestAsync( "setExceptionBreakpoints", new { filters = new[] { "all" } } );
            JObject stopped = await LaunchToFirstStopAsync( macro );
            int threadId = (int) stopped["body"]["threadId"];

            Assert.Equal( "exception", (string) stopped["body"]["reason"] );

            JObject info = await _dap.RequestAsync( "exceptionInfo", new { threadId } );
            Assert.Equal( "Python exception", (string) info["body"]["description"] );
            Assert.Equal( "always", (string) info["body"]["breakMode"] );

            await _dap.RequestAsync( "setExceptionBreakpoints", new { filters = new string[0] } );
            await _dap.RequestAsync( "continue", new { threadId } );

            await WaitForExitAsync( macro, threadId );
            Assert.True( macro.MacroInvoker.IsFaulted );
        }

        [Fact]
        public async Task WillPauseARunningMacro()
        {
            MacroEntry macro = AddMacro( "n = 0\nwhile n >= 0:\n    n = n + 1\n" );

            await _dap.RequestAsync( "initialize" );
            await _dap.RequestAsync( "launch", new { program = macro.FilePath } );
            await _dap.RequestAsync( "configurationDone" );

            JObject started = await _dap.WaitForEventAsync( "thread" );
            int threadId = (int) started["body"]["threadId"];

            await _dap.RequestAsync( "pause", new { threadId } );
            await _dap.WaitForEventAsync( "stopped" );

            Assert.True( _manager.GetThreadState( threadId ).IsPaused );
            Assert.Contains( await StoppedLineAsync( threadId ), new[] { 2, 3 } );
        }

        /// <summary>Disconnecting while a macro sits at a breakpoint lets it run on to the end.</summary>
        [Fact]
        public async Task WillLetAPausedMacroFinishWhenTheEditorDisconnects()
        {
            MacroEntry macro = AddMacro( LOOP_MACRO );
            int threadId = (int) ( await LaunchToFirstStopAsync( macro, 3 ) )["body"]["threadId"];

            await _dap.RequestAsync( "disconnect" );

            Assert.True( macro.MacroInvoker.Thread.Join( DapHarness.Timeout ), "macro stayed paused after disconnect" );
            Assert.False( macro.MacroInvoker.IsFaulted, $"{macro.MacroInvoker.Exception}" );
            Assert.Null( _manager.GetThreadState( threadId ) );
        }

        /// <summary>Stopping a macro from the assistant while it is paused must not leave its thread parked.</summary>
        [Fact]
        public async Task WillEndAPausedMacroThatIsStopped()
        {
            MacroEntry macro = AddMacro( LOOP_MACRO );
            int threadId = (int) ( await LaunchToFirstStopAsync( macro, 3 ) )["body"]["threadId"];

            macro.Stop();

            await WaitForExitAsync( macro, threadId );
            Assert.False( macro.IsRunning );
        }

        [Fact]
        public async Task WillPassLaunchArgumentsToTheMacro()
        {
            MacroEntry macro = AddMacro( "first = args[0]\ncount = len(args)\nend = 0\n" );

            await _dap.RequestAsync( "initialize" );
            await _dap.RequestAsync( "launch", new { program = macro.FilePath, args = " alpha, beta ,," } );
            await _dap.RequestAsync( "setBreakpoints",
                new { source = new { path = macro.FilePath }, breakpoints = new[] { new { line = 3 } } } );
            await _dap.RequestAsync( "configurationDone" );

            int threadId = (int) ( await _dap.WaitForEventAsync( "stopped" ) )["body"]["threadId"];
            JArray locals = await TopFrameLocalsAsync( threadId );

            Assert.Equal( "'alpha'", Value( locals, "first" ) );
            Assert.Equal( "2", Value( locals, "count" ) );
        }
    }
}
