using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Data.Macros;
using ClassicAssist.DebugAdapter;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json.Linq;
using Xunit;
using Engine = ClassicAssist.Shared.Engine;

namespace ClassicAssist.Tests.DebugAdapter
{
    /// <summary>
    ///     A <see cref="DapSession" /> driven over a loopback socket by a strict DAP client: framing on
    ///     the read side (split, batched, multi-byte, malformed input), response correlation and sequence
    ///     numbering on the write side, and each request handler's answer when no macro is paused - the
    ///     state the editor is in for most of a session. Paused-macro behaviour is in
    ///     <see cref="MacroDebuggingTests" />.
    /// </summary>
    public class DapSessionTests : IAsyncLifetime
    {
        private DapHarness _dap;
        private DebugAdapterGlobals _globals;
        private DebugManager _manager;

        public async ValueTask InitializeAsync()
        {
            EngineState.Reset();
            _globals = new DebugAdapterGlobals();
            _manager = new DebugManager();
            _dap = await DapHarness.StartAsync( _manager );
        }

        public async ValueTask DisposeAsync()
        {
            await _dap.DisposeAsync();
            _globals.Dispose();
        }

        [Fact]
        public async Task WillAnswerInitializeWithCapabilitiesThenSendInitialized()
        {
            JObject response = await _dap.RequestAsync( "initialize", new { adapterID = "classicassist" } );

            Assert.True( (bool) response["success"] );
            Assert.Equal( "initialize", (string) response["command"] );
            Assert.True( (bool) response["body"]["supportsConfigurationDoneRequest"] );
            Assert.True( (bool) response["body"]["supportsConditionalBreakpoints"] );
            Assert.True( (bool) response["body"]["supportsLogPoints"] );
            Assert.False( (bool) response["body"]["supportsStepBack"] );

            JToken filter = Assert.Single( (JArray) response["body"]["exceptionBreakpointFilters"] );
            Assert.Equal( "all", (string) filter["filter"] );

            JObject initialized = await _dap.WaitForEventAsync( "initialized" );

            Assert.True( (int) initialized["seq"] > (int) response["seq"], "initialized must follow the response" );
        }

        /// <summary>
        ///     Responses and events share one outgoing sequence starting at 1, independent of the client's
        ///     numbering; request_seq carries the client's number back.
        /// </summary>
        [Fact]
        public async Task WillNumberOutgoingMessagesFromOneSequenceAndEchoTheRequestSeq()
        {
            await _dap.WriteRawAsync( DapHarness.Frame( _dap.RequestJson( 100, "threads" ) ) );
            JObject first = await _dap.WaitForResponseAsync( 100 );

            await _dap.WriteRawAsync( DapHarness.Frame( _dap.RequestJson( 7, "bogus" ) ) );
            JObject second = await _dap.WaitForResponseAsync( 7 );

            await _dap.WriteRawAsync( DapHarness.Frame( _dap.RequestJson( 7, "threads" ) ) );
            JObject third = await _dap.WaitForResponseAsync( 7 );

            Assert.Equal( new[] { 1, 2, 3 }, new[] { (int) first["seq"], (int) second["seq"], (int) third["seq"] } );
            Assert.Equal( "threads", (string) third["command"] );
        }

        [Fact]
        public async Task WillAnswerAnUnknownCommandWithAnErrorResponse()
        {
            JObject response = await _dap.RequestAsync( "frobnicate" );

            Assert.False( (bool) response["success"] );
            Assert.Equal( "frobnicate", (string) response["command"] );
            Assert.Equal( "Unknown command: frobnicate", (string) response["message"] );
        }

        [Fact]
        public async Task WillSkipABodyThatIsNotJsonAndKeepServing()
        {
            await _dap.WriteRawAsync( Encoding.ASCII.GetBytes( "Content-Length: 5\r\n\r\n{oops" ) );
            int seq = await _dap.SendAsync( "threads" );

            JObject response = await _dap.WaitForResponseAsync( seq );

            Assert.Same( response, _dap.Received.First() );
        }

        [Fact]
        public async Task WillReassembleARequestWrittenOneByteAtATime()
        {
            byte[] framed = DapHarness.Frame( _dap.RequestJson( 1, "threads" ) );

            foreach ( byte b in framed )
            {
                await _dap.WriteRawAsync( new[] { b } );
            }

            JObject response = await _dap.WaitForResponseAsync( 1 );

            Assert.True( (bool) response["success"] );
        }

