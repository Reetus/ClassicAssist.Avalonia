using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Data.Macros;
using ClassicAssist.DebugAdapter;
using ClassicAssist.DebugAdapter.Dap;
using IronPython.Runtime.Exceptions;
using Xunit;

namespace ClassicAssist.Tests.DebugAdapter
{
    /// <summary>
    ///     <see cref="DebugManager" /> decides, on every traced line of a macro, whether to stop: line
    ///     breakpoints (keyed by a normalised path, so the editor's path and IronPython's co_filename
    ///     agree), conditions and log points evaluated in the frame, and the step-over/into/out depth
    ///     rules. These drive it with real IronPython frames on the test thread, issuing the next debugger
    ///     command at each stop exactly as a client would, and record where it stopped.
    /// </summary>
    public class DebugManagerTests : IDisposable
    {
        private const string FUNCTION_SOURCE =
            "def add(a, b):\n" + // 1
            "    c = a + b\n" + // 2
            "    return c\n" + // 3
            "x = 1\n" + // 4
            "y = add(x, 2)\n" + // 5
            "z = y * 2\n"; // 6

        private const string LOOP_SOURCE =
            "total = 0\n" + // 1
            "for i in range(5):\n" + // 2
            "    total = total + i\n" + // 3
            "done = True\n"; // 4

        private readonly DebugAdapterGlobals _globals;
        private readonly DebugManager _manager = new DebugManager();
        private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;

        public DebugManagerTests()
        {
            EngineState.Reset();
            _globals = new DebugAdapterGlobals();
        }

        public void Dispose()
        {
            _globals.Dispose();
        }

        /// <summary>
        ///     Runs the source as MacroInvoker.OnTrace would: at each stop, records the line and hands
        ///     control to the next scripted command (continue when the script runs out).
        /// </summary>
        private List<int> Drive( string source, params Action<int>[] commands )
        {
            DebugAdapterGlobals.RegisterThread( _manager, Thread.CurrentThread, "test" );

            List<int> stops = new List<int>();
            Queue<Action<int>> pending = new Queue<Action<int>>( commands );

            PythonFrames.Run( source, ( frame, result ) =>
            {
                if ( result != "line" )
                {
                    return;
                }

                int line = (int) frame.f_lineno;

                if ( !_manager.ShouldBreak( frame.f_code.co_filename, line, _threadId, frame ) )
                {
                    return;
                }

                _manager.OnBreakpoint( _threadId, frame );
                stops.Add( line );

                Assert.True( stops.Count < 50, "runaway stepping" );

                Action<int> command = pending.Count > 0 ? pending.Dequeue() : _manager.Continue;
                command( _threadId );
            } );

            return stops;
        }

        private void SetBreakpoint( int line, string condition = null, string logMessage = null )
        {
            _manager.SetBreakpoints( PythonFrames.ScriptPath,
                new[] { new BreakpointInfo { Line = line, Condition = condition, LogMessage = logMessage } } );
        }

        [Fact]
        public void WillStopOnEveryHitOfABreakpointInALoop()
        {
            SetBreakpoint( 3 );

            Assert.Equal( new List<int> { 3, 3, 3, 3, 3 }, Drive( LOOP_SOURCE ) );
        }

        [Fact]
        public void WillStepOverACallWithoutStoppingInsideIt()
        {
            SetBreakpoint( 5 );

            Assert.Equal( new List<int> { 5, 6 }, Drive( FUNCTION_SOURCE, _manager.StepOver ) );
        }

        [Fact]
        public void WillStepIntoACalledFunction()
        {
            SetBreakpoint( 5 );

            List<int> stops = Drive( FUNCTION_SOURCE, _manager.StepInto, _manager.StepInto );

            Assert.Equal( new List<int> { 5, 2, 3 }, stops );
        }

        [Fact]
        public void WillStepOutOfAFunctionToItsCaller()
        {
            SetBreakpoint( 2 );

            Assert.Equal( new List<int> { 2, 6 }, Drive( FUNCTION_SOURCE, _manager.StepOut ) );
        }

