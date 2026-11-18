using System;
using System.IO;
using System.Linq;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros;
using ClassicAssist.Mcp;
using ClassicAssist.UI.Misc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.Mcp
{
    /// <summary>
    ///     The macro tools (create/update/delete, play/stop, status, wait) against the real MacroManager
    ///     and real IronPython runs. These are how an agent writes and debugs macros, so the parts pinned
    ///     are the ones it acts on: a Python error comes back with its type, message and line; a loop macro
    ///     is reported as still running rather than as a success; waitForMacro clamps its timeout and
    ///     refuses to guess between several running macros; names are unique case-insensitively; and a
    ///     file-backed macro's file is created under the Macros folder and removed with it.
    ///     <para>
    ///         The macro collection, current macro, quiet mode, current options and the profile/global
    ///         directories are all swapped for test values and restored in Dispose.
    ///     </para>
    /// </summary>
    public class McpMacroToolsTests : IDisposable
    {
        private const string LOOP = "x = 0\nwhile True:\n    x = x + 1\n";

        private readonly ObservableCollectionEx<MacroEntry> _originalItems;
        private readonly MacroEntry _originalCurrent;
        private readonly bool _originalQuietMode;
        private readonly Options _originalOptions;
        private readonly string _originalProfileDirectory;
        private readonly string _originalGlobalDirectory;
        private readonly string _tempDir;

        public McpMacroToolsTests()
        {
            EngineState.Reset();

            MacroManager manager = MacroManager.GetInstance();

            _originalItems = manager.Items;
            _originalCurrent = manager.CurrentMacro;
            _originalQuietMode = MacroManager.QuietMode;
            _originalOptions = Options.CurrentOptions;
            _originalProfileDirectory = AssistantOptions.ProfileDirectory;
            _originalGlobalDirectory = AssistantOptions.GlobalDirectory;

            _tempDir = Path.Combine( Path.GetTempPath(), "ClassicAssistMcpMacros", Guid.NewGuid().ToString( "N" ) );
            AssistantOptions.ProfileDirectory = Path.Combine( _tempDir, "Profiles" );
            AssistantOptions.GlobalDirectory = Path.Combine( _tempDir, "Global" );
            Options.CurrentOptions = new Options { Name = "mcp-macros.json" };

            manager.Items = new ObservableCollectionEx<MacroEntry>();
            manager.CurrentMacro = null;
            MacroManager.QuietMode = true;
        }

        public void Dispose()
        {
            MacroManager manager = MacroManager.GetInstance();

            foreach ( MacroEntry entry in manager.Items?.ToArray() ?? Array.Empty<MacroEntry>() )
            {
                entry.Stop();
                entry.MacroInvoker.Thread?.Join( 5000 );
            }

            manager.Items = _originalItems;
            manager.CurrentMacro = _originalCurrent;
            MacroManager.QuietMode = _originalQuietMode;
            Options.CurrentOptions = _originalOptions;
            AssistantOptions.ProfileDirectory = _originalProfileDirectory;
            AssistantOptions.GlobalDirectory = _originalGlobalDirectory;

            try
            {
                Directory.Delete( _tempDir, true );
            }
            catch ( IOException )
            {
                // best effort
            }
            catch ( UnauthorizedAccessException )
            {
                // best effort
            }

            EngineState.Reset();
        }

        private static string OkText( string tool, JObject args = null )
        {
            CallToolResult result = McpTools.Invoke( tool, args ?? new JObject() );

            Assert.False( result.IsError, $"{tool} failed: {result.Content.Single().Text}" );

            return result.Content.Single().Text;
        }

        private static JObject Ok( string tool, JObject args = null )
        {
            return JObject.Parse( OkText( tool, args ) );
        }

        private static string Fail( string tool, JObject args = null )
        {
            CallToolResult result = McpTools.Invoke( tool, args ?? new JObject() );

            Assert.True( result.IsError, $"{tool} should have failed but returned {result.Content.Single().Text}" );

            return result.Content.Single().Text;
        }

        private static MacroEntry Add( string name, string code = "", bool background = false, bool running = false )
        {
            MacroEntry entry = new MacroEntry { Name = name, Macro = code, IsBackground = background };

            MacroManager.GetInstance().Items.Add( entry );

            if ( running )
            {
                entry.IsRunning = true;
            }

            return entry;
        }

        private static JObject Create( string name, string code, string filePath = null, bool background = false )
        {
            JObject args = new JObject { ["name"] = name, ["code"] = code, ["background"] = background };

            if ( filePath != null )
            {
                args["filePath"] = filePath;
            }

            return Ok( "createMacro", args );
        }

        [Fact]
        public void WillCreateEmbeddedMacroAndReadItBack()
        {
            JObject created = Create( "Heal Loop", "print('hi')", background: true );

            Assert.Equal( "Heal Loop", created["name"]?.ToString() );
            Assert.True( created["isBackground"]?.ToObject<bool>() );
            Assert.False( created["isFileBacked"]?.ToObject<bool>() );
            Assert.Equal( "print('hi')", created["code"]?.ToString() );

            JObject read = Ok( "getMacro", new JObject { ["name"] = "heal loop" } );

            Assert.Equal( "print('hi')", read["code"]?.ToString() );
            Assert.True( read["isBackground"]?.ToObject<bool>() );

            // The macro was persisted to the (temp) profile.
            Assert.True( File.Exists( Path.Combine( AssistantOptions.ProfileDirectory, "mcp-macros.json" ) ) );
        }

        [Fact]
        public void WillRejectDuplicateNameIgnoringCase()
        {
            Create( "Mining", "pass" );

            Assert.Contains( "already exists", Fail( "createMacro", new JObject { ["name"] = "MINING", ["code"] = "pass" } ) );
            Assert.Single( MacroManager.GetInstance().Items );
        }

        [Fact]
        public void WillRequireNameAndCode()
        {
            Assert.Contains( "'code'", Fail( "createMacro", new JObject { ["name"] = "NoCode" } ) );
            Assert.Contains( "'name'", Fail( "createMacro", new JObject { ["code"] = "pass" } ) );
            Assert.Empty( MacroManager.GetInstance().Items );
        }

        [Fact]
        public void WillRejectNonBooleanBackgroundFlag()
        {
            Fail( "createMacro", new JObject { ["name"] = "Odd", ["code"] = "pass", ["background"] = "sometimes" } );

            Assert.Empty( MacroManager.GetInstance().Items );
        }

        [Fact]
        public void WillReportUninitialisedMacroCollection()
        {
            MacroManager.GetInstance().Items = null;

            Assert.Contains( "not initialized", Fail( "createMacro", new JObject { ["name"] = "Early", ["code"] = "pass" } ) );

            // Reading tools treat a missing collection as empty rather than failing.
            Assert.Equal( "[]", OkText( "listMacros" ) );
        }

        [Fact]
        public void WillWriteRelativeFileBackedMacroUnderMacrosFolderAndDeleteIt()
        {
            JObject created = Create( "Filed", "print('file')", "sub/filed" );

            string expected = Path.Combine( AssistantOptions.GetGlobalPath(), "Macros", "sub", "filed.py" );

            Assert.True( created["isFileBacked"]?.ToObject<bool>() );
            Assert.Equal( expected, Path.GetFullPath( created["filePath"]?.ToString() ?? "" ) );
            Assert.Equal( "print('file')", File.ReadAllText( expected ) );

            Assert.Equal( "Deleted macro 'filed'.", OkText( "deleteMacro", new JObject { ["name"] = "filed" } ) );
            Assert.False( File.Exists( expected ) );
            Assert.Empty( MacroManager.GetInstance().Items );
        }

        [Fact]
        public void WillKeepExplicitPyExtensionAndAbsolutePath()
        {
            string absolute = Path.Combine( _tempDir, "abs", "mine.txt" );

            Assert.Equal( absolute, Create( "Absolute", "pass", absolute )["filePath"]?.ToString() );
            Assert.True( File.Exists( absolute ) );

            string relative = Create( "Relative", "pass", "already.py" )["filePath"]?.ToString();

            Assert.EndsWith( Path.Combine( "Macros", "already.py" ), relative );
        }

        [Fact]
        public void WillListMacrosSortedAndFiltered()
        {
            Add( "Zeta" );
            Add( "alpha mining" );
            Add( "Beta Mining" );

            JArray all = JArray.Parse( OkText( "listMacros" ) );

            Assert.Equal( new[] { "alpha mining", "Beta Mining", "Zeta" }, all.Select( m => m["name"]?.ToString() ).ToArray() );

            JArray mining = JArray.Parse( OkText( "listMacros", new JObject { ["filter"] = "MINING" } ) );

            Assert.Equal( 2, mining.Count );
        }

        [Fact]
        public void WillUpdateCodeAndRename()
        {
            Add( "Old", "pass" );

            JObject renamed = Ok( "updateMacro", new JObject { ["name"] = "old", ["newName"] = "New" } );

            Assert.Equal( "New", renamed["name"]?.ToString() );
            Assert.Equal( "pass", renamed["code"]?.ToString() );

            JObject recoded = Ok( "updateMacro", new JObject { ["name"] = "New", ["code"] = "print(1)" } );

            Assert.Equal( "print(1)", recoded["code"]?.ToString() );
            Assert.Contains( "not found", Fail( "getMacro", new JObject { ["name"] = "Old" } ) );
        }

        [Fact]
        public void WillRefuseRenameOntoAnotherMacroIgnoringCase()
        {
            Add( "First" );
            Add( "Second" );

            Assert.Contains( "already exists", Fail( "updateMacro", new JObject { ["name"] = "First", ["newName"] = "SECOND" } ) );
            Assert.Equal( "First", Ok( "getMacro", new JObject { ["name"] = "first" } )["name"]?.ToString() );

            // Changing only the case of its own name is not a collision.
            Assert.Equal( "FIRST", Ok( "updateMacro", new JObject { ["name"] = "First", ["newName"] = "FIRST" } )["name"]?.ToString() );
        }

        [Theory]
        [InlineData( "getMacro" )]
        [InlineData( "updateMacro" )]
        [InlineData( "deleteMacro" )]
        [InlineData( "playMacro" )]
        [InlineData( "stopMacro" )]
        [InlineData( "getMacroStatus" )]
        [InlineData( "waitForMacro" )]
        public void WillReportUnknownMacroByName( string tool )
        {
            Assert.Contains( "'Ghost' not found", Fail( tool, new JObject { ["name"] = "Ghost" } ) );
        }

        [Fact]
        public void WillRunMacroToCompletionAndReportSuccess()
        {
            Add( "Adder", "if args[0] != 'hello' or len(args) != 2:\n    raise Exception('bad args')\n" );

            JObject result = Ok( "playMacro", new JObject
            {
                ["name"] = "adder", ["args"] = new JArray( "hello", "world" ), ["waitMs"] = 20000
            } );

            Assert.True( result["success"]?.ToObject<bool>(), result.ToString() );
            Assert.False( result["isRunning"]?.ToObject<bool>() );
            Assert.Equal( JTokenType.Null, result["error"]?.Type );
        }

        [Fact]
        public void WillReportPythonErrorWithTypeMessageAndLine()
        {
            Add( "Broken", "x = 1\nraise ValueError('boom at two')\n" );

            JObject result = Ok( "playMacro", new JObject { ["name"] = "Broken", ["waitMs"] = 20000 } );

            Assert.False( result["success"]?.ToObject<bool>() );
            Assert.Equal( "boom at two", result["error"]?["message"]?.ToString() );
            Assert.False( string.IsNullOrEmpty( result["error"]?["type"]?.ToString() ) );
            Assert.Equal( 2, result["error"]?["line"]?.ToObject<int>() );

            JObject status = (JObject) Assert.Single( JArray.Parse( OkText( "getMacroStatus", new JObject { ["name"] = "broken" } ) ) );

            Assert.Equal( "boom at two", status["lastException"]?.ToString() );
        }

        [Fact]
        public void WillReportSyntaxErrorLine()
        {
            Add( "Typo", "x = 1\ny = 2\nif x ==\n" );

            JObject result = Ok( "playMacro", new JObject { ["name"] = "Typo", ["waitMs"] = 20000 } );

            Assert.False( result["success"]?.ToObject<bool>() );
            Assert.Equal( 3, result["error"]?["line"]?.ToObject<int>() );
        }

        /// <summary>
        ///     The life of a loop macro as an agent sees it: still running after playMacro's wait (not a
        ///     success), visible in the running list and as the current macro, a bounded waitForMacro times
        ///     out, and after stopMacro the wait is met.
        /// </summary>
        [Fact]
        public void WillTrackLoopMacroUntilStopped()
        {
            Add( "Looper", LOOP );

            JObject played = Ok( "playMacro", new JObject { ["name"] = "Looper", ["waitMs"] = 200 } );

            Assert.True( played["isRunning"]?.ToObject<bool>() );
            Assert.False( played["success"]?.ToObject<bool>() );

            JObject running = Ok( "getRunningMacros" );
            JObject looper = (JObject) Assert.Single( (JArray) running["macros"] );

            Assert.Equal( 1, running["runningCount"]?.ToObject<int>() );
            Assert.Equal( "Looper", looper["name"]?.ToString() );
            Assert.NotEqual( JTokenType.Null, looper["startedOn"]?.Type );

            JObject current = Ok( "getCurrentMacro" );

            Assert.True( current["hasCurrent"]?.ToObject<bool>() );
            Assert.Equal( "Looper", current["name"]?.ToString() );

            JObject timedOut = Ok( "waitForMacro", new JObject { ["timeoutMs"] = 100 } );

            Assert.True( timedOut["timedOut"]?.ToObject<bool>() );
            Assert.True( timedOut["isRunning"]?.ToObject<bool>() );

            Assert.Equal( "Stopped macro 'looper'.", OkText( "stopMacro", new JObject { ["name"] = "looper" } ) );

            JObject finished = Ok( "waitForMacro", new JObject { ["name"] = "Looper", ["timeoutMs"] = 10000 } );

            Assert.True( finished["met"]?.ToObject<bool>() );
            Assert.False( finished["isRunning"]?.ToObject<bool>() );
            Assert.Equal( 0, Ok( "getRunningMacros" )["runningCount"]?.ToObject<int>() );
        }

        [Fact]
        public void WillStopCurrentMacroWhenNoNameIsGiven()
        {
            Add( "Looper", LOOP );

            Ok( "playMacro", new JObject { ["name"] = "Looper", ["waitMs"] = 0 } );

            Assert.Equal( "Stopped current macro.", OkText( "stopMacro" ) );
            Assert.False( MacroManager.GetInstance().Items.Single().IsRunning );
        }

        [Fact]
        public void WillStopRunningMacroWhenItIsDeleted()
        {
            MacroEntry looper = Add( "Looper", LOOP );

            Ok( "playMacro", new JObject { ["name"] = "Looper", ["waitMs"] = 0 } );
            OkText( "deleteMacro", new JObject { ["name"] = "Looper" } );

            Assert.False( looper.IsRunning );
            Assert.Empty( MacroManager.GetInstance().Items );

            looper.MacroInvoker.Thread?.Join( 5000 );
        }

        [Fact]
        public void WillStopAllRunningMacros()
        {
            MacroEntry a = Add( "A", background: true, running: true );
            MacroEntry b = Add( "B", background: true, running: true );

            Assert.Equal( "Stopped all macros.", OkText( "stopAllMacros" ) );
            Assert.False( a.IsRunning );
            Assert.False( b.IsRunning );
        }

        [Fact]
        public void WillReportStatusOfAllMacrosSorted()
        {
            Add( "b-idle" );
            Add( "a-busy", background: true, running: true );

            JArray status = JArray.Parse( OkText( "getMacroStatus" ) );

            Assert.Equal( new[] { "a-busy", "b-idle" }, status.Select( s => s["name"]?.ToString() ).ToArray() );
            Assert.True( status[0]["isRunning"]?.ToObject<bool>() );
            Assert.False( status[1]["isRunning"]?.ToObject<bool>() );
            Assert.Equal( JTokenType.Null, status[1]["error"]?.Type );
        }

        [Fact]
        public void WillReportNoCurrentMacro()
        {
            Assert.False( Ok( "getCurrentMacro" )["hasCurrent"]?.ToObject<bool>() );
        }

        [Fact]
        public void WillWaitOnTheOnlyRunningBackgroundMacro()
        {
            Add( "Idle" );
            Add( "Background", background: true, running: true );

            JObject result = Ok( "waitForMacro", new JObject { ["timeoutMs"] = 0 } );

            Assert.Equal( "Background", result["name"]?.ToString() );
            Assert.True( result["timedOut"]?.ToObject<bool>() );
        }

        [Fact]
        public void WillRefuseToGuessBetweenSeveralRunningMacros()
        {
            Add( "One", background: true, running: true );
            Add( "Two", background: true, running: true );

            Assert.Contains( "several are", Fail( "waitForMacro" ) );
        }

        [Fact]
        public void WillRejectUnknownWaitCondition()
        {
            Add( "Idle" );

            Assert.Contains( "Invalid 'until' value 'stopped'", Fail( "waitForMacro", new JObject { ["name"] = "Idle", ["until"] = "stopped" } ) );
        }

        [Theory]
        [InlineData( "PAUSED", "paused" )]
        [InlineData( "Error", "error" )]
        [InlineData( null, "finished" )]
        public void WillTreatAStoppedMacroAsMeetingEveryCondition( string until, string condition )
        {
            Add( "Idle" );

            JObject args = new JObject { ["name"] = "Idle", ["timeoutMs"] = 5000 };

            if ( until != null )
            {
                args["until"] = until;
            }

            JObject result = Ok( "waitForMacro", args );

            Assert.Equal( condition, result["condition"]?.ToString() );
            Assert.True( result["met"]?.ToObject<bool>() );
            Assert.False( result["timedOut"]?.ToObject<bool>() );
            Assert.True( result["success"]?.ToObject<bool>() );
        }

        [Theory]
        [InlineData( 1000000000, 295000 )]
        [InlineData( -50, 0 )]
        public void WillClampWaitTimeoutToTheRequestTimeout( int requested, int effective )
        {
            Add( "Idle" );

            JObject result = Ok( "waitForMacro", new JObject { ["name"] = "Idle", ["timeoutMs"] = requested } );

            Assert.Equal( requested, result["requestedTimeoutMs"]?.ToObject<int>() );
            Assert.Equal( effective, result["timeoutMs"]?.ToObject<int>() );
            Assert.True( effective < McpServer.RequestTimeoutSeconds * 1000 );
        }
    }
}
