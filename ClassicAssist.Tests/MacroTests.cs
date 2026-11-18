using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using ClassicAssist.Data.Macros;
using ClassicAssist.Shared;
using ClassicAssist.Tests.Macros;
using Xunit;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Basic macro execution through <see cref="MacroManager" />, and the contract every macro command
    ///     must meet: a <see cref="CommandsDisplayAttribute" /> whose help text and category resolve.
    ///     The attribute is what puts a command in the editor's command list and help, and its constructor
    ///     throws on a missing translation - so one untranslated command breaks the whole list, not just
    ///     its own entry. The deeper runtime behaviour lives in <c>Macros/</c>.
    /// </summary>
    public class MacroTests : IDisposable
    {
        private readonly MacroHarness _harness = new MacroHarness();

        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>
        ///     Public statics in the command classes that are not macro commands: accessors the Active
        ///     Objects window and profile save use to read the commands' state. Macros can still see them
        ///     (the whole class is imported), but they are not offered in the command list.
        /// </summary>
        private static readonly HashSet<string> _hostAccessors = new HashSet<string>
        {
            "AliasCommands.GetAllAliases",
            "AliasCommands.GetAllPlayerAliases",
            "AliasCommands.GetPlayerAliases",
            "ListCommands.GetAllLists",
            "TimerCommands.GetAllTimers"
        };

        private static IEnumerable<MethodInfo> CommandMethods()
        {
            return Assembly.GetAssembly( typeof( Engine ) ).GetTypes()
                .Where( t => t.Namespace != null && t.Namespace.EndsWith( "Macros.Commands" ) && t.IsClass &&
                             t.IsPublic && !t.Name.Contains( "Dummy" ) )
                .SelectMany( t => t.GetMethods( BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly ) )
                .Where( m => !m.IsSpecialName && !_hostAccessors.Contains( $"{m.DeclaringType?.Name}.{m.Name}" ) );
        }

        [Fact]
        public void WillExecute()
        {
            MacroEntry entry = _harness.Add( "Execute", "SetAlias('ran', 1)" );

            _harness.Manager.Execute( entry );

            Assert.Same( entry, _harness.Manager.CurrentMacro );
            Assert.True( MacroHarness.Join( entry ) );
            Assert.False( entry.MacroInvoker.IsFaulted, entry.MacroInvoker.Exception?.ToString() );
            Assert.Equal( 1, MacroHarness.Alias( "ran" ) );
        }

        [Fact]
        public void WillExecuteAnEmptyMacro()
        {
            MacroEntry entry = _harness.Add( "Empty", string.Empty );

            _harness.Manager.Execute( entry );

            Assert.True( MacroHarness.Join( entry ) );
            Assert.False( entry.MacroInvoker.IsFaulted );
            Assert.False( entry.IsRunning );
        }

        [Fact]
        public void WillAbortRunning()
        {
            MacroEntry entry = _harness.Add( "Abort", "SetAlias('ready', 1)\nwhile True:\n    pass" );

            _harness.Manager.Execute( entry );

            Assert.True( MacroHarness.WaitForAlias( "ready", 1 ) );

            _harness.Manager.Stop();

            Assert.True( MacroHarness.Join( entry, 5000 ) );
            Assert.False( entry.IsRunning );
        }

        [Fact]
        public void WillRaiseStartedEvent()
        {
            MacroEntry entry = _harness.Add( "Started", string.Empty );
            using ManualResetEventSlim started = new ManualResetEventSlim();

            void OnStarted()
            {
                started.Set();
            }

            entry.MacroInvoker.StartedEvent += OnStarted;

            _harness.Manager.Execute( entry );

            Assert.True( started.Wait( MacroHarness.TIMEOUT_MS, TestContext.Current.CancellationToken ) );
            Assert.True( MacroHarness.Join( entry ) );
        }

        [Fact]
        public void WillRaiseStoppedEvent()
        {
            MacroEntry entry = _harness.Add( "Stopped", string.Empty );
            using ManualResetEventSlim stopped = new ManualResetEventSlim();

            void OnStopped()
            {
                stopped.Set();
            }

            entry.MacroInvoker.StoppedEvent += OnStopped;

            _harness.Manager.Execute( entry );

            Assert.True( stopped.Wait( MacroHarness.TIMEOUT_MS, TestContext.Current.CancellationToken ) );
            Assert.True( MacroHarness.Join( entry ) );
        }

        [Fact]
        public void WillRaiseExceptionEvent()
        {
            MacroEntry entry = _harness.Add( "Throws", "kjdkdsdksdfsdk" );
            Exception reported = null;
            bool faultedWhenRaised = false;

            void OnException( Exception e )
            {
                reported = e;
                faultedWhenRaised = entry.MacroInvoker.IsFaulted;
            }

            entry.MacroInvoker.ExceptionEvent += OnException;

            _harness.Manager.Execute( entry );

            Assert.True( MacroHarness.Join( entry ) );
            Assert.NotNull( reported );
            Assert.True( faultedWhenRaised );
            Assert.Same( reported, entry.MacroInvoker.Exception );
        }

        [Fact]
        public void EnsureAllCommandsHaveAttribute()
        {
            List<string> missing = new List<string>();

            foreach ( MethodInfo method in CommandMethods() )
            {
                if ( !method.IsDefined( typeof( CommandsDisplayAttribute ) ) )
                {
                    missing.Add( $"{method.DeclaringType?.Name}.{method.Name}" );
                }
            }

            Assert.True( missing.Count == 0, "No CommandsDisplayAttribute: " + string.Join( ", ", missing.Distinct() ) );
        }

        /// <summary>
        ///     Instantiating the attribute is the check: its constructor and Category setter throw when the
        ///     help text or the category name has no resource entry.
        /// </summary>
        [Fact]
        public void EnsureAllCategoriesAreLocalizable()
        {
            List<string> failures = new List<string>();

            foreach ( MethodInfo method in CommandMethods().Where( m => m.IsDefined( typeof( CommandsDisplayAttribute ) ) ) )
            {
                try
                {
                    CommandsDisplayAttribute attribute = method.GetCustomAttribute<CommandsDisplayAttribute>();

                    if ( string.IsNullOrEmpty( attribute?.Category ) )
                    {
                        failures.Add( $"{method.DeclaringType?.Name}.{method.Name}: no category" );
                    }
                }
                catch ( Exception e )
                {
                    failures.Add( $"{method.DeclaringType?.Name}.{method.Name}: {e.Message}" );
                }
            }

            Assert.True( failures.Count == 0, string.Join( Environment.NewLine, failures ) );
        }
    }
}