        [Fact]
        public async Task WillAnswerSeveralRequestsWrittenTogetherInOrder()
        {
            List<byte> batch = new List<byte>();
            batch.AddRange( DapHarness.Frame( _dap.RequestJson( 1, "threads" ) ) );
            batch.AddRange( DapHarness.Frame( _dap.RequestJson( 2, "nope" ) ) );
            batch.AddRange( DapHarness.Frame( _dap.RequestJson( 3, "threads" ) ) );

            await _dap.WriteRawAsync( batch.ToArray() );
            await _dap.WaitForResponseAsync( 3 );

            Assert.Equal( new[] { 1, 2, 3 }, _dap.Received.Select( m => (int) m["request_seq"] ) );
        }

        /// <summary>
        ///     A request with multi-byte characters, immediately followed by another in the same write:
        ///     counting characters instead of bytes would cut the first short and corrupt the second.
        /// </summary>
        [Fact]
        public async Task WillFrameMultiByteRequestsAndResponsesByByteCount()
        {
            List<byte> batch = new List<byte>();
            batch.AddRange( DapHarness.Frame( _dap.RequestJson( 1, "évaluer✓日本" ) ) );
            batch.AddRange( DapHarness.Frame( _dap.RequestJson( 2, "threads" ) ) );

            await _dap.WriteRawAsync( batch.ToArray() );

            JObject unicode = await _dap.WaitForResponseAsync( 1 );
            JObject next = await _dap.WaitForResponseAsync( 2 );

            Assert.Equal( "Unknown command: évaluer✓日本", (string) unicode["message"] );
            Assert.True( (bool) next["success"] );
        }

        [Fact]
        public async Task WillVerifyEveryRequestedBreakpointAndInstallThem()
        {
            string path = Path.Combine( Path.GetTempPath(), "ca-dap", "loot.py" );

            JObject response = await _dap.RequestAsync( "setBreakpoints",
                new { source = new { path }, breakpoints = new[] { new { line = 3 }, new { line = 8 } } } );

            Assert.True( (bool) response["success"] );
            JArray breakpoints = (JArray) response["body"]["breakpoints"];
            Assert.Equal( new[] { 1, 2 }, breakpoints.Select( b => (int) b["id"] ) );
            Assert.Equal( new[] { 3, 8 }, breakpoints.Select( b => (int) b["line"] ) );
            Assert.All( breakpoints, b => Assert.True( (bool) b["verified"] ) );
            Assert.All( breakpoints, b => Assert.Equal( "loot.py", (string) b["source"]["name"] ) );

            Assert.True( _manager.ShouldBreak( path, 3, 1, null ) );
            Assert.True( _manager.ShouldBreak( path, 8, 1, null ) );
            Assert.False( _manager.ShouldBreak( path, 4, 1, null ) );
        }

        [Fact]
        public async Task WillReplaceAndThenClearAFilesBreakpoints()
        {
            string path = Path.Combine( Path.GetTempPath(), "ca-dap", "loot.py" );

            await _dap.RequestAsync( "setBreakpoints", new { source = new { path }, breakpoints = new[] { new { line = 3 } } } );
            await _dap.RequestAsync( "setBreakpoints", new { source = new { path }, breakpoints = new[] { new { line = 5 } } } );

            Assert.False( _manager.ShouldBreak( path, 3, 1, null ) );
            Assert.True( _manager.ShouldBreak( path, 5, 1, null ) );

            JObject cleared = await _dap.RequestAsync( "setBreakpoints", new { source = new { path } } );

            Assert.True( (bool) cleared["success"] );
            Assert.Empty( (JArray) cleared["body"]["breakpoints"] );
            Assert.False( _manager.ShouldBreak( path, 5, 1, null ) );
        }

        [Fact]
        public async Task WillRejectSetBreakpointsWithoutASourcePath()
        {
            JObject response = await _dap.RequestAsync( "setBreakpoints", new { source = new { name = "x.py" } } );

            Assert.False( (bool) response["success"] );
            Assert.Equal( "Missing source path", (string) response["message"] );
        }

        [Fact]
        public async Task WillListRunningMacroThreads()
        {
            JObject none = await _dap.RequestAsync( "threads" );
            Assert.Empty( (JArray) none["body"]["threads"] );

            Thread thread = new Thread( () => { } );
            DebugAdapterGlobals.RegisterThread( _manager, thread, "Mining" );

            JObject one = await _dap.RequestAsync( "threads" );
            JToken listed = Assert.Single( (JArray) one["body"]["threads"] );
            Assert.Equal( thread.ManagedThreadId, (int) listed["id"] );
            Assert.Equal( "Mining", (string) listed["name"] );
        }

