using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UO.Network;
using Microsoft.Scripting;
using Xunit;

namespace ClassicAssist.Tests.Macros
{
    /// <summary>
    ///     The life of a single macro run on <see cref="MacroInvoker" />'s own thread: the events it raises
    ///     and their order, how errors are reported, and - the part that has broken before - how a stop
    ///     reaches a macro that is busy, sleeping or blocked in a wait. A macro that cannot be stopped
    ///     keeps playing the game on its own, and a stop that leaves state behind (an event handler, a
    ///     stale IsRunning) breaks the next macro instead.
    /// </summary>
    public class MacroInvokerLifecycleTests : IDisposable
    {
        private readonly MacroHarness _harness = new MacroHarness();

        public void Dispose()
        {
            _harness.Dispose();
        }

        [Fact]
        public void WillRaiseStartedThenStoppedOnTheMacroThread()
        {
            MacroEntry entry = _harness.Create( "x = 1" );
            List<string> events = new List<string>();
            List<int> threads = new List<int>();

            void OnStarted()
            {
                lock ( events )
                {
                    events.Add( "started" );
                    threads.Add( Environment.CurrentManagedThreadId );
                }
            }

            void OnStopped()
            {
                lock ( events )
                {
                    events.Add( "stopped" );
                    threads.Add( Environment.CurrentManagedThreadId );
                }
            }

            entry.MacroInvoker.StartedEvent += OnStarted;
            entry.MacroInvoker.StoppedEvent += OnStopped;

            entry.Execute();

            Assert.True( MacroHarness.Join( entry ) );

            Assert.Equal( new[] { "started", "stopped" }, events );
            Assert.All( threads, t => Assert.NotEqual( Environment.CurrentManagedThreadId, t ) );
            Assert.False( entry.MacroInvoker.IsFaulted );
            Assert.Null( entry.MacroInvoker.Exception );
            Assert.False( entry.IsRunning );
        }

        [Fact]
        public void WillPassParametersAsArgs()
        {
            MacroInvoker invoker = _harness.RunToEnd( "SetAlias('first', args[0])\nSetAlias('count', len(args))",
                new object[] { 0x1234, "two" } );

            Assert.False( invoker.IsFaulted, invoker.Exception?.ToString() );
            Assert.Equal( 0x1234, MacroHarness.Alias( "first" ) );
            Assert.Equal( 2, MacroHarness.Alias( "count" ) );
        }

        [Fact]
        public void WillRunCommandsFromTheCommandNamespaces()
        {
            // DummyCommands is excluded from the help, not from the import list.
            MacroInvoker invoker = _harness.RunToEnd( "SetAlias('sum', Dummy(5, 7))" );

            Assert.False( invoker.IsFaulted, invoker.Exception?.ToString() );
            Assert.Equal( 12, MacroHarness.Alias( "sum" ) );
        }

        [Fact]
        public void WillReportRuntimeErrorBeforeStoppedWithItsLine()
        {
            MacroEntry entry = _harness.Create( "x = 1\nraise Exception('boom')\nSetAlias('after', 1)" );
            List<string> events = new List<string>();
            Exception reported = null;

            void OnException( Exception e )
            {
                lock ( events )
                {
                    events.Add( "exception" );
                    reported = e;
                }
            }

            void OnStopped()
            {
                lock ( events )
                {
                    events.Add( "stopped" );
                }
            }

            entry.MacroInvoker.ExceptionEvent += OnException;
            entry.MacroInvoker.StoppedEvent += OnStopped;

            entry.Execute();

            Assert.True( MacroHarness.Join( entry ) );

            Assert.Equal( new[] { "exception", "stopped" }, events );
            Assert.NotNull( reported );
            Assert.Equal( "boom", reported.Message );
            Assert.Same( reported, entry.MacroInvoker.Exception );
            Assert.True( entry.MacroInvoker.IsFaulted );
            Assert.Equal( -1, MacroHarness.Alias( "after" ) );

            // MacroEntry reports the error and where it happened to the player.
            Assert.True( JournalCommands.InJournal( string.Format( Strings.Macro_error___0_, "boom" ), "system" ) );
            Assert.True( JournalCommands.InJournal( $"{Strings.Line_Number}: 2", "system" ) );
        }

        [Fact]
        public void WillReportSyntaxErrorWithItsLine()
        {
            MacroEntry entry = _harness.Create( "x = 1\ny = 2\nif x ==\n    pass" );
            Exception reported = null;

            void OnException( Exception e )
            {
                reported = e;
            }

            entry.MacroInvoker.ExceptionEvent += OnException;

            entry.Execute();

            Assert.True( MacroHarness.Join( entry ) );

            Assert.IsType<SyntaxErrorException>( reported, false );
            Assert.True( entry.MacroInvoker.IsFaulted );
            Assert.True( JournalCommands.InJournal( $"{Strings.Line_Number}: 3", "system" ) );
        }