        [Fact]
        public void WillStepOverWithinAFunctionWithoutLeavingIt()
        {
            SetBreakpoint( 2 );

            Assert.Equal( new List<int> { 2, 3 }, Drive( FUNCTION_SOURCE, _manager.StepOver ) );
        }

        [Fact]
        public void WillStopOnlyWhenTheConditionHolds()
        {
            SetBreakpoint( 3, "i == 3" );
            List<object> seen = new List<object>();

            DebugAdapterGlobals.RegisterThread( _manager, Thread.CurrentThread, "test" );

            PythonFrames.Run( LOOP_SOURCE, ( frame, result ) =>
            {
                if ( result == "line" && _manager.ShouldBreak( frame.f_code.co_filename, (int) frame.f_lineno,
                        _threadId, frame ) )
                {
                    seen.Add( ( (IronPython.Runtime.PythonDictionary) frame.f_locals )["i"] );
                }
            } );

            Assert.Equal( new List<object> { 3 }, seen );
        }

        /// <summary>
        ///     Conditions see module globals (imported commands, args, module state), not just the
        ///     function's own locals.
        /// </summary>
        [Fact]
        public void WillEvaluateConditionsAgainstGlobalsFromInsideAFunction()
        {
            const string source =
                "limit = 2\n" + // 1
                "def f(n):\n" + // 2
                "    return n\n" + // 3
                "for k in range(4):\n" + // 4
                "    f(k)\n"; // 5

            SetBreakpoint( 3, "n == limit" );
            List<object> seen = new List<object>();

            DebugAdapterGlobals.RegisterThread( _manager, Thread.CurrentThread, "test" );

            PythonFrames.Run( source, ( frame, result ) =>
            {
                if ( result == "line" && _manager.ShouldBreak( frame.f_code.co_filename, (int) frame.f_lineno,
                        _threadId, frame ) )
                {
                    seen.Add( ( (IronPython.Runtime.PythonDictionary) frame.f_locals )["n"] );
                }
            } );

            Assert.Equal( new List<object> { 2 }, seen );
        }

        [Fact]
        public void WontStopOrThrowWhenTheConditionIsInvalid()
        {
            SetBreakpoint( 3, "i ==" );

            Assert.Empty( Drive( LOOP_SOURCE ) );
        }

        [Fact]
        public void WillLogInsteadOfStoppingOnALogPoint()
        {
            SetBreakpoint( 3, logMessage: "i={i} total={total} bad={nope}" );
            List<DapEvent> events = new List<DapEvent>();
            Action<DapEvent> handler = events.Add;
            _manager.SendEvent += handler;

            List<int> stops;

            try
            {
                stops = Drive( LOOP_SOURCE );
            }
            finally
            {
                _manager.SendEvent -= handler;
            }

            Assert.Empty( stops );

            List<string> output = events.FindAll( e => e.Event == "output" )
                .ConvertAll( e => ( (OutputEventBody) e.Body ).Output );

            Assert.Equal(
                new List<string>
                {
                    "i=0 total=0 bad={nope}\n",
                    "i=1 total=0 bad={nope}\n",
                    "i=2 total=1 bad={nope}\n",
                    "i=3 total=3 bad={nope}\n",
                    "i=4 total=6 bad={nope}\n"
                }, output );
        }

        [Fact]
        public void WillLogAPlainMessageVerbatim()
        {
            SetBreakpoint( 4, logMessage: "reached the end" );
            List<DapEvent> events = new List<DapEvent>();
            Action<DapEvent> handler = events.Add;
            _manager.SendEvent += handler;

            try
            {
                Drive( LOOP_SOURCE );
            }
            finally
            {
                _manager.SendEvent -= handler;
            }

            DapEvent output = Assert.Single( events, e => e.Event == "output" );
            Assert.Equal( "reached the end\n", ( (OutputEventBody) output.Body ).Output );
            Assert.Equal( "console", ( (OutputEventBody) output.Body ).Category );
        }

