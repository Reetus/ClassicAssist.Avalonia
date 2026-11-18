using System;
using System.Collections.Generic;
using System.Threading;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.UI.Misc;
using Xunit;

namespace ClassicAssist.Tests.Macros
{
    /// <summary>
    ///     Shared set-up for tests that run real macros on real macro threads. It starts every test from
    ///     an empty world and an empty <see cref="MacroManager" />, and on Dispose stops and joins every
    ///     macro it created, so no macro thread outlives its test and nothing it changed on the manager
    ///     singleton leaks into the next one.
    /// </summary>
    internal sealed class MacroHarness : IDisposable
    {
        /// <summary>Upper bound for anything a test waits on. Generous: the first macro of a run pays
        ///     the IronPython warm-up, and nothing healthy gets anywhere near it.</summary>
        public const int TIMEOUT_MS = 15000;

        private readonly List<MacroEntry> _created = new List<MacroEntry>();
        private readonly MacroEntry _originalCurrent;
        private readonly ObservableCollectionEx<MacroEntry> _originalItems;
        private readonly bool _originalQuietMode;
        private readonly bool _originalReplay;
        private readonly bool _originalDefaultQuietMode;

        public MacroHarness()
        {
            EngineState.Reset();

            Manager = MacroManager.GetInstance();

            _originalItems = Manager.Items;
            _originalCurrent = Manager.CurrentMacro;
            _originalReplay = Manager.Replay;
            _originalQuietMode = MacroManager.QuietMode;
            _originalDefaultQuietMode = Options.CurrentOptions.DefaultMacroQuietMode;

            Manager.Items = new ObservableCollectionEx<MacroEntry>();
            Manager.CurrentMacro = null;
            Manager.Replay = false;

            // Background macros announce themselves with a system message unless quiet; keep the
            // journal to what the tests write. MacroInvoker.Execute resets QuietMode from this option.
            Options.CurrentOptions.DefaultMacroQuietMode = true;
            MacroManager.QuietMode = true;
        }

        public MacroManager Manager { get; }

        /// <summary>
        ///     A macro registered with the manager the way the macros tab registers one: its hotkey
        ///     action goes through <see cref="MacroManager.Execute" />, so PlayMacro and Replay behave
        ///     as they do in the app.
        /// </summary>
        public MacroEntry Add( string name, string code, bool background = false )
        {
            MacroEntry entry = new MacroEntry { Name = name, Macro = code, IsBackground = background };

            entry.Action = ( hks, parameters ) => MacroManager.GetInstance().Execute( entry, parameters );

            Manager.Items.Add( entry );
            _created.Add( entry );

            return entry;
        }

        /// <summary>An entry that is not in the manager's list, for driving MacroEntry/MacroInvoker directly.</summary>
        public MacroEntry Create( string code )
        {
            MacroEntry entry = new MacroEntry { Macro = code };

            _created.Add( entry );

            return entry;
        }

        public void Dispose()
        {
            foreach ( MacroEntry entry in _created )
            {
                Thread thread = entry.MacroInvoker.Thread;

                if ( thread == null || !thread.IsAlive )
                {
                    continue;
                }

                // Paused on a breakpoint: release it first, or the interrupt has nothing to land on
                // until the debugger wait returns.
                entry.Breakpoints?.Clear();
                entry.Resume();
                entry.MacroInvoker.Stop();
                thread.Join( TIMEOUT_MS );
            }

            Manager.Items = _originalItems;
            Manager.CurrentMacro = _originalCurrent;
            Manager.Replay = _originalReplay;
            MacroManager.QuietMode = _originalQuietMode;
            Options.CurrentOptions.DefaultMacroQuietMode = _originalDefaultQuietMode;

            AliasCommands._aliases.Clear();
        }

        /// <summary>Runs an unlisted macro to completion and returns its invoker.</summary>
        public MacroInvoker RunToEnd( string code, object[] parameters = null )
        {
            MacroEntry entry = Create( code );

            entry.Execute( parameters );

            Assert.True( Join( entry ), "macro did not finish" );

            return entry.MacroInvoker;
        }

        /// <summary>Waits, bounded, for the entry's current run to end.</summary>
        public static bool Join( MacroEntry entry, int timeout = TIMEOUT_MS )
        {
            return entry.MacroInvoker.Thread?.Join( timeout ) ?? true;
        }

        /// <summary>Waits, bounded, until a macro has set the alias - the tests' "I got here" signal.</summary>
        public static bool WaitForAlias( string name, int value, int timeout = TIMEOUT_MS )
        {
            return SpinWait.SpinUntil( () => Alias( name ) == value, timeout );
        }

        /// <summary>Global alias value, or -1. Read directly so the caller's thread does not matter.</summary>
        public static int Alias( string name )
        {
            return AliasCommands._aliases.TryGetValue( name.ToLower(), out int value ) ? value : -1;
        }
    }
}
