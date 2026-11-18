using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ClassicAssist.Data.Macros;
using Microsoft.Scripting.Hosting;
using Xunit;

namespace ClassicAssist.Tests
{
    public class MacroOutputEncodingTests
    {
        /// <summary>
        ///     sys.stdout's encoding must come from MacroOutputEncoding, not the console: with no console
        ///     and redirected std handles (the client started by a launcher that captures its output),
        ///     Console.Out reports code page 0 and every print fails with "unknown encoding: codepage___0".
        /// </summary>
        [Fact]
        public void StdoutUsesMacroOutputEncodingNotConsole()
        {
            TextWriter oldOut = Console.Out;
            TextWriter oldError = Console.Error;

            try
            {
                // What .NET hands out in that situation: a writer whose encoding is code page 0.
                Console.SetOut( new CodePageZeroWriter() );
                Console.SetError( new CodePageZeroWriter() );

                ScriptEngine engine = MacroInvoker.CreateEngine();
                ScriptScope scope = engine.CreateScope();

                // print itself needs the stdlib (codecs), which only the app ships in Modules/, so check
                // the names print's codec lookup would use: IronPython's normalized names for
                // Encoding.Unicode / Encoding.UTF8.
                string expected = RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) ? "utf_16_le" : "utf_8";

                engine.Execute( "import sys\nout = sys.stdout.encoding\nerr = sys.stderr.encoding", scope );

                Assert.Equal( expected, scope.GetVariable<string>( "out" ) );
                Assert.Equal( expected, scope.GetVariable<string>( "err" ) );
            }
            finally
            {
                Console.SetOut( oldOut );
                Console.SetError( oldError );
            }
        }

        private class CodePageZeroWriter : StringWriter
        {
            public override Encoding Encoding { get; } = new CodePageZeroEncoding();
        }

        private class CodePageZeroEncoding : UTF8Encoding
        {
            public override int CodePage => 0;
            public override string EncodingName => "Codepage - 0";
            public override string WebName => "Codepage - 0";
        }
    }
}
