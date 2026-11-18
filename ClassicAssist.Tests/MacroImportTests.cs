using System;
using System.Threading;
using ClassicAssist.Data.Macros;
using ClassicAssist.UO.Objects;
using Xunit;
using Engine = ClassicAssist.Shared.Engine;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Macros written against upstream ClassicAssist say <c>from Assistant import Engine</c>, and the
    ///     macro help shipped here still tells people to. The engine lives in a different namespace in
    ///     this build, so a module on the macro search path re-exports it.
    /// </summary>
    public class MacroImportTests
    {
        private static bool RunMacro( string source, out Exception exception )
        {
            MacroInvoker invoker = new MacroInvoker();

            invoker.Execute( new MacroEntry { Macro = source, DoNotAutoInterrupt = true } );

            invoker.Thread?.Join( 15000 );

            exception = invoker.Exception;

            return !invoker.IsFaulted;
        }

        [Fact]
        public void WillImportEngineFromAssistant()
        {
            Assert.True( RunMacro( "from Assistant import Engine", out Exception e ),
                $"the compatibility module did not resolve: {e}" );
        }

        /// <summary>
        ///     Importing the name is not the point; reaching the real engine through it is.
        /// </summary>
        [Fact]
        public void WillReachTheRealEngineThroughIt()
        {
            Engine.Player = new PlayerMobile( 0x1234 ) { Name = "Testificate" };

            try
            {
                bool ok = RunMacro(
                    "from Assistant import Engine\n" +
                    "if Engine.Player.Name != 'Testificate':\n" +
                    "    raise Exception('got ' + str(Engine.Player.Name))\n",
                    out Exception e );

                Assert.True( ok, $"Engine.Player was not reachable through the shim: {e}" );
            }
            finally
            {
                Engine.Player = null;
            }
        }
    }
}