        /// <summary>Attaching announces macros that were already running before the editor connected.</summary>
        [Fact]
        public async Task WillActivateAndAnnounceExistingThreadsOnAttach()
        {
            Thread thread = new Thread( () => { } );
            DebugAdapterGlobals.RegisterThread( _manager, thread, "Already running" );

            JObject response = await _dap.RequestAsync( "attach" );

            Assert.True( (bool) response["success"] );
            Assert.True( _manager.IsActive );

            JObject started = await _dap.WaitForEventAsync( "thread" );
            Assert.Equal( "started", (string) started["body"]["reason"] );
            Assert.Equal( thread.ManagedThreadId, (int) started["body"]["threadId"] );
        }

        [Fact]
        public async Task WillForwardDebugManagerEventsToTheClient()
        {
            await _dap.RequestAsync( "attach" );

            Thread thread = new Thread( () => { } );
            MacroEntry entry = DebugAdapterGlobals.RegisterThread( _manager, thread, "Late" );
            _manager.OnMacroStopped( entry );

            JObject started = await _dap.WaitForEventAsync( "thread", e => (string) e["body"]["reason"] == "started" );
            JObject exited = await _dap.WaitForEventAsync( "thread", e => (string) e["body"]["reason"] == "exited" );

            Assert.Equal( thread.ManagedThreadId, (int) started["body"]["threadId"] );
            Assert.True( (int) exited["seq"] > (int) started["seq"] );
        }

        [Fact]
        public async Task WillReturnAnEmptyStackForAThreadThatIsNotPaused()
        {
            Thread thread = new Thread( () => { } );
            DebugAdapterGlobals.RegisterThread( _manager, thread, "Running" );

            JObject running = await _dap.RequestAsync( "stackTrace", new { threadId = thread.ManagedThreadId } );
            JObject unknown = await _dap.RequestAsync( "stackTrace", new { threadId = 424242 } );

            foreach ( JObject response in new[] { running, unknown } )
            {
                Assert.True( (bool) response["success"] );
                Assert.Empty( (JArray) response["body"]["stackFrames"] );
                Assert.Equal( 0, (int) response["body"]["totalFrames"] );
            }
        }

        [Theory]
        [InlineData( "stackTrace" )]
        [InlineData( "scopes" )]
        [InlineData( "variables" )]
        [InlineData( "setVariable" )]
        [InlineData( "exceptionInfo" )]
        public async Task WillRejectARequestWithoutArguments( string command )
        {
            JObject response = await _dap.RequestAsync( command );

            Assert.False( (bool) response["success"] );
            Assert.Equal( "Missing arguments", (string) response["message"] );
        }

        [Fact]
        public async Task WillHandOutNoScopesOrVariablesForAnUnknownFrame()
        {
            JObject scopes = await _dap.RequestAsync( "scopes", new { frameId = 5000 } );

            JArray list = (JArray) scopes["body"]["scopes"];
            Assert.Equal( new[] { "Locals", "Globals" }, list.Select( s => (string) s["name"] ) );
            Assert.All( list, s => Assert.Equal( 0, (int) s["variablesReference"] ) );
            Assert.False( (bool) list[0]["expensive"] );
            Assert.True( (bool) list[1]["expensive"] );

            JObject variables = await _dap.RequestAsync( "variables", new { variablesReference = 10001 } );
            Assert.True( (bool) variables["success"] );
            Assert.Empty( (JArray) variables["body"]["variables"] );
        }

        [Fact]
        public async Task WillRejectAnEmptyExpression()
        {
            JObject missing = await _dap.RequestAsync( "evaluate" );
            JObject blank = await _dap.RequestAsync( "evaluate", new { expression = "   " } );

            Assert.False( (bool) missing["success"] );
            Assert.False( (bool) blank["success"] );
            Assert.Equal( "Missing expression", (string) blank["message"] );
        }

        /// <summary>
        ///     Evaluating needs a paused frame; without one the error is shown as the result, which is
        ///     how it reaches the debug console.
        /// </summary>
        [Fact]
        public async Task WillShowTheErrorAsTheResultWhenNothingIsPaused()
        {
            JObject response = await _dap.RequestAsync( "evaluate", new { expression = "1 + 1", frameId = 3000 } );

            Assert.True( (bool) response["success"] );
            Assert.Equal( "No frame available for evaluation", (string) response["body"]["result"] );
            Assert.Equal( 0, (int) response["body"]["variablesReference"] );
        }

