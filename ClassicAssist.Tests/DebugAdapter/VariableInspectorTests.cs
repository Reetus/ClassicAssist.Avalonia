using System;
using System.Collections.Generic;
using System.Linq;
using ClassicAssist.DebugAdapter;
using ClassicAssist.DebugAdapter.Dap;
using ClassicAssist.UO.Objects;
using IronPython.Runtime.Exceptions;
using Microsoft.Scripting.Hosting;
using Xunit;

namespace ClassicAssist.Tests.DebugAdapter
{
    /// <summary>
    ///     <see cref="VariableInspector" /> is what the editor's Variables pane, hover and debug console
    ///     see: frame ids map to (thread, depth), each frame gets locals/globals references, and values are
    ///     rendered with Python repr and expanded lazily through further references. These pin the
    ///     rendering of the common Python values, what is hidden, how containers expand (including
    ///     self-referencing ones), evaluation against a paused frame and writing a value back into it.
    /// </summary>
    public class VariableInspectorTests
    {
        private const int THREAD = 7;
        private const int TOP_FRAME = THREAD * 1000;

        private const string VALUES_SOURCE =
            "import sys\n" + // 1
            "def helper():\n" + // 2
            "    pass\n" + // 3
            "number = 42\n" + // 4
            "text = 'hi'\n" + // 5
            "nothing = None\n" + // 6
            "items = [1, 'two', None]\n" + // 7
            "mapping = {'b': 2, 'a': [1]}\n" + // 8
            "pair = (1, 2)\n" + // 9
            "flag = True\n" + // 10
            "end = 0\n"; // 11

        private readonly VariableInspector _inspector = new VariableInspector();

        private static DapVariable Find( IEnumerable<DapVariable> variables, string name )
        {
            return Assert.Single( variables, v => v.Name == name );
        }

        private DapVariable[] ModuleLocalsAtEnd( string source, int line, out TraceBackFrame captured )
        {
            DapVariable[] result = null;
            TraceBackFrame frame = null;

            PythonFrames.RunToLine( source, line, f =>
            {
                frame = f;
                _inspector.StoreFrames( THREAD, f );
                result = _inspector.GetVariables( _inspector.GetLocalsRef( TOP_FRAME ) );
            } );

            captured = frame;

            return result;
        }

        [Fact]
        public void WillRenderValuesWithPythonRepr()
        {
            DapVariable[] variables = ModuleLocalsAtEnd( VALUES_SOURCE, 11, out _ );

            Assert.Equal( "42", Find( variables, "number" ).Value );
            Assert.Equal( "Int32", Find( variables, "number" ).Type );
            Assert.Equal( "'hi'", Find( variables, "text" ).Value );
            Assert.Equal( "None", Find( variables, "nothing" ).Value );
            Assert.Equal( "None", Find( variables, "nothing" ).Type );
            Assert.Equal( "True", Find( variables, "flag" ).Value );
            Assert.Equal( "[1, 'two', None]", Find( variables, "items" ).Value );
            Assert.Equal( "(1, 2)", Find( variables, "pair" ).Value );
        }

        [Fact]
        public void WillOnlyMakeContainersExpandable()
        {
            DapVariable[] variables = ModuleLocalsAtEnd( VALUES_SOURCE, 11, out _ );

            Assert.Equal( 0, Find( variables, "number" ).VariablesReference );
            Assert.Equal( 0, Find( variables, "text" ).VariablesReference );
            Assert.Equal( 0, Find( variables, "nothing" ).VariablesReference );
            Assert.NotEqual( 0, Find( variables, "items" ).VariablesReference );
            Assert.NotEqual( 0, Find( variables, "mapping" ).VariablesReference );
            Assert.NotEqual( 0, Find( variables, "pair" ).VariablesReference );
        }

        /// <summary>
        ///     Module plumbing (__builtins__, __name__, ...), imported modules and function objects would
        ///     bury the macro's own variables, so they are left out; what remains is sorted by name.
        /// </summary>
        [Fact]
        public void WillHideModulesFunctionsAndDunderGlobalsAndSortByName()
        {
            DapVariable[] variables = ModuleLocalsAtEnd( VALUES_SOURCE, 11, out _ );
            List<string> names = variables.Select( v => v.Name ).ToList();

            Assert.DoesNotContain( "sys", names );
            Assert.DoesNotContain( "helper", names );
            Assert.DoesNotContain( "__builtins__", names );
            Assert.DoesNotContain( "__name__", names );
            Assert.Equal( names.OrderBy( n => n, StringComparer.Ordinal ).ToList(), names );
        }