        /// <summary>
        ///     A fault must not stick to the invoker: the next run of the same entry starts clean.
        /// </summary>
        [Fact]
        public void WillClearFaultOnTheNextRun()
        {
            MacroEntry entry = _harness.Create( "raise Exception('first')" );

            entry.Execute();
            Assert.True( MacroHarness.Join( entry ) );
            Assert.True( entry.MacroInvoker.IsFaulted );

            entry.Macro = "SetAlias('second', 1)";
            entry.Execute();
            Assert.True( MacroHarness.Join( entry ) );

            Assert.False( entry.MacroInvoker.IsFaulted );
            Assert.Equal( 1, MacroHarness.Alias( "second" ) );
        }

        /// <summary>
        ///     The invoker caches the compiled script by content hash. An edit must be picked up, not
        ///     replayed from the cache.
        /// </summary>
        [Fact]
        public void WillRecompileWhenTheMacroTextChanges()
        {
            MacroEntry entry = _harness.Create( "SetAlias('v', 1)" );

            entry.Execute();
            Assert.True( MacroHarness.Join( entry ) );
            Assert.Equal( 1, MacroHarness.Alias( "v" ) );

            entry.Macro = "SetAlias('v', 2)";
            entry.Execute();
            Assert.True( MacroHarness.Join( entry ) );

            Assert.Equal( 2, MacroHarness.Alias( "v" ) );
        }

        /// <summary>
        ///     Each run gets a fresh scope; a variable from the last run must not leak into the next.
        /// </summary>
        [Fact]
        public void WontKeepVariablesBetweenRuns()
        {
            MacroEntry entry = _harness.Create(
                "try:\n    leftover\n    SetAlias('leaked', 1)\nexcept NameError:\n    SetAlias('leaked', 0)\nleftover = 1" );

            entry.Execute();
            Assert.True( MacroHarness.Join( entry ) );
            Assert.Equal( 0, MacroHarness.Alias( "leaked" ) );

            entry.Execute();
            Assert.True( MacroHarness.Join( entry ) );

            Assert.Equal( 0, MacroHarness.Alias( "leaked" ) );
        }

        [Fact]
        public void WillStopAMacroSleepingInPause()
        {
            MacroEntry entry = _harness.Create( "SetAlias('ready', 1)\nPause(60000)\nSetAlias('after', 1)" );
            bool exceptionRaised = false;

            void OnException( Exception e )
            {
                exceptionRaised = true;
            }

            entry.MacroInvoker.ExceptionEvent += OnException;

            entry.Execute();

            Assert.True( MacroHarness.WaitForAlias( "ready", 1 ) );
            Assert.True( entry.IsRunning );

            Stopwatch stopwatch = Stopwatch.StartNew();
            entry.Stop();

            Assert.True( MacroHarness.Join( entry, 5000 ) );
            Assert.True( stopwatch.ElapsedMilliseconds < 5000, $"stop took {stopwatch.ElapsedMilliseconds}ms" );
            Assert.False( entry.IsRunning );
            Assert.True( entry.MacroInvoker.IsFaulted );
            Assert.Equal( -1, MacroHarness.Alias( "after" ) );

            // Being stopped is not an error to report to the player.
            Assert.False( exceptionRaised );
            Assert.Null( entry.MacroInvoker.Exception );
        }

        [Fact]
        public void WillStopABusyPythonLoop()
        {
            // No command call to interrupt: only the trace hook's cancellation check can end this.
            MacroEntry entry = _harness.Create( "SetAlias('ready', 1)\nwhile True:\n    x = 1" );

            entry.Execute();

            Assert.True( MacroHarness.WaitForAlias( "ready", 1 ) );

            entry.Stop();

            Assert.True( MacroHarness.Join( entry, 5000 ) );
            Assert.False( entry.IsRunning );
            Assert.Null( entry.MacroInvoker.Exception );
        }