        [Fact]
        public void WillReplaceAFilesBreakpointsRatherThanAddToThem()
        {
            _manager.SetBreakpoints( "C:/macros/a.py", new[] { new BreakpointInfo { Line = 3 } } );
            _manager.SetBreakpoints( "C:/macros/a.py", new[] { new BreakpointInfo { Line = 7 } } );

            Assert.False( _manager.ShouldBreak( "C:/macros/a.py", 3, _threadId, null ) );
            Assert.True( _manager.ShouldBreak( "C:/macros/a.py", 7, _threadId, null ) );
        }

        [Fact]
        public void WillClearAFilesBreakpointsWithAnEmptyList()
        {
            _manager.SetBreakpoints( "C:/macros/a.py", new[] { new BreakpointInfo { Line = 3 } } );
            _manager.SetBreakpoints( "C:/macros/b.py", new[] { new BreakpointInfo { Line = 3 } } );

            _manager.SetBreakpoints( "C:/macros/a.py", new BreakpointInfo[0] );

            Assert.False( _manager.ShouldBreak( "C:/macros/a.py", 3, _threadId, null ) );
            Assert.True( _manager.ShouldBreak( "C:/macros/b.py", 3, _threadId, null ) );
        }

        /// <summary>
        ///     The editor sends a resolved path; IronPython reports the macro's FilePath, which can carry
        ///     "./" segments and the other separator. Both must find the same breakpoints.
        /// </summary>
        [Fact]
        public void WillMatchBreakpointsAcrossPathSpellings()
        {
            string dir = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "ca-dbg" );
            _manager.SetBreakpoints( System.IO.Path.Combine( dir, "Macros", "foo.py" ),
                new[] { new BreakpointInfo { Line = 1 } } );

            Assert.True( _manager.ShouldBreak( dir + "/./Macros/sub/../foo.py", 1, _threadId, null ) );
            Assert.True( _manager.ShouldBreak( dir.Replace( '\\', '/' ) + "/Macros/foo.py", 1, _threadId, null ) );
            Assert.False( _manager.ShouldBreak( dir + "/Macros/bar.py", 1, _threadId, null ) );
        }

        [Fact]
        public void WontBreakWithoutAFileName()
        {
            _manager.SetBreakpoints( "C:/macros/a.py", new[] { new BreakpointInfo { Line = 1 } } );
            DebugAdapterGlobals.RegisterThread( _manager, Thread.CurrentThread, "test" );
            _manager.StepInto( _threadId );

            Assert.False( _manager.ShouldBreak( null, 1, _threadId, null ) );
            Assert.False( _manager.ShouldBreak( "", 1, _threadId, null ) );
        }

        [Fact]
        public void WillClearEveryBreakpoint()
        {
            _manager.SetBreakpoints( "C:/macros/a.py", new[] { new BreakpointInfo { Line = 1 } } );
            _manager.SetBreakpoints( "C:/macros/b.py", new[] { new BreakpointInfo { Line = 2 } } );

            _manager.ClearAllBreakpoints();

            Assert.False( _manager.ShouldBreak( "C:/macros/a.py", 1, _threadId, null ) );
            Assert.False( _manager.ShouldBreak( "C:/macros/b.py", 2, _threadId, null ) );
        }

        [Fact]
        public void WillNormalisePathsToForwardSlashesWithoutTrailingSeparator()
        {
            string full = System.IO.Path.GetFullPath( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "a", "b" ) );
            string expected = full.Replace( '\\', '/' ).TrimEnd( '/' );