        [Fact]
        public void WillExpandListsByIndexInOrder()
        {
            DapVariable[] variables = ModuleLocalsAtEnd( VALUES_SOURCE, 11, out _ );

            DapVariable[] children = _inspector.GetVariables( Find( variables, "items" ).VariablesReference );

            Assert.Equal( new[] { "[0]", "[1]", "[2]" }, children.Select( c => c.Name ) );
            Assert.Equal( new[] { "1", "'two'", "None" }, children.Select( c => c.Value ) );
        }

        [Fact]
        public void WillExpandDictsByKeyAndNestFurther()
        {
            DapVariable[] variables = ModuleLocalsAtEnd( VALUES_SOURCE, 11, out _ );

            DapVariable[] children = _inspector.GetVariables( Find( variables, "mapping" ).VariablesReference );

            Assert.Equal( new[] { "a", "b" }, children.Select( c => c.Name ) );

            DapVariable[] nested = _inspector.GetVariables( Find( children, "a" ).VariablesReference );
            Assert.Equal( "1", Assert.Single( nested ).Value );
        }

        [Fact]
        public void WillExpandTuplesByIndex()
        {
            DapVariable[] variables = ModuleLocalsAtEnd( VALUES_SOURCE, 11, out _ );

            DapVariable[] children = _inspector.GetVariables( Find( variables, "pair" ).VariablesReference );

            Assert.Equal( new[] { "[0]", "[1]" }, children.Select( c => c.Name ) );
            Assert.Equal( new[] { "1", "2" }, children.Select( c => c.Value ) );
        }

        /// <summary>
        ///     A list that contains itself must render (Python's "[...]") and expand one level at a time
        ///     rather than recursing forever.
        /// </summary>
        [Fact]
        public void WillRenderAndExpandASelfReferencingList()
        {
            const string source = "loop = [1]\nloop.append(loop)\nend = 0\n";

            DapVariable[] variables = ModuleLocalsAtEnd( source, 3, out _ );
            DapVariable loop = Find( variables, "loop" );

            Assert.Equal( "[1, [...]]", loop.Value );

            DapVariable inner = _inspector.GetVariables( loop.VariablesReference )[1];
            Assert.Equal( "[1, [...]]", inner.Value );
            Assert.NotEqual( 0, inner.VariablesReference );
            Assert.Equal( 2, _inspector.GetVariables( inner.VariablesReference ).Length );
        }

        [Fact]
        public void WillExpandALargeListInFull()
        {
            DapVariable[] variables = ModuleLocalsAtEnd( "big = list(range(5000))\nend = 0\n", 2, out _ );

            DapVariable[] children = _inspector.GetVariables( Find( variables, "big" ).VariablesReference );

            Assert.Equal( 5000, children.Length );
            Assert.Equal( "[4999]", children[4999].Name );
            Assert.Equal( "4999", children[4999].Value );
        }

        /// <summary>
        ///     .NET objects a macro holds (Item, Mobile, ...) are shown by repr and are not expandable.
        /// </summary>
        [Fact]
        public void WillShowDotNetObjectsAsLeafValues()
        {
            const string source =
                "it = given\n" +
                "end = 0\n";

            DapVariable[] variables = null;

            PythonFrames.RunToLine( source, 2, frame =>
            {
                _inspector.StoreFrames( THREAD, frame );
                variables = _inspector.GetVariables( _inspector.GetLocalsRef( TOP_FRAME ) );
            }, new Dictionary<string, object> { ["given"] = new Item( 0x40000001 ) { Name = "Dagger" } } );

            DapVariable item = Find( variables, "it" );

            Assert.Equal( "Item", item.Type );
            Assert.Equal( 0, item.VariablesReference );
            Assert.False( string.IsNullOrEmpty( item.Value ) );
        }

