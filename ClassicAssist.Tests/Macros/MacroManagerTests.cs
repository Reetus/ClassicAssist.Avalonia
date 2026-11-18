using System;
using System.Collections.Generic;
using System.Threading;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using Xunit;
using MC = ClassicAssist.Data.Macros.Commands.MacroCommands;

namespace ClassicAssist.Tests.Macros
{
    /// <summary>
    ///     <see cref="MacroManager" />'s scheduling rules with real macro threads: one foreground macro
    ///     at a time (a new one interrupts the current one), background macros alongside it and toggled
    ///     by their hotkey, DoNotAutoInterrupt / Replay, autostart, and macros starting other macros.
    ///     Getting these wrong leaves two foreground macros fighting over the same targets, or a macro
    ///     the player believes stopped still running.
    /// </summary>
    public class MacroManagerTests : IDisposable
    {
        private const string BLOCKING = "SetAlias('{0}', 1)\nPause(60000)";

        private readonly MacroHarness _harness = new MacroHarness();

        public void Dispose()
        {
            _harness.Dispose();
        }

        private MacroEntry AddBlocking( string name, bool background = false )
        {
            return _harness.Add( name, string.Format( BLOCKING, name ), background );
        }

        private void StartAndWait( MacroEntry entry )
        {
            _harness.Manager.Execute( entry );

            Assert.True( MacroHarness.WaitForAlias( entry.Name, 1 ), $"{entry.Name} did not start" );
        }

        [Fact]
        public void WillInterruptTheCurrentForegroundMacro()
        {
            MacroEntry first = AddBlocking( "first" );
            MacroEntry second = AddBlocking( "second" );

            StartAndWait( first );
            Thread firstThread = first.MacroInvoker.Thread;

            StartAndWait( second );

            Assert.True( firstThread.Join( 5000 ), "the interrupted macro kept running" );
            Assert.False( first.IsRunning );
            Assert.True( second.IsRunning );
            Assert.Same( second, _harness.Manager.CurrentMacro );
        }

        [Fact]
        public void WillRunBackgroundMacrosAlongsideTheForegroundOne()
        {
            MacroEntry background = AddBlocking( "background", true );
            MacroEntry first = AddBlocking( "first" );
            MacroEntry second = AddBlocking( "second" );

            StartAndWait( background );
            StartAndWait( first );
            StartAndWait( second );

            Assert.True( background.IsRunning );
            Assert.True( background.MacroInvoker.IsRunning );
            Assert.False( first.IsRunning );

            // A background macro is never the "current" one, so Stop() without a name leaves it alone.
            Assert.Same( second, _harness.Manager.CurrentMacro );

            _harness.Manager.Stop();

            Assert.True( MacroHarness.Join( second, 5000 ) );
            Assert.True( background.IsRunning );
        }

        [Fact]
        public void WillToggleABackgroundMacroOnRepeatedExecute()
        {
            MacroEntry background = AddBlocking( "background", true );

            StartAndWait( background );

            _harness.Manager.Execute( background );

            Assert.True( MacroHarness.Join( background, 5000 ) );
            Assert.False( background.IsRunning );
        }

        [Fact]
        public void WillRestartTheSameForegroundMacroOnRepeatedExecute()
        {
            MacroEntry entry = _harness.Add( "restart", "SetAlias('runs', GetAlias('runs') + 1)\nPause(60000)" );
            AliasCommands.SetAlias( "runs", 0 );

            _harness.Manager.Execute( entry );
            Assert.True( MacroHarness.WaitForAlias( "runs", 1 ) );

            _harness.Manager.Execute( entry );

            Assert.True( MacroHarness.WaitForAlias( "runs", 2 ) );
            Assert.True( entry.IsRunning );
        }

        [Fact]
        public void WontRestartARunningMacroMarkedDoNotAutoInterrupt()
        {
            MacroEntry entry = _harness.Add( "keep", "SetAlias('runs', GetAlias('runs') + 1)\nPause(60000)" );
            entry.DoNotAutoInterrupt = true;
            AliasCommands.SetAlias( "runs", 0 );

            _harness.Manager.Execute( entry );
            Assert.True( MacroHarness.WaitForAlias( "runs", 1 ) );

            Thread thread = entry.MacroInvoker.Thread;

            _harness.Manager.Execute( entry );

            Assert.Same( thread, entry.MacroInvoker.Thread );
            Assert.True( thread.IsAlive );
            Assert.Equal( 1, MacroHarness.Alias( "runs" ) );
        }

