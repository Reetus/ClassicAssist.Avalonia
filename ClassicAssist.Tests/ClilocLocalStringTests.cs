using System.Collections.Generic;
using ClassicAssist.UO.Data;
using Xunit;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Pins the behaviour of <see cref="Cliloc.GetLocalString" /> after it was rewritten to substitute
    ///     in one pass instead of a <c>string.Replace</c> per placeholder: repeated tokens must share an
    ///     argument, spare arguments are ignored, and a lone <c>~</c> is left alone.
    /// </summary>
    public class ClilocLocalStringTests
    {
        private static void Initialize()
        {
            Cliloc.Initialize( () => new Dictionary<int, string>
            {
                { 100, "Sword of ~1_DAMAGE~" },
                { 101, "~1_NAME~ hits for ~2_DMG~" },
                { 102, "~1_A~ and ~1_A~ plus ~2_B~" },
                { 103, "value ~1_V~" },
                { 104, "plain text" },
                { 105, "a ~ b" },
                { 106, "~1_A~" },
                { 200, "Hello" },
                { 500000, "World" }
            } );
        }

        [Fact]
        public void SubstitutesSinglePlaceholder()
        {
            Initialize();

            Assert.Equal( "Sword of 10", Cliloc.GetLocalString( 100, ["10"] ) );
        }

        [Fact]
        public void SubstitutesMultiplePlaceholdersInOrder()
        {
            Initialize();

            Assert.Equal( "Bob hits for 5", Cliloc.GetLocalString( 101, ["Bob", "5"] ) );
        }

        [Fact]
        public void RepeatedTokenSharesOneArgument()
        {
            Initialize();

            Assert.Equal( "x and x plus y", Cliloc.GetLocalString( 102, ["x", "y"] ) );
        }

        [Fact]
        public void SpareArgumentsAreIgnored()
        {
            Initialize();

            Assert.Equal( "value 7", Cliloc.GetLocalString( 103, ["7", "extra"] ) );
        }

        [Fact]
        public void NoPlaceholderReturnsText()
        {
            Initialize();

            Assert.Equal( "plain text", Cliloc.GetLocalString( 104, ["x"] ) );
        }

        [Fact]
        public void ExpandsArgumentsEvenWithoutPlaceholders()
        {
            Initialize();

            // The text has no ~placeholder~, but callers read the expanded arguments off
            // Property.Arguments (autoloot's skill-bonus match reads Arguments[0]/[1] directly), so the
            // # tokens must still be resolved - the old loop expanded the first argument before giving
            // up on the missing placeholder.
            string[] arguments = ["#500000", "5"];

            Assert.Equal( "plain text", Cliloc.GetLocalString( 104, arguments ) );
            Assert.Equal( "World", arguments[0] );
        }

        [Fact]
        public void LoneTildeIsLeftAlone()
        {
            Initialize();

            Assert.Equal( "a ~ b", Cliloc.GetLocalString( 105, ["x"] ) );
        }

        [Fact]
        public void ArgumentContainingTildeUsesFallback()
        {
            Initialize();

            // The old loop re-scanned the inserted argument, so ~2_B~ was replaced by the next argument.
            Assert.Equal( "Z", Cliloc.GetLocalString( 106, ["~2_B~", "Z"] ) );
        }

        [Fact]
        public void ExpandsHashTokens()
        {
            Initialize();

            Assert.Equal( "World", Cliloc.GetLocalString( "#500000" ) );
            Assert.Equal( "say Hello", Cliloc.GetLocalString( "say #200" ) );
        }
    }
}
