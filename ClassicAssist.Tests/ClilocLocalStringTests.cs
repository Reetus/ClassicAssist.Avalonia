using System.Collections.Generic;
using ClassicAssist.UO.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Pins the behaviour of <see cref="Cliloc.GetLocalString" /> after it was rewritten to substitute
    ///     in one pass instead of a <c>string.Replace</c> per placeholder: repeated tokens must share an
    ///     argument, spare arguments are ignored, and a lone <c>~</c> is left alone.
    /// </summary>
    [TestClass]
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

        [TestMethod]
        public void SubstitutesSinglePlaceholder()
        {
            Initialize();

            Assert.AreEqual( "Sword of 10", Cliloc.GetLocalString( 100, ["10"] ) );
        }

        [TestMethod]
        public void SubstitutesMultiplePlaceholdersInOrder()
        {
            Initialize();

            Assert.AreEqual( "Bob hits for 5", Cliloc.GetLocalString( 101, ["Bob", "5"] ) );
        }

        [TestMethod]
        public void RepeatedTokenSharesOneArgument()
        {
            Initialize();

            Assert.AreEqual( "x and x plus y", Cliloc.GetLocalString( 102, ["x", "y"] ) );
        }

        [TestMethod]
        public void SpareArgumentsAreIgnored()
        {
            Initialize();

            Assert.AreEqual( "value 7", Cliloc.GetLocalString( 103, ["7", "extra"] ) );
        }

        [TestMethod]
        public void NoPlaceholderReturnsText()
        {
            Initialize();

            Assert.AreEqual( "plain text", Cliloc.GetLocalString( 104, ["x"] ) );
        }

        [TestMethod]
        public void ExpandsArgumentsEvenWithoutPlaceholders()
        {
            Initialize();

            // The text has no ~placeholder~, but callers read the expanded arguments off
            // Property.Arguments (autoloot's skill-bonus match reads Arguments[0]/[1] directly), so the
            // # tokens must still be resolved - the old loop expanded the first argument before giving
            // up on the missing placeholder.
            string[] arguments = ["#500000", "5"];

            Assert.AreEqual( "plain text", Cliloc.GetLocalString( 104, arguments ) );
            Assert.AreEqual( "World", arguments[0] );
        }

        [TestMethod]
        public void LoneTildeIsLeftAlone()
        {
            Initialize();

            Assert.AreEqual( "a ~ b", Cliloc.GetLocalString( 105, ["x"] ) );
        }

        [TestMethod]
        public void ArgumentContainingTildeUsesFallback()
        {
            Initialize();

            // The old loop re-scanned the inserted argument, so ~2_B~ was replaced by the next argument.
            Assert.AreEqual( "Z", Cliloc.GetLocalString( 106, ["~2_B~", "Z"] ) );
        }

        [TestMethod]
        public void ExpandsHashTokens()
        {
            Initialize();

            Assert.AreEqual( "World", Cliloc.GetLocalString( "#500000" ) );
            Assert.AreEqual( "say Hello", Cliloc.GetLocalString( "say #200" ) );
        }
    }
}