        [Fact]
        public async Task WillRouteBangCommandsToTheGameStateWithoutAPausedMacro()
        {
            Engine.Items.Add( new Item( 0x40000001 ) { Name = "Dagger" } );

            JObject response = await _dap.RequestAsync( "evaluate", new { expression = "!props 0x40000001" } );

            Assert.True( (bool) response["success"] );
            Assert.Contains( "Name: Dagger", (string) response["body"]["result"] );
            Assert.Equal( 0, (int) response["body"]["variablesReference"] );
        }

        [Fact]
        public async Task WillSetExceptionBreakpointsAndReportTheBreakMode()
        {
            await _dap.RequestAsync( "setExceptionBreakpoints", new { filters = new[] { "all" } } );
            Assert.True( _manager.BreakOnAllExceptions );

            JObject always = await _dap.RequestAsync( "exceptionInfo", new { threadId = 1 } );
            Assert.Equal( "always", (string) always["body"]["breakMode"] );
            Assert.Equal( "PythonException", (string) always["body"]["exceptionId"] );
            Assert.Equal( "Unknown", (string) always["body"]["description"] );

            await _dap.RequestAsync( "setExceptionBreakpoints", new { filters = new string[0] } );
            Assert.False( _manager.BreakOnAllExceptions );

            JObject unhandled = await _dap.RequestAsync( "exceptionInfo", new { threadId = 1 } );
            Assert.Equal( "unhandled", (string) unhandled["body"]["breakMode"] );
        }

        [Fact]
        public async Task WillTreatMissingExceptionFiltersAsNone()
        {
            _manager.BreakOnAllExceptions = true;

            JObject response = await _dap.RequestAsync( "setExceptionBreakpoints" );

            Assert.True( (bool) response["success"] );
            Assert.False( _manager.BreakOnAllExceptions );
        }

        [Fact]
        public async Task WillResumeAPausedThreadOnContinue()
        {
            Thread thread = new Thread( () => { } );
            DebugAdapterGlobals.RegisterThread( _manager, thread, "Paused" );
            MacroDebugState state = _manager.GetThreadState( thread.ManagedThreadId );
            state.Pause();

            JObject response = await _dap.RequestAsync( "continue", new { threadId = thread.ManagedThreadId } );

            Assert.True( (bool) response["success"] );
            Assert.False( (bool) response["body"]["allThreadsContinued"] );
            Assert.False( state.IsPaused );

            JObject continued = await _dap.WaitForEventAsync( "continued" );
            Assert.Equal( thread.ManagedThreadId, (int) continued["body"]["threadId"] );
        }

        [Theory]
        [InlineData( "next", StepMode.StepOver )]
        [InlineData( "stepIn", StepMode.StepInto )]
        [InlineData( "stepOut", StepMode.StepOut )]
        public async Task WillResumeAPausedThreadInTheRequestedStepMode( string command, StepMode expected )
        {
            Thread thread = new Thread( () => { } );
            DebugAdapterGlobals.RegisterThread( _manager, thread, "Paused" );
            MacroDebugState state = _manager.GetThreadState( thread.ManagedThreadId );
            state.Pause();

            JObject response = await _dap.RequestAsync( command, new { threadId = thread.ManagedThreadId } );

            Assert.True( (bool) response["success"] );
            Assert.Equal( expected, state.StepMode );
            Assert.False( state.IsPaused );
        }

        [Fact]
        public async Task WillAskARunningThreadToStopOnPause()
        {
            Thread thread = new Thread( () => { } );
            DebugAdapterGlobals.RegisterThread( _manager, thread, "Running" );

            JObject response = await _dap.RequestAsync( "pause", new { threadId = thread.ManagedThreadId } );

            Assert.True( (bool) response["success"] );
            Assert.Equal( StepMode.StepInto, _manager.GetThreadState( thread.ManagedThreadId ).StepMode );
        }