        /// <summary>
        ///     WaitForJournal parks on a wait handle with a handler on a static event. Interrupting it must
        ///     end the macro and take the handler with it, or every later journal line runs the dead
        ///     macro's matcher.
        /// </summary>
        [Fact]
        public void WillStopAMacroBlockedInWaitForJournalAndDetachItsHandler()
        {
            FieldInfo field = typeof( IncomingPacketHandlers ).GetField( "JournalEntryAddedEvent",
                BindingFlags.Static | BindingFlags.NonPublic );

            Assert.NotNull( field );

            int JournalHandlers()
            {
                return ( (Delegate) field.GetValue( null ) )?.GetInvocationList()
                    .Count( d => d.Method.DeclaringType?.DeclaringType == typeof( JournalCommands ) ||
                                 d.Method.DeclaringType == typeof( JournalCommands ) ) ?? 0;
            }

            int before = JournalHandlers();

            MacroEntry entry = _harness.Create(
                "SetAlias('ready', 1)\nWaitForJournal('never going to happen', 60000)\nSetAlias('after', 1)" );

            entry.Execute();

            Assert.True( MacroHarness.WaitForAlias( "ready", 1 ) );
            Assert.True( SpinWait.SpinUntil( () => JournalHandlers() == before + 1, MacroHarness.TIMEOUT_MS ) );

            entry.Stop();

            Assert.True( MacroHarness.Join( entry, 5000 ) );
            Assert.Equal( -1, MacroHarness.Alias( "after" ) );
            Assert.Equal( before, JournalHandlers() );
        }

        /// <summary>
        ///     The scripted way out of a macro: sys.exit() ends it quietly, without an error report.
        /// </summary>
        [Fact]
        public void WillEndQuietlyOnSysExit()
        {
            MacroEntry entry = _harness.Create( "SetAlias('before', 1)\nsys.exit()\nSetAlias('after', 1)" );
            bool exceptionRaised = false;

            void OnException( Exception e )
            {
                exceptionRaised = true;
            }

            entry.MacroInvoker.ExceptionEvent += OnException;

            entry.Execute();

            Assert.True( MacroHarness.Join( entry ) );
            Assert.Equal( 1, MacroHarness.Alias( "before" ) );
            Assert.Equal( -1, MacroHarness.Alias( "after" ) );
            Assert.False( exceptionRaised );
            Assert.False( entry.IsRunning );
        }

        [Fact]
        public void WillRunAShutdownHandlerWhenStopped()
        {
            MacroEntry entry = _harness.Create(
                "def onShutdown(sender, e):\n    SetAlias('shutdown', 1)\n" +
                "Events.Shutdown += onShutdown\nSetAlias('ready', 1)\nPause(60000)" );

            entry.Execute();

            Assert.True( MacroHarness.WaitForAlias( "ready", 1 ) );

            entry.Stop();

            Assert.True( MacroHarness.Join( entry, 5000 ) );
            Assert.Equal( 1, MacroHarness.Alias( "shutdown" ) );
        }

        [Fact]
        public void WillLoopUntilStopped()
        {
            MacroEntry entry = _harness.Create( "SetAlias('runs', GetAlias('runs') + 1)" );
            AliasCommands.SetAlias( "runs", 0 );
            entry.Loop = true;

            entry.Execute();

            Assert.True( SpinWait.SpinUntil( () => MacroHarness.Alias( "runs" ) >= 3, MacroHarness.TIMEOUT_MS ) );

            entry.Stop();

            Assert.True( MacroHarness.Join( entry, 5000 ) );

            int runs = MacroHarness.Alias( "runs" );

            Thread.Sleep( 150 );

            Assert.Equal( runs, MacroHarness.Alias( "runs" ) );
        }

        /// <summary>
        ///     The loop keeps the runs at least 50ms apart, so a looping macro that does nothing cannot
        ///     spin a core flat out.
        /// </summary>
        [Fact]
        public void WillThrottleALoopingMacro()
        {
            MacroEntry entry = _harness.Create( "SetAlias('runs', GetAlias('runs') + 1)" );
            AliasCommands.SetAlias( "runs", 0 );
            entry.Loop = true;

            entry.Execute();

            Assert.True( MacroHarness.WaitForAlias( "runs", 1 ) );

            Stopwatch stopwatch = Stopwatch.StartNew();

            Assert.True( SpinWait.SpinUntil( () => MacroHarness.Alias( "runs" ) >= 5, MacroHarness.TIMEOUT_MS ) );

            stopwatch.Stop();
            entry.Stop();

            Assert.True( MacroHarness.Join( entry, 5000 ) );

            // Four more runs after the first: at least four 50ms gaps, less a little timer slack.
            Assert.True( stopwatch.ElapsedMilliseconds >= 150, $"4 loops in {stopwatch.ElapsedMilliseconds}ms" );
        }