        [Fact]
        public void WillReturnNothingForAnUnknownReference()
        {
            Assert.Empty( _inspector.GetVariables( 0 ) );
            Assert.Empty( _inspector.GetVariables( 123456 ) );
            Assert.Equal( 0, _inspector.GetLocalsRef( 999 ) );
            Assert.Equal( 0, _inspector.GetGlobalsRef( 999 ) );
        }

        /// <summary>
        ///     Frame ids are thread * 1000 + depth, matching the ids the stackTrace response hands out, so
        ///     the scopes request for the caller's frame finds the caller's variables.
        /// </summary>
        [Fact]
        public void WillMapEachStackDepthToItsOwnFrame()
        {
            const string source =
                "def inner(a):\n" + // 1
                "    b = a * 2\n" + // 2
                "    return b\n" + // 3
                "outer_value = 5\n" + // 4
                "inner(outer_value)\n"; // 5

            PythonFrames.RunToLine( source, 3, frame =>
            {
                _inspector.StoreFrames( THREAD, frame );

                DapVariable[] top = _inspector.GetVariables( _inspector.GetLocalsRef( TOP_FRAME ) );
                Assert.Equal( new[] { "a", "b" }, top.Select( v => v.Name ) );
                Assert.Equal( "10", Find( top, "b" ).Value );

                DapVariable[] caller = _inspector.GetVariables( _inspector.GetLocalsRef( TOP_FRAME + 1 ) );
                Assert.Equal( "5", Find( caller, "outer_value" ).Value );

                DapVariable[] globals = _inspector.GetVariables( _inspector.GetGlobalsRef( TOP_FRAME ) );
                Assert.Equal( "5", Find( globals, "outer_value" ).Value );

                Assert.Equal( 0, _inspector.GetLocalsRef( TOP_FRAME + 2 ) );
            } );
        }

        /// <summary>
        ///     Each stop replaces the thread's frames; references handed out for the previous stop must not
        ///     keep resolving to stale frames.
        /// </summary>
        [Fact]
        public void WillDropAThreadsOldFrameReferencesOnTheNextStop()
        {
            PythonFrames.RunToLine( VALUES_SOURCE, 11, frame =>
            {
                _inspector.StoreFrames( THREAD, frame );
                int oldLocals = _inspector.GetLocalsRef( TOP_FRAME );

                _inspector.StoreFrames( THREAD, frame );

                Assert.Empty( _inspector.GetVariables( oldLocals ) );
                Assert.NotEqual( oldLocals, _inspector.GetLocalsRef( TOP_FRAME ) );
                Assert.NotEmpty( _inspector.GetVariables( _inspector.GetLocalsRef( TOP_FRAME ) ) );
            } );
        }

        [Fact]
        public void WillForgetEverythingOnClear()
        {
            PythonFrames.RunToLine( VALUES_SOURCE, 11, frame =>
            {
                _inspector.StoreFrames( THREAD, frame );
                int locals = _inspector.GetLocalsRef( TOP_FRAME );

                _inspector.Clear();

                Assert.Empty( _inspector.GetVariables( locals ) );
                Assert.Equal( 0, _inspector.GetLocalsRef( TOP_FRAME ) );
                Assert.Throws<InvalidOperationException>( () => _inspector.Evaluate( TOP_FRAME, "number" ) );
            } );
        }

        [Fact]
        public void WillEvaluateAgainstLocalsShadowingGlobals()
        {
            const string source =
                "value = 'global'\n" + // 1
                "scale = 3\n" + // 2
                "def f(value):\n" + // 3
                "    return value\n" + // 4
                "f(7)\n"; // 5

            PythonFrames.RunToLine( source, 4, frame =>
            {
                _inspector.StoreFrames( THREAD, frame );

                Tuple<string, string, int> result = _inspector.Evaluate( TOP_FRAME, "value * scale" );

                Assert.Equal( "21", result.Item1 );
                Assert.Equal( "Int32", result.Item2 );
                Assert.Equal( 0, result.Item3 );

                Assert.Equal( "'global'", _inspector.Evaluate( TOP_FRAME + 1, "value" ).Item1 );
            } );
        }

