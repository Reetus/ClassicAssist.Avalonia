using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using ClassicAssist.Data.Macros;
using ClassicAssist.DebugAdapter;
using ClassicAssist.UI.Misc;
using IronPython.Hosting;
using IronPython.Runtime.Exceptions;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;

namespace ClassicAssist.Tests.DebugAdapter
{
    /// <summary>
    ///     Runs Python on a private engine with a trace hook, so the debugger pieces can be driven with
    ///     real <see cref="TraceBackFrame" />s on the test thread, the same way MacroInvoker.OnTrace drives
    ///     them on a macro thread, but without one. Assertion failures inside the hook are rethrown after
    ///     the script finishes rather than being swallowed by the interpreter.
    /// </summary>
    internal static class PythonFrames
    {
        public static readonly string ScriptPath = Path.Combine( Path.GetTempPath(), "ca-debug-adapter-test.py" );

        public static ScriptScope Run( string source, Action<TraceBackFrame, string> onTrace,
            IDictionary<string, object> globals = null )
        {
            ScriptEngine engine = Python.CreateEngine();
            ScriptScope scope = globals == null ? engine.CreateScope() : engine.CreateScope( globals );
            ExceptionDispatchInfo failure = null;
            TracebackDelegate trace = null;

            trace = ( frame, result, payload ) =>
            {
                if ( failure == null )
                {
                    try
                    {
                        onTrace( frame, result );
                    }
                    catch ( Exception e )
                    {
                        failure = ExceptionDispatchInfo.Capture( e );
                    }
                }

                return trace;
            };

            engine.SetTrace( trace );

            try
            {
                engine.CreateScriptSourceFromString( source, ScriptPath, SourceCodeKind.Statements ).Execute( scope );
            }
            finally
            {
                engine.SetTrace( null );
            }

            failure?.Throw();

            return scope;
        }

        /// <summary>Runs the source and calls <paramref name="atLine" /> the first time a line event hits it.</summary>
        public static ScriptScope RunToLine( string source, int line, Action<TraceBackFrame> atLine,
            IDictionary<string, object> globals = null )
        {
            bool hit = false;

            ScriptScope scope = Run( source, ( frame, result ) =>
            {
                if ( !hit && result == "line" && (int) frame.f_lineno == line )
                {
                    hit = true;
                    atLine( frame );
                }
            }, globals );

            Xunit.Assert.True( hit, $"line {line} was never traced" );

            return scope;
        }
    }

    /// <summary>
    ///     Saves and restores the globals the debug adapter touches: the <see cref="DebugManager" />
    ///     singleton MacroInvoker consults, and the <see cref="MacroManager" /> macro list and current
    ///     macro the session's launch/terminate/restart requests use.
    /// </summary>
    internal sealed class DebugAdapterGlobals : IDisposable
    {
        private readonly MacroEntry _originalCurrent;
        private readonly DebugManager _originalInstance;
        private readonly ObservableCollectionEx<MacroEntry> _originalItems;

        public DebugAdapterGlobals()
        {
            MacroManager manager = MacroManager.GetInstance();

            _originalInstance = DebugManager.Instance;
            _originalItems = manager.Items;
            _originalCurrent = manager.CurrentMacro;

            manager.Items = new ObservableCollectionEx<MacroEntry>();
            manager.CurrentMacro = null;
        }

        public void Dispose()
        {
            MacroManager manager = MacroManager.GetInstance();

            foreach ( MacroEntry entry in manager.Items )
            {
                entry.Stop();
                entry.MacroInvoker.Thread?.Join( DapHarness.Timeout );
            }

            manager.Items = _originalItems;
            manager.CurrentMacro = _originalCurrent;
            SetInstance( _originalInstance );
        }

        /// <summary>Installs a manager as the process-wide instance MacroInvoker.OnTrace consults.</summary>
        public static void SetInstance( DebugManager manager )
        {
            typeof( DebugManager ).GetProperty( nameof( DebugManager.Instance ) ).SetValue( null, manager );
        }

        /// <summary>
        ///     Registers <paramref name="thread" /> with the manager as a running macro, as MacroManager
        ///     does when a macro starts.
        /// </summary>
        public static MacroEntry RegisterThread( DebugManager manager, Thread thread, string name )
        {
            MacroEntry entry = new MacroEntry { Name = name };
            entry.MacroInvoker.Thread = thread;
            manager.OnMacroStarted( entry );

            return entry;
        }
    }
}