        [Fact]
        public void WontLoopAfterAnError()
        {
            MacroEntry entry = _harness.Create( "SetAlias('runs', GetAlias('runs') + 1)\nraise Exception('stop')" );
            AliasCommands.SetAlias( "runs", 0 );
            entry.Loop = true;

            entry.Execute();

            Assert.True( MacroHarness.Join( entry ) );
            Assert.Equal( 1, MacroHarness.Alias( "runs" ) );
            Assert.False( entry.IsRunning );
        }

        [Fact]
        public void WillEndALoopFromInsideWithSysExit()
        {
            MacroEntry entry = _harness.Create(
                "SetAlias('runs', GetAlias('runs') + 1)\nif GetAlias('runs') >= 3:\n    sys.exit()" );
            AliasCommands.SetAlias( "runs", 0 );
            entry.Loop = true;

            entry.Execute();

            Assert.True( MacroHarness.Join( entry ) );
            Assert.Equal( 3, MacroHarness.Alias( "runs" ) );
        }

        /// <summary>
        ///     Executing an entry that is still running replaces the run. The old run's thread exits
        ///     after the new one has started, and its StoppedEvent must not mark the new run stopped -
        ///     otherwise the next Stop() does nothing and the macro cannot be stopped from the UI.
        /// </summary>
        [Fact]
        public void WillReplaceARunningRunAndKeepTheNewOneStoppable()
        {
            MacroEntry entry = _harness.Create( "SetAlias('ready', GetAlias('ready') + 1)\nPause(60000)" );
            AliasCommands.SetAlias( "ready", 0 );

            entry.Execute();

            Assert.True( MacroHarness.WaitForAlias( "ready", 1 ) );

            Thread first = entry.MacroInvoker.Thread;

            entry.Execute();

            Assert.True( MacroHarness.WaitForAlias( "ready", 2 ) );
            Assert.True( first.Join( 5000 ), "the replaced run kept going" );

            Thread second = entry.MacroInvoker.Thread;

            Assert.NotSame( first, second );
            Assert.True( second.IsAlive );
            Assert.True( entry.IsRunning );

            entry.Stop();

            Assert.True( second.Join( 5000 ) );
            Assert.False( entry.IsRunning );
        }

        [Fact]
        public void WillSeeItsOwnEntryAsTheCurrentMacro()
        {
            MacroEntry entry = _harness.Add( "Current", "SetMacroAlias('mine', 7)" );

            entry.Execute();

            Assert.True( MacroHarness.Join( entry ) );

            // SetMacroAlias found the running entry through GetCurrentMacro, so the alias is the
            // macro's own, not a global one.
            Assert.Equal( 7, entry.Aliases["mine"] );
            Assert.Equal( -1, MacroHarness.Alias( "mine" ) );
            Assert.Null( _harness.Manager.GetCurrentMacro() );
        }

        /// <summary>
        ///     A breakpoint parks the macro thread and publishes the pause to the entry; Resume lets it
        ///     carry on from where it stopped.
        /// </summary>
        [Fact]
        public void WillPauseOnABreakpointAndResume()
        {
            MacroEntry entry = _harness.Create( "SetAlias('a', 1)\nlocalValue = 42\nSetAlias('b', 1)" );
            entry.Breakpoints.Add( 3 );

            entry.Execute();

            Assert.True( SpinWait.SpinUntil( () => entry.IsPaused, MacroHarness.TIMEOUT_MS ) );

            Assert.Equal( 3, entry.PausedLineNumber );
            Assert.Equal( 1, MacroHarness.Alias( "a" ) );
            Assert.Equal( -1, MacroHarness.Alias( "b" ) );
            Assert.Equal( 42, entry.FrameVariables["localValue"] );

            entry.Breakpoints.Clear();
            entry.Resume();

            Assert.True( MacroHarness.Join( entry ) );
            Assert.False( entry.IsPaused );
            Assert.Equal( 1, MacroHarness.Alias( "b" ) );
        }

        /// <summary>
        ///     Stopping a macro parked on a breakpoint has to release the debugger wait as well, or the
        ///     thread never sees the interrupt and the macro stays parked forever.
        /// </summary>
        [Fact]
        public void WillStopAMacroPausedOnABreakpoint()
        {
            MacroEntry entry = _harness.Create( "SetAlias('a', 1)\nSetAlias('b', 1)" );
            entry.Breakpoints.Add( 2 );

            entry.Execute();

            Assert.True( SpinWait.SpinUntil( () => entry.IsPaused, MacroHarness.TIMEOUT_MS ) );

            entry.Stop();

            Assert.True( MacroHarness.Join( entry, 5000 ) );
            Assert.False( entry.IsRunning );
            Assert.False( entry.IsPaused );
            Assert.Equal( -1, MacroHarness.Alias( "b" ) );
        }
    }
}