        [Fact]
        public void WillMakeAnEvaluatedContainerExpandable()
        {
            PythonFrames.RunToLine( VALUES_SOURCE, 11, frame =>
            {
                _inspector.StoreFrames( THREAD, frame );

                Tuple<string, string, int> result = _inspector.Evaluate( TOP_FRAME, "[number, text]" );

                Assert.Equal( "[42, 'hi']", result.Item1 );
                Assert.NotEqual( 0, result.Item3 );
                Assert.Equal( 2, _inspector.GetVariables( result.Item3 ).Length );
            } );
        }

        [Fact]
        public void WillRefuseToEvaluateWithoutAStoppedFrame()
        {
            Assert.Throws<InvalidOperationException>( () => _inspector.Evaluate( 0, "1 + 1" ) );

            PythonFrames.RunToLine( VALUES_SOURCE, 11, frame =>
            {
                _inspector.StoreFrames( THREAD, frame );

                Assert.Throws<InvalidOperationException>( () => _inspector.Evaluate( TOP_FRAME + 5, "1" ) );
            } );
        }

        [Fact]
        public void WillSurfaceErrorsFromTheExpression()
        {
            PythonFrames.RunToLine( VALUES_SOURCE, 11, frame =>
            {
                _inspector.StoreFrames( THREAD, frame );

                Exception error = Record.Exception( () => _inspector.Evaluate( TOP_FRAME, "undefined_name + 1" ) );

                Assert.NotNull( error );
                Assert.Contains( "undefined_name", error.Message );
            } );
        }

        /// <summary>
        ///     Setting a module-level variable while paused changes what the rest of the macro computes.
        /// </summary>
        [Fact]
        public void WillWriteAModuleVariableBackIntoTheRunningMacro()
        {
            const string source = "x = 1\ny = x + 1\n";

            ScriptScope scope = PythonFrames.RunToLine( source, 2, frame =>
            {
                _inspector.StoreFrames( THREAD, frame );

                Tuple<string, string> result = _inspector.SetVariable( _inspector.GetLocalsRef( TOP_FRAME ), "x", "x * 41" );

                Assert.Equal( "41", result.Item1 );
                Assert.Equal( "Int32", result.Item2 );
            } );

            Assert.Equal( 42, scope.GetVariable<int>( "y" ) );
        }

        [Fact]
        public void WillWriteAListElementByIndex()
        {
            const string source = "items = [1, 2, 3]\nend = 0\n";

            ScriptScope scope = PythonFrames.RunToLine( source, 2, frame =>
            {
                _inspector.StoreFrames( THREAD, frame );
                int items = Find( _inspector.GetVariables( _inspector.GetLocalsRef( TOP_FRAME ) ), "items" )
                    .VariablesReference;

                _inspector.SetVariable( items, "[1]", "'two'" );

                Assert.Throws<InvalidOperationException>( () => _inspector.SetVariable( items, "[x]", "0" ) );
                Assert.Throws<InvalidOperationException>( () => _inspector.SetVariable( items, "name", "0" ) );
            } );

            Assert.Equal( "[1, 'two', 3]",
                scope.Engine.Execute<string>( "repr(items)", scope ) );
        }

        [Fact]
        public void WillRefuseToWriteIntoAnUnknownContainer()
        {
            Assert.Throws<InvalidOperationException>( () => _inspector.SetVariable( 4242, "x", "1" ) );
        }

        [Fact]
        public void WillCompleteNamesFromTheWordBeforeTheCursor()
        {
            const string source =
                "player_hits = 1\n" + // 1
                "PlayerName = 'x'\n" + // 2
                "def plan():\n" + // 3
                "    pass\n" + // 4
                "other = 2\n" + // 5
                "end = 0\n"; // 6

            PythonFrames.RunToLine( source, 6, frame =>
            {
                _inspector.StoreFrames( THREAD, frame );

                const string text = "print(pla";
                CompletionItem[] items = _inspector.GetCompletions( TOP_FRAME, text, text.Length + 1 );

                Assert.Equal( new[] { "player_hits", "PlayerName" }, items.Select( i => i.Label ) );
                Assert.All( items, i => Assert.Equal( "variable", i.Type ) );

                const string dotted = "foo.oth";
                Assert.Equal( "other", Assert.Single( _inspector.GetCompletions( TOP_FRAME, dotted, dotted.Length + 1 ) ).Label );

                Assert.Empty( _inspector.GetCompletions( TOP_FRAME + 9, text, text.Length + 1 ) );
            } );
        }
    }
}