            Assert.Equal( expected, DebugManager.NormalizePath( full + "/./c/../" ) );
            Assert.Null( DebugManager.NormalizePath( null ) );
            Assert.Equal( "", DebugManager.NormalizePath( "" ) );
        }

        [Fact]
        public void WillPauseARunningThreadOnItsNextLine()
        {
            bool paused = false;
            List<int> stops = new List<int>();

            DebugAdapterGlobals.RegisterThread( _manager, Thread.CurrentThread, "test" );

            PythonFrames.Run( LOOP_SOURCE, ( frame, result ) =>
            {
                if ( result != "line" )
                {
                    return;
                }

                int line = (int) frame.f_lineno;

                if ( _manager.ShouldBreak( frame.f_code.co_filename, line, _threadId, frame ) )
                {
                    _manager.OnBreakpoint( _threadId, frame, "pause" );
                    stops.Add( line );
                    _manager.Continue( _threadId );
                }

                // The client's pause request lands while line 3 runs for the second time.
                if ( !paused && line == 3 && Equals( ( (IronPython.Runtime.PythonDictionary) frame.f_locals )["i"], 1 ) )
                {
                    paused = true;
                    _manager.ForcePause( _threadId );
                }
            } );

            Assert.Equal( new List<int> { 2 }, stops );
        }

        [Fact]
        public void WontChangeStepModeWhenPausingAnAlreadyPausedThread()
        {
            DebugAdapterGlobals.RegisterThread( _manager, Thread.CurrentThread, "test" );
            MacroDebugState state = _manager.GetThreadState( _threadId );
            state.Pause();

            _manager.ForcePause( _threadId );

            Assert.Equal( StepMode.None, state.StepMode );
        }

        [Fact]
        public void WillAnnounceThreadsOnlyWhileAClientIsActive()
        {
            List<DapEvent> events = new List<DapEvent>();
            Action<DapEvent> handler = events.Add;
            _manager.SendEvent += handler;

            try
            {
                Thread a = new Thread( () => { } );
                Thread b = new Thread( () => { } );

                DebugAdapterGlobals.RegisterThread( _manager, a, "quiet" );
                _manager.IsActive = true;
                MacroEntry announced = DebugAdapterGlobals.RegisterThread( _manager, b, "loud" );

                DapEvent started = Assert.Single( events );
                Assert.Equal( "thread", started.Event );
                Assert.Equal( "started", ( (ThreadEventBody) started.Body ).Reason );
                Assert.Equal( b.ManagedThreadId, ( (ThreadEventBody) started.Body ).ThreadId );

                Assert.Equal( 2, _manager.GetThreads().Length );
                Assert.Contains( _manager.GetThreads(), t => t.Item1 == a.ManagedThreadId && t.Item2 == "quiet" );

                _manager.OnMacroStopped( announced );

                Assert.Equal( "exited", ( (ThreadEventBody) events[1].Body ).Reason );
                Assert.Null( _manager.GetThreadState( b.ManagedThreadId ) );
                Assert.Single( _manager.GetThreads() );
            }
            finally
            {
                _manager.SendEvent -= handler;
            }
        }

        [Fact]
        public void WillIgnoreAMacroThatHasNoThread()
        {
            _manager.OnMacroStarted( new MacroEntry { Name = "never-ran" } );
            _manager.OnMacroStopped( new MacroEntry { Name = "never-ran-2" } );

            Assert.Empty( _manager.GetThreads() );
        }

        /// <summary>
        ///     A macro stopped while sitting at a breakpoint must not leave its thread blocked on the
        ///     debugger.
        /// </summary>
        [Fact]
        public async Task WillReleaseAPausedThreadWhenItsMacroStops()
        {
            Thread macroThread = new Thread( () => { } );
            MacroEntry entry = DebugAdapterGlobals.RegisterThread( _manager, macroThread, "paused" );
            MacroDebugState state = _manager.GetThreadState( macroThread.ManagedThreadId );
            state.Pause();

            using ManualResetEventSlim waiting = new ManualResetEventSlim();

            Task waiter = Task.Run( () =>
            {
                waiting.Set();
                state.Wait( TestContext.Current.CancellationToken );
            }, TestContext.Current.CancellationToken );

            Assert.True( waiting.Wait( DapHarness.Timeout, TestContext.Current.CancellationToken ) );

            _manager.OnMacroStopped( entry );

            // OnMacroStopped resumes and then immediately disposes the state's event, so the released
            // waiter can either return or throw ObjectDisposedException depending on which thread runs
            // first. In a real macro the stop has already cancelled the wait's token, so it throws
            // either way; what matters is that it does not stay blocked.
            Exception released = await Record.ExceptionAsync( () =>
                waiter.WaitAsync( DapHarness.Timeout, TestContext.Current.CancellationToken ) );

            Assert.True( waiter.IsCompleted, "the paused thread was not released" );
            Assert.True( released == null || released is ObjectDisposedException, $"{released}" );
        }

        [Fact]
        public void WillResumeOnlyPausedThreadsAndClearTheirStepMode()
        {
            Thread a = new Thread( () => { } );
            Thread b = new Thread( () => { } );
            DebugAdapterGlobals.RegisterThread( _manager, a, "a" );
            DebugAdapterGlobals.RegisterThread( _manager, b, "b" );

            MacroDebugState paused = _manager.GetThreadState( a.ManagedThreadId );
            MacroDebugState running = _manager.GetThreadState( b.ManagedThreadId );
            paused.StepMode = StepMode.StepOver;
            paused.Pause();
            running.StepMode = StepMode.StepInto;

            _manager.ResumeAll();

            Assert.False( paused.IsPaused );
            Assert.Equal( StepMode.None, paused.StepMode );
            Assert.Equal( StepMode.StepInto, running.StepMode );
        }

        [Fact]
        public void WillAbandonTheWaitWhenTheMacroIsCancelled()
        {
            DebugAdapterGlobals.RegisterThread( _manager, Thread.CurrentThread, "test" );
            _manager.GetThreadState( _threadId ).Pause();

            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>( () => _manager.WaitForResume( _threadId, cts.Token ) );
        }

        [Fact]
        public void WontWaitForAThreadItDoesNotKnow()
        {
            _manager.WaitForResume( 987654, TestContext.Current.CancellationToken );
            _manager.Continue( 987654 );
            _manager.StepOver( 987654 );
            _manager.StepInto( 987654 );
            _manager.StepOut( 987654 );
            _manager.ForcePause( 987654 );
            _manager.OnBreakpoint( 987654, null );

            Assert.Null( _manager.GetThreadState( 987654 ) );
        }

        [Fact]
        public void WillBreakOnExceptionsOnlyForTheAllFilter()
        {
            _manager.SetExceptionBreakpoints( new[] { "all" } );
            Assert.True( _manager.ShouldBreakOnException( _threadId, null ) );

            _manager.SetExceptionBreakpoints( new[] { "uncaught" } );
            Assert.False( _manager.ShouldBreakOnException( _threadId, null ) );
            Assert.True( _manager.BreakOnUncaughtExceptions );

            _manager.SetExceptionBreakpoints( new string[0] );
            Assert.False( _manager.ShouldBreakOnException( _threadId, null ) );
            Assert.False( _manager.BreakOnUncaughtExceptions );
        }

        [Fact]
        public void WillRecordWhereAndWhyAThreadStopped()
        {
            DebugAdapterGlobals.RegisterThread( _manager, Thread.CurrentThread, "test" );
            List<DapEvent> events = new List<DapEvent>();
            Action<DapEvent> handler = events.Add;
            _manager.SendEvent += handler;

            try
            {
                PythonFrames.RunToLine( FUNCTION_SOURCE, 2, frame =>
                {
                    _manager.OnBreakpoint( _threadId, frame, "step" );

                    MacroDebugState state = _manager.GetThreadState( _threadId );
                    Assert.True( state.IsPaused );
                    Assert.Equal( 2, state.StoppedLine );
                    Assert.Equal( "step", state.StoppedReason );
                    Assert.Equal( PythonFrames.ScriptPath, state.StoppedFile );
                    Assert.Same( frame, state.CurrentFrame );

                    _manager.Continue( _threadId );

                    Assert.False( state.IsPaused );
                    Assert.Null( state.CurrentFrame );
                } );
            }
            finally
            {
                _manager.SendEvent -= handler;
            }

            Assert.Equal( new List<string> { "stopped", "continued" }, events.ConvertAll( e => e.Event ) );
            Assert.Equal( "step", ( (StoppedEventBody) events[0].Body ).Reason );
            Assert.Equal( _threadId, ( (ContinuedEventBody) events[1].Body ).ThreadId );
        }
    }
}
