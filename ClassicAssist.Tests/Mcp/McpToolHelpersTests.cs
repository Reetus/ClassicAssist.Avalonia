using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Mcp;
using ClassicAssist.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.Mcp
{
    /// <summary>
    ///     The argument helpers every MCP tool goes through and the UI-thread bridge they use. Agents send
    ///     numbers as JSON numbers, decimal strings or hex strings, and sometimes nulls; these pin which of
    ///     those become a value, which fall back to the default, and which become an error. OnUi matters
    ///     because the real Avalonia dispatcher's InvokeAsync is fire-and-forget: the bridge must still
    ///     return the value and rethrow the exception on the calling thread.
    /// </summary>
    public class McpToolHelpersTests : IDisposable
    {
        private readonly IDispatcher _originalDispatcher;

        public McpToolHelpersTests()
        {
            EngineState.Reset();
            _originalDispatcher = Engine.Dispatcher;
        }

        public void Dispose()
        {
            Engine.Dispatcher = _originalDispatcher;
            EngineState.Reset();
        }

        [Fact]
        public void WillReadIntegerFromNumberOrDecimalOrHexString()
        {
            JObject args = new JObject { ["n"] = 12, ["d"] = " 34 ", ["h"] = "0X1f", ["neg"] = "-5" };

            Assert.Equal( 12, McpTools.GetInt( args, "n" ) );
            Assert.Equal( 34, McpTools.GetInt( args, "d" ) );
            Assert.Equal( 0x1f, McpTools.GetInt( args, "h" ) );
            Assert.Equal( -5, McpTools.GetInt( args, "neg" ) );
        }

        [Fact]
        public void WillTreatAbsentNullOrUnparseableIntegerAsUnset()
        {
            JObject args = new JObject { ["nul"] = null, ["word"] = "many", ["real"] = 2.5, ["empty"] = "", ["bare"] = "0x" };

            Assert.Null( McpTools.GetInt( args, "missing" ) );
            Assert.Null( McpTools.GetInt( args, "nul" ) );
            Assert.Null( McpTools.GetInt( args, "word" ) );
            Assert.Null( McpTools.GetInt( args, "real" ) );
            Assert.Null( McpTools.GetInt( args, "empty" ) );
            Assert.Null( McpTools.GetInt( args, "bare" ) );
            Assert.Null( McpTools.GetInt( null, "anything" ) );
        }

        [Fact]
        public void WillRequireIntegerThatParses()
        {
            JObject args = new JObject { ["x"] = "ten" };

            InvalidOperationException e = Assert.Throws<InvalidOperationException>( () => McpTools.RequireInt( args, "x" ) );

            Assert.Contains( "'x'", e.Message );
        }

        [Fact]
        public void WillTreatEmptyStringAsMissingRequiredString()
        {
            JObject args = new JObject { ["name"] = "", ["number"] = 5 };

            Assert.Throws<InvalidOperationException>( () => McpTools.RequireString( args, "name" ) );
            Assert.Equal( "5", McpTools.RequireString( args, "number" ) );
        }

        [Fact]
        public void WillReadBooleanOnlyWhenPresent()
        {
            JObject args = new JObject { ["yes"] = true, ["text"] = "true", ["nul"] = null };

            Assert.True( McpTools.GetBool( args, "yes" ) );
            Assert.True( McpTools.GetBool( args, "text" ) );
            Assert.False( McpTools.GetBool( args, "nul" ) );
            Assert.False( McpTools.GetBool( args, "missing" ) );
        }

        [Fact]
        public void WillReadStringArrayOnlyFromJsonArray()
        {
            JObject args = new JObject { ["list"] = new JArray( "a", 2, true ), ["scalar"] = "a" };

            Assert.Equal( new[] { "a", "2", "True" }, McpTools.GetStringArray( args, "list" ) );
            Assert.Null( McpTools.GetStringArray( args, "scalar" ) );
            Assert.Null( McpTools.GetStringArray( args, "missing" ) );
        }

        [Fact]
        public void WillReportPageNotTruncatedAtTheEnd()
        {
            ( List<int> page, int total, int offset, int limit ) =
                McpTools.Paginate( Enumerable.Range( 0, 10 ), 4, 8 );

            JObject info = McpTools.WithPageInfo( new JObject(), total, offset, limit, page.Count );

            Assert.Equal( new[] { 8, 9 }, page );
            Assert.False( info["truncated"]?.ToObject<bool>() );
            Assert.Equal( 2, info["returned"]?.ToObject<int>() );
        }

        [Fact]
        public void WillMarkOnlyMutatingToolsAsDestructive()
        {
            string[] mutating =
            {
                "createMacro", "updateMacro", "deleteMacro", "playMacro", "stopMacro", "stopAllMacros", "invokeCommand",
                "executeHotkey"
            };

            foreach ( McpTool tool in McpTools.GetTools() )
            {
                bool expected = mutating.Contains( tool.Name );

                Assert.Equal( expected, tool.Annotations?["destructiveHint"]?.ToObject<bool>() );
                Assert.Equal( !expected, tool.Annotations?["readOnlyHint"]?.ToObject<bool>() );
                Assert.Equal( tool.Name, tool.Annotations?["title"]?.ToString() );
            }
        }

        [Fact]
        public void WillMarshalOntoTheUiThreadAndReturnTheValue()
        {
            using ThreadDispatcher dispatcher = new ThreadDispatcher();

            Engine.Dispatcher = dispatcher;

            int threadId = McpTools.OnUi( () => Environment.CurrentManagedThreadId );

            Assert.Equal( dispatcher.ThreadId, threadId );
            Assert.NotEqual( Environment.CurrentManagedThreadId, threadId );
        }

        [Fact]
        public void WillRethrowUiThreadExceptionOnTheCaller()
        {
            using ThreadDispatcher dispatcher = new ThreadDispatcher();

            Engine.Dispatcher = dispatcher;

            InvalidOperationException e = Assert.Throws<InvalidOperationException>( () =>
                McpTools.OnUi( () => throw new InvalidOperationException( "from ui" ) ) );

            Assert.Equal( "from ui", e.Message );
            Assert.Throws<InvalidOperationException>( () => McpTools.OnUi<int>( () => throw new InvalidOperationException( "typed" ) ) );
        }

        /// <summary>
        ///     Through the tool surface: a tool whose UI-thread work throws must come back as a tool error,
        ///     not hang or escape.
        /// </summary>
        [Fact]
        public void WillTurnUiThreadFailureIntoToolError()
        {
            using ThreadDispatcher dispatcher = new ThreadDispatcher();

            Engine.Dispatcher = dispatcher;

            CallToolResult result = McpTools.Invoke( "updateMacro", new JObject { ["name"] = "No Such Macro Here", ["code"] = "pass" } );

            Assert.True( result.IsError );
            Assert.Contains( "not found", result.Content.Single().Text );
        }

        /// <summary>
        ///     A UI dispatcher stand-in: a dedicated thread draining a queue, with InvokeAsync returning
        ///     before the work runs - the shape of Avalonia's dispatcher that OnUi has to bridge.
        /// </summary>
        private sealed class ThreadDispatcher : IDispatcher, IDisposable
        {
            private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
            private readonly Thread _thread;

            public ThreadDispatcher()
            {
                _thread = new Thread( () =>
                {
                    foreach ( Action action in _queue.GetConsumingEnumerable() )
                    {
                        action();
                    }
                } ) { IsBackground = true, Name = "Test UI thread" };

                _thread.Start();
            }

            public int ThreadId => _thread.ManagedThreadId;

            public void Invoke( Action action )
            {
                _queue.Add( action );
            }

            public Task InvokeAsync( Action action )
            {
                _queue.Add( action );

                return Task.CompletedTask;
            }

            public bool CheckAccess()
            {
                return Thread.CurrentThread == _thread;
            }

            public void Dispose()
            {
                _queue.CompleteAdding();
                _thread.Join( 5000 );
                _queue.Dispose();
            }
        }
    }
}
