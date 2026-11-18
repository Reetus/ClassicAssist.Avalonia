using System.Collections.Generic;
using System.Linq;
using ClassicAssist.Data.Macros;
using ClassicAssist.UO.Objects;
using IronPython.Runtime;
using Xunit;

namespace ClassicAssist.Tests.Macros
{
    /// <summary>
    ///     <see cref="MacroInvoker.GetDisplayValue" /> renders a paused macro's variables for the debugger
    ///     view and for "copy value". The view truncates collections so a 10,000-item list cannot hang the
    ///     UI; the clipboard copy must not truncate, and must render entities as a serial the macro can
    ///     paste back in.
    /// </summary>
    public class MacroDisplayValueTests
    {
        [Fact]
        public void WillRenderScalarsAsPythonWould()
        {
            Assert.Equal( "None", MacroInvoker.GetDisplayValue( null ) );
            Assert.Equal( "\"text\"", MacroInvoker.GetDisplayValue( "text" ) );
            Assert.Equal( "True", MacroInvoker.GetDisplayValue( true ) );
            Assert.Equal( "False", MacroInvoker.GetDisplayValue( false ) );
            Assert.Equal( "42", MacroInvoker.GetDisplayValue( 42 ) );
        }

        [Fact]
        public void WillRenderEntityWithNameForDisplayAndSerialForClipboard()
        {
            Item item = new Item( 0x40001234 ) { Name = "Dagger" };

            Assert.Equal( "Dagger (0x40001234)", MacroInvoker.GetDisplayValue( item ) );
            Assert.Equal( "0x40001234", MacroInvoker.GetDisplayValue( item, true ) );
        }

        [Fact]
        public void WillTruncateListsForDisplayButNotForClipboard()
        {
            List<int> values = Enumerable.Range( 1, 12 ).ToList();

            Assert.Equal( "[1, 2, 3, 4, 5, 6, 7, 8, 9, 10]", MacroInvoker.GetDisplayValue( values ) );
            Assert.Equal( "[1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]", MacroInvoker.GetDisplayValue( values, true ) );
        }

        [Fact]
        public void WillRenderNestedValues()
        {
            List<object> values = new List<object> { "a", null, new List<object> { true } };

            Assert.Equal( "[\"a\", None, [True]]", MacroInvoker.GetDisplayValue( values ) );
        }

        [Fact]
        public void WillTruncateDictionariesForDisplayButNotForClipboard()
        {
            PythonDictionary dictionary = new PythonDictionary();

            for ( int i = 1; i <= 6; i++ )
            {
                dictionary[$"k{i}"] = i;
            }

            // Entry order is the dictionary's, which is not insertion order here; check the shape.
            string display = MacroInvoker.GetDisplayValue( dictionary );
            string clipboard = MacroInvoker.GetDisplayValue( dictionary, true );

            Assert.StartsWith( "{", display );
            Assert.EndsWith( ", ...}", display );
            Assert.Equal( 5, display.Split( ": " ).Length - 1 );

            Assert.StartsWith( "{", clipboard );
            Assert.EndsWith( "}", clipboard );
            Assert.DoesNotContain( "...", clipboard );

            for ( int i = 1; i <= 6; i++ )
            {
                Assert.Contains( $"\"k{i}\": {i}", clipboard );
            }
        }

        [Fact]
        public void WontAddEllipsisToASmallDictionary()
        {
            PythonDictionary dictionary = new PythonDictionary { ["only"] = 1 };

            Assert.Equal( "{\"only\": 1}", MacroInvoker.GetDisplayValue( dictionary ) );
        }

        [Fact]
        public void WillFallBackToToStringForOtherObjects()
        {
            Assert.Equal( "custom", MacroInvoker.GetDisplayValue( new Custom() ) );
        }

        private class Custom
        {
            public override string ToString()
            {
                return "custom";
            }
        }
    }
}