        /// <summary>
        ///     DoNotAutoInterrupt protects a macro from its own hotkey, not from a different macro.
        /// </summary>
        [Fact]
        public void WillInterruptADoNotAutoInterruptMacroForADifferentMacro()
        {
            MacroEntry keep = AddBlocking( "keep" );
            keep.DoNotAutoInterrupt = true;
            MacroEntry other = AddBlocking( "other" );

            StartAndWait( keep );
            StartAndWait( other );

            Assert.True( MacroHarness.Join( keep, 5000 ) );
            Assert.Same( other, _harness.Manager.CurrentMacro );
        }

        /// <summary>
        ///     Replay() restarts the calling macro even when it is marked DoNotAutoInterrupt - that is
        ///     what Replay is for.
        /// </summary>
        [Fact]
        public void WillReplayAMacroMarkedDoNotAutoInterrupt()
        {
            MacroEntry entry = _harness.Add( "replay",
                "SetAlias('runs', GetAlias('runs') + 1)\nif GetAlias('runs') < 2:\n    Replay()\n    Pause(60000)" );
            entry.DoNotAutoInterrupt = true;
            AliasCommands.SetAlias( "runs", 0 );

            _harness.Manager.Execute( entry );

            Assert.True( MacroHarness.WaitForAlias( "runs", 2 ) );
            Assert.True( SpinWait.SpinUntil( () => !entry.MacroInvoker.IsRunning, MacroHarness.TIMEOUT_MS ) );

            // MacroInvoker.Stop clears the flag once it has interrupted the old run.
            Assert.False( _harness.Manager.Replay );
        }

        [Fact]
        public void WillPassArgumentsThroughExecute()
        {
            MacroEntry entry = _harness.Add( "withargs", "SetAlias('arg', args[0])" );

            _harness.Manager.Execute( entry, new object[] { 99 } );

            Assert.True( MacroHarness.Join( entry ) );
            Assert.Equal( 99, MacroHarness.Alias( "arg" ) );
        }

        [Fact]
        public void WillAutostartOnlyMacrosMarkedAutostart()
        {
            MacroEntry auto = _harness.Add( "auto", "SetAlias('auto', 1)", true );
            auto.IsAutostart = true;
            MacroEntry manual = _harness.Add( "manual", "SetAlias('manual', 1)", true );

            _harness.Manager.Autostart();

            Assert.True( MacroHarness.WaitForAlias( "auto", 1 ) );
            Assert.True( MacroHarness.Join( auto ) );
            Assert.Equal( -1, MacroHarness.Alias( "manual" ) );
            Assert.Null( manual.MacroInvoker.Thread );
        }

        [Fact]
        public void WillAutostartSeveralBackgroundMacrosTogether()
        {
            MacroEntry a = AddBlocking( "a", true );
            MacroEntry b = AddBlocking( "b", true );
            a.IsAutostart = true;
            b.IsAutostart = true;

            _harness.Manager.Autostart();

            Assert.True( MacroHarness.WaitForAlias( "a", 1 ) );
            Assert.True( MacroHarness.WaitForAlias( "b", 1 ) );
            Assert.True( a.IsRunning );
            Assert.True( b.IsRunning );
        }

        [Fact]
        public void StopAllWillStopForegroundAndBackground()
        {
            MacroEntry background = AddBlocking( "background", true );
            MacroEntry foreground = AddBlocking( "foreground" );

            StartAndWait( background );
            StartAndWait( foreground );

            _harness.Manager.StopAll();

            Assert.True( MacroHarness.Join( background, 5000 ) );
            Assert.True( MacroHarness.Join( foreground, 5000 ) );
            Assert.False( background.IsRunning );
            Assert.False( foreground.IsRunning );
        }

        [Fact]
        public void StopByNameIsCaseInsensitive()
        {
            MacroEntry background = AddBlocking( "MyBackground", true );

            StartAndWait( background );

            MC.Stop( "mybackground" );

            Assert.True( MacroHarness.Join( background, 5000 ) );
            Assert.False( background.IsRunning );
        }