        [Theory]
        [InlineData( "continue" )]
        [InlineData( "next" )]
        [InlineData( "stepIn" )]
        [InlineData( "stepOut" )]
        [InlineData( "pause" )]
        public async Task WillAcknowledgeExecutionControlForAnUnknownThread( string command )
        {
            JObject withThread = await _dap.RequestAsync( command, new { threadId = 999999 } );
            JObject withoutArguments = await _dap.RequestAsync( command );

            Assert.True( (bool) withThread["success"] );
            Assert.True( (bool) withoutArguments["success"] );
        }

        [Fact]
        public async Task WillRejectALaunchWithoutAProgram()
        {
            JObject response = await _dap.RequestAsync( "launch", new { args = "1,2" } );

            Assert.False( (bool) response["success"] );
            Assert.Equal( "Missing 'program' in launch configuration", (string) response["message"] );
        }

        [Fact]
        public async Task WillRunNothingWhenTheLaunchedProgramIsNotAMacro()
        {
            JObject launch = await _dap.RequestAsync( "launch",
                new { program = Path.Combine( Path.GetTempPath(), "not-a-macro.py" ) } );
            JObject done = await _dap.RequestAsync( "configurationDone" );

            Assert.True( (bool) launch["success"] );
            Assert.True( (bool) done["success"] );
            Assert.True( _manager.IsActive );
            Assert.Null( MacroManager.GetInstance().CurrentMacro );
        }

        [Fact]
        public async Task WillAnswerCompletionsWithoutArgumentsWithNoTargets()
        {
            JObject response = await _dap.RequestAsync( "completions" );

            Assert.True( (bool) response["success"] );
            Assert.Empty( (JArray) response["body"]["targets"] );
        }

        [Fact]
        public async Task WillFailToSetAVariableInAnUnknownContainer()
        {
            JObject response = await _dap.RequestAsync( "setVariable",
                new { variablesReference = 31337, name = "x", value = "1" } );

            Assert.False( (bool) response["success"] );
            Assert.Equal( "Variable container not found", (string) response["message"] );
        }

        [Fact]
        public async Task WillSendTerminatedOnTerminate()
        {
            JObject response = await _dap.RequestAsync( "terminate" );

            Assert.True( (bool) response["success"] );
            await _dap.WaitForEventAsync( "terminated" );
        }

        /// <summary>
        ///     Disconnecting must not leave a macro frozen at a breakpoint or breakpoints armed for the
        ///     next session, and the connection is closed after the response goes out.
        /// </summary>
        [Fact]
        public async Task WillReleaseEverythingAndCloseOnDisconnect()
        {
            string path = Path.Combine( Path.GetTempPath(), "ca-dap", "loot.py" );
            await _dap.RequestAsync( "attach" );
            await _dap.RequestAsync( "setBreakpoints", new { source = new { path }, breakpoints = new[] { new { line = 3 } } } );

            Thread thread = new Thread( () => { } );
            DebugAdapterGlobals.RegisterThread( _manager, thread, "Paused" );
            MacroDebugState state = _manager.GetThreadState( thread.ManagedThreadId );
            state.Pause();

            JObject response = await _dap.RequestAsync( "disconnect", new { terminateDebuggee = false } );

            Assert.True( (bool) response["success"] );
            Assert.False( _manager.IsActive );
            Assert.False( state.IsPaused );
            Assert.False( _manager.ShouldBreak( path, 3, 1, null ) );

            Assert.True( await _dap.WaitForCloseAsync(), "the session did not close the connection" );

            // The read loop ends with the IOException from its own closed stream, which RunAsync lets
            // escape once the session is closed (DapServer's accept loop absorbs it); it only has to end.
            await Record.ExceptionAsync( () =>
                _dap.RunTask.WaitAsync( DapHarness.Timeout, TestContext.Current.CancellationToken ) );
            Assert.True( _dap.RunTask.IsCompleted );
        }

        /// <summary>
        ///     When the editor goes away without a disconnect, the session ends and does the same
        ///     clean-up, so a paused macro carries on.
        /// </summary>
        [Fact]
        public async Task WillReleasePausedMacrosWhenTheClientDropsTheConnection()
        {
            await _dap.RequestAsync( "attach" );

            Thread thread = new Thread( () => { } );
            DebugAdapterGlobals.RegisterThread( _manager, thread, "Paused" );
            MacroDebugState state = _manager.GetThreadState( thread.ManagedThreadId );
            state.Pause();

            _dap.DropConnection();

            await _dap.RunTask.WaitAsync( DapHarness.Timeout, TestContext.Current.CancellationToken );

            Assert.False( _manager.IsActive );
            Assert.False( state.IsPaused );
        }
    }
}