        /// <summary>
        ///     PlayMacro of a background macro from a foreground one: both run, with the arguments passed
        ///     through.
        /// </summary>
        [Fact]
        public void PlayMacroWillStartABackgroundMacroAlongsideTheCaller()
        {
            MacroEntry child = _harness.Add( "child", "SetAlias('child', args[0])\nPause(60000)", true );
            MacroEntry parent = _harness.Add( "parent",
                "PlayMacro('child', 5)\nwhile GetAlias('child') != 5:\n    Pause(10)\nSetAlias('parent', 1)\nPause(60000)" );

            _harness.Manager.Execute( parent );

            Assert.True( MacroHarness.WaitForAlias( "parent", 1 ) );
            Assert.True( parent.IsRunning );
            Assert.True( child.IsRunning );
        }

        /// <summary>
        ///     PlayMacro of a foreground macro from a foreground one hands over: the caller is
        ///     interrupted, the same as pressing the other macro's hotkey.
        /// </summary>
        [Fact]
        public void PlayMacroWillHandOverToAForegroundMacro()
        {
            MacroEntry child = AddBlocking( "child" );
            MacroEntry parent = _harness.Add( "parent", "PlayMacro('child')\nPause(60000)\nSetAlias('parent', 1)" );

            _harness.Manager.Execute( parent );

            Assert.True( MacroHarness.WaitForAlias( "child", 1 ) );
            Assert.True( MacroHarness.Join( parent, 5000 ), "the calling macro kept running" );
            Assert.Equal( -1, MacroHarness.Alias( "parent" ) );
            Assert.Same( child, _harness.Manager.CurrentMacro );
            Assert.True( child.IsRunning );
        }

        [Fact]
        public void IsRunningWillSeeAnotherMacroRunning()
        {
            MacroEntry background = AddBlocking( "background", true );
            MacroEntry probe = _harness.Add( "probe",
                "SetAlias('before', 1 if IsRunning('background') else 0)" );

            StartAndWait( background );

            _harness.Manager.Execute( probe );

            Assert.True( MacroHarness.Join( probe ) );
            Assert.Equal( 1, MacroHarness.Alias( "before" ) );
        }

        /// <summary>
        ///     A macro stopping itself by name ends there: the lines after Stop() do not run.
        /// </summary>
        [Fact]
        public void WillEndAMacroThatStopsItself()
        {
            MacroEntry entry = _harness.Add( "self", "SetAlias('before', 1)\nStop('self')\nSetAlias('after', 1)" );

            _harness.Manager.Execute( entry );

            Assert.True( MacroHarness.Join( entry, 5000 ) );
            Assert.Equal( 1, MacroHarness.Alias( "before" ) );
            Assert.Equal( -1, MacroHarness.Alias( "after" ) );
            Assert.False( entry.IsRunning );
        }

        [Fact]
        public void WillRaiseManagerStartedAndStoppedEvents()
        {
            MacroEntry entry = _harness.Add( "events", "x = 1" );
            List<string> events = new List<string>();
            ManualResetEventSlim stopped = new ManualResetEventSlim();

            void OnStarted( MacroEntry e )
            {
                lock ( events )
                {
                    events.Add( "started:" + e.Name );
                }
            }

            void OnStopped( MacroEntry e )
            {
                lock ( events )
                {
                    events.Add( "stopped:" + e.Name );
                }

                stopped.Set();
            }

            _harness.Manager.MacroStartedEvent += OnStarted;
            _harness.Manager.MacroStoppedEvent += OnStopped;

            try
            {
                _harness.Manager.Execute( entry );

                Assert.True( stopped.Wait( MacroHarness.TIMEOUT_MS, TestContext.Current.CancellationToken ) );
                Assert.True( MacroHarness.Join( entry ) );
            }
            finally
            {
                _harness.Manager.MacroStartedEvent -= OnStarted;
                _harness.Manager.MacroStoppedEvent -= OnStopped;
                stopped.Dispose();
            }

            lock ( events )
            {
                Assert.Contains( "started:events", events );
                Assert.Single( events, e => e == "stopped:events" );
            }
        }
    }
}
