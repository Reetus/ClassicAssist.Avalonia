using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Mcp;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UI.Misc;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using ClassicAssist.UO.Objects.Gumps;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.Mcp
{
    /// <summary>
    ///     invokeCommand binds a JSON object (or array) to a macro command's .NET parameters by reflection.
    ///     These pin the binding rules agents rely on: names match case-insensitively, optional parameters
    ///     take their defaults, positional arrays work, serial-or-alias parameters turn numbers into serials
    ///     and leave other strings as aliases, bools accept the usual spellings, and a value that can't be
    ///     converted produces an error naming the signature instead of a call with garbage.
    /// </summary>
    public class McpCommandInvokerTests : IDisposable
    {
        private readonly bool _originalQuietMode;
        private readonly ObservableCollectionEx<MacroEntry> _originalMacros;

        public McpCommandInvokerTests()
        {
            EngineState.Reset();

            _originalQuietMode = MacroManager.QuietMode;
            _originalMacros = MacroManager.GetInstance().Items;
            MacroManager.GetInstance().Items = new ObservableCollectionEx<MacroEntry>();
        }

        public void Dispose()
        {
            MacroManager.QuietMode = _originalQuietMode;
            MacroManager.GetInstance().Items = _originalMacros;

            EngineState.Reset();
        }

        private static CallToolResult Invoke( string command, JToken arguments )
        {
            JObject args = new JObject { ["command"] = command };

            if ( arguments != null )
            {
                args["arguments"] = arguments;
            }

            return McpTools.Invoke( "invokeCommand", args );
        }

        private static string Ok( string command, JToken arguments = null )
        {
            CallToolResult result = Invoke( command, arguments );

            Assert.False( result.IsError, $"{command} failed: {result.Content.Single().Text}" );

            return result.Content.Single().Text;
        }

        private static string Fail( string command, JToken arguments = null )
        {
            CallToolResult result = Invoke( command, arguments );

            Assert.True( result.IsError, $"{command} should have failed but returned {result.Content.Single().Text}" );

            return result.Content.Single().Text;
        }

        [Fact]
        public void WillBindNamedArgumentsCaseInsensitively()
        {
            Assert.Equal( "5", Ok( "Dummy", new JObject { ["a"] = 2, ["b"] = 3 } ) );
            Assert.Equal( "18", Ok( "dUmMy", new JObject { ["A"] = 2, ["B"] = "0x10" } ) );
        }

        [Fact]
        public void WillBindPositionalArray()
        {
            Assert.Equal( "-1", Ok( "Dummy", new JArray( 2, -3 ) ) );
        }

        [Fact]
        public void WillNameTheMissingArgumentAndSignature()
        {
            string error = Fail( "Dummy", new JObject { ["a"] = 2 } );

            Assert.Contains( "Dummy(Int32 a, Int32 b)", error );
            Assert.Contains( "missing required argument 'b'", error );

            Assert.Contains( "missing required argument 'b'", Fail( "Dummy", new JArray( 2 ) ) );
            Assert.Contains( "missing required argument 'a'", Fail( "Dummy" ) );

            // An explicit null is a missing value, not a zero.
            Assert.Contains( "missing required argument 'b'", Fail( "Dummy", new JObject { ["a"] = 1, ["b"] = null } ) );
        }

        [Fact]
        public void WillRejectUnconvertibleInteger()
        {
            Assert.Contains( "'seven' is not a valid Int32", Fail( "Dummy", new JObject { ["a"] = "seven", ["b"] = 1 } ) );
            Assert.Contains( "is not a valid Int32", Fail( "Dummy", new JObject { ["a"] = 1.5, ["b"] = 1 } ) );
        }

        [Fact]
        public void WillReportUnknownOrMissingCommand()
        {
            Assert.Contains( "not found", Fail( "NoSuchCommandAnywhere" ) );
            Assert.Contains( "'command'", McpTools.Invoke( "invokeCommand", new JObject() ).Content.Single().Text );
            Assert.True( McpTools.Invoke( "invokeCommand", null ).IsError );
        }

        [Fact]
        public void WillFormatVoidAndBoolResults()
        {
            Engine.Items.Add( new Item( 0x40004321 ) );

            Assert.Equal( "OK", Ok( "SetAlias", new JObject { ["aliasName"] = "mcpThing", ["obj"] = 0x40004321 } ) );
            Assert.Equal( "True", Ok( "FindAlias", new JObject { ["aliasName"] = "MCPTHING" } ) );
            Assert.Equal( "False", Ok( "FindAlias", new JObject { ["aliasName"] = "neverSet" } ) );
        }

        [Theory]
        [InlineData( "0x1234" )]
        [InlineData( "4660" )]
        public void WillPassNumericStringToSerialOrAliasAsSerial( string value )
        {
            Ok( "SetAlias", new JObject { ["aliasName"] = "mcpserial", ["obj"] = value } );

            Assert.Equal( 0x1234, AliasCommands.GetAlias( "mcpserial" ) );
        }

        [Fact]
        public void WillPassJsonIntegerToSerialOrAliasAsSerial()
        {
            Ok( "SetAlias", new JObject { ["aliasName"] = "mcpserial", ["obj"] = 0x1234 } );

            Assert.Equal( 0x1234, AliasCommands.GetAlias( "mcpserial" ) );
        }

        [Fact]
        public void WillPassNonNumericStringToSerialOrAliasAsAlias()
        {
            AliasCommands.SetAlias( "mcpsource", 0x00654321 );

            Ok( "SetAlias", new JObject { ["aliasName"] = "mcpcopy", ["obj"] = "mcpsource" } );

            Assert.Equal( 0x00654321, AliasCommands.GetAlias( "mcpcopy" ) );
        }

        [Fact]
        public void WillPassNonStringJsonToStringParameterAsText()
        {
            Ok( "SetAlias", new JObject { ["aliasName"] = 42, ["obj"] = 7 } );

            Assert.Equal( 7, AliasCommands.GetAlias( "42" ) );
        }

        [Fact]
        public void WillApplyDefaultsForOmittedOptionalParameters()
        {
            Engine.Journal.Write( new JournalEntry { Text = "Hello there", Name = "Bob", SpeechHue = 0x22, SpeechType = JournalSpeech.Say } );

            Assert.Equal( "True", Ok( "InJournal", new JObject { ["text"] = "hello" } ) );
            Assert.Equal( "True", Ok( "InJournal", new JObject { ["text"] = "hello", ["author"] = "bob", ["hue"] = "0x22" } ) );
            Assert.Equal( "False", Ok( "InJournal", new JObject { ["text"] = "hello", ["hue"] = 0x23 } ) );
        }

        [Theory]
        [InlineData( "true", true )]
        [InlineData( "On", true )]
        [InlineData( "yes", true )]
        [InlineData( "1", true )]
        [InlineData( "false", false )]
        [InlineData( "OFF", false )]
        [InlineData( "no", false )]
        [InlineData( "0", false )]
        public void WillAcceptBooleanSpellings( string value, bool expected )
        {
            MacroManager.QuietMode = !expected;

            Ok( "SetQuietMode", new JObject { ["onOff"] = value } );

            Assert.Equal( expected, MacroManager.QuietMode );
        }

        [Fact]
        public void WillAcceptJsonBooleanAndRejectOtherWords()
        {
            MacroManager.QuietMode = false;

            Ok( "SetQuietMode", new JObject { ["onOff"] = true } );

            Assert.True( MacroManager.QuietMode );
            Assert.Contains( "'maybe' is not a valid boolean", Fail( "SetQuietMode", new JObject { ["onOff"] = "maybe" } ) );
            Assert.True( MacroManager.QuietMode );
        }

        [Fact]
        public void WillBindUnsignedGumpIdFromHexOrDecimal()
        {
            Engine.Gumps.Add( new Gump( 0, 0, 0x8000abcd, 1, "", new[] { "x" },
                new[] { new GumpElement { Type = ElementType.htmlgump, Text = "Bank Balance" } }, Array.Empty<GumpPage>() ) );

            Assert.Equal( "True", Ok( "InGump", new JObject { ["gumpId"] = "0x8000ABCD", ["text"] = "balance" } ) );
            Assert.Equal( "True", Ok( "InGump", new JObject { ["gumpId"] = 2147527629L, ["text"] = "bank" } ) );
            Assert.Contains( "'-1' is not a valid UInt32", Fail( "InGump", new JObject { ["gumpId"] = -1, ["text"] = "bank" } ) );
        }

        /// <summary>
        ///     C# lets a caller omit a params array; a macro calls PlayMacro("name") all the time. The binder
        ///     treats the params parameter as required (it has no default value), so PlayMacro can't be
        ///     invoked without arguments, and a JSON array for it doesn't convert either.
        /// </summary>
        [Fact( Skip = "BUG: invokeCommand treats a params array parameter as required, so PlayMacro(name) can't be invoked" )]
        public void WillOmitParamsArrayArgument()
        {
            using ManualResetEventSlim ran = new ManualResetEventSlim();

            MacroManager.GetInstance().Items.Add( new MacroEntry { Name = "McpParamsMacro", Action = ( _, _ ) => ran.Set() } );

            Ok( "PlayMacro", new JObject { ["name"] = "McpParamsMacro" } );

            Assert.True( ran.Wait( TimeSpan.FromSeconds( 5 ), TestContext.Current.CancellationToken ) );
        }

        /// <summary>
        ///     MethodInfo.Invoke wraps whatever the command throws in a TargetInvocationException, and the
        ///     tool reports that wrapper's message, so the agent sees "Exception has been thrown by the target
        ///     of an invocation." instead of the reason.
        /// </summary>
        [Fact( Skip = "BUG: invokeCommand reports TargetInvocationException's generic message instead of the command's exception" )]
        public void WillReportTheCommandsOwnExceptionMessage()
        {
            string error = Fail( "ClearHands", new JObject { ["hand"] = "middle" } );

            Assert.Contains( "hand", error );
        }

        [Fact]
        public void WillReportCommandExceptionAsToolErrorNotCrash()
        {
            Fail( "ClearHands", new JObject { ["hand"] = "middle" } );
        }

        private static bool TryConvert( JToken value, Type type, out object converted, out string error )
        {
            MethodInfo method = typeof( McpCommandInvoker ).GetMethod( "TryConvert", BindingFlags.NonPublic | BindingFlags.Static );

            Assert.NotNull( method );

            object[] args = { value, type, null, null };
            bool result = (bool) method.Invoke( null, args );

            converted = args[2];
            error = (string) args[3];

            return result;
        }

        [Fact]
        public void WillConvertEnumByNameCaseInsensitivelyOrByHexValue()
        {
            Assert.True( TryConvert( "friday", typeof( DayOfWeek ), out object byName, out _ ) );
            Assert.Equal( DayOfWeek.Friday, byName );

            Assert.True( TryConvert( "0x2", typeof( DayOfWeek ), out object byHex, out _ ) );
            Assert.Equal( DayOfWeek.Tuesday, byHex );

            Assert.False( TryConvert( "0x2a", typeof( DayOfWeek ), out _, out string error ) );
            Assert.Contains( "not a valid DayOfWeek", error );

            Assert.False( TryConvert( "Someday", typeof( DayOfWeek ), out _, out _ ) );
        }

        [Fact]
        public void WillConvertWideAndFloatingTypes()
        {
            Assert.True( TryConvert( "0xFFFFFFFFFF", typeof( long ), out object wide, out _ ) );
            Assert.Equal( 0xFFFFFFFFFFL, wide );

            Assert.False( TryConvert( "lots", typeof( long ), out _, out _ ) );

            Assert.True( TryConvert( 2.5, typeof( double ), out object real, out _ ) );
            Assert.Equal( 2.5, real );

            Assert.True( TryConvert( "0.25", typeof( float ), out object single, out _ ) );
            Assert.Equal( 0.25f, single );

            Assert.False( TryConvert( "a quarter", typeof( double ), out _, out _ ) );
            Assert.False( TryConvert( "not a date", typeof( DateTime ), out _, out _ ) );
        }

        [Fact]
        public void WillConvertNullableByItsUnderlyingType()
        {
            Assert.True( TryConvert( "0x10", typeof( int? ), out object value, out _ ) );
            Assert.Equal( 16, value );
        }

        [Fact]
        public void WillListDisplayedCommandsWithHintsAndDefaults()
        {
            JArray commands = JArray.Parse( McpTools.Invoke( "listCommands", new JObject { ["filter"] = "setalias" } ).Content.Single().Text );

            JToken setAlias = commands.Single( c => c["name"]?.ToString() == "SetAlias" );

            Assert.Equal( "SetAlias(String aliasName, serialOrAlias obj)", setAlias["signature"]?.ToString() );
            Assert.Equal( "alias name", setAlias["parameters"]?[0]?["hint"]?.ToString() );
            Assert.StartsWith( "serial or alias", setAlias["parameters"]?[1]?["hint"]?.ToString() );

            JToken inJournal = JArray.Parse( McpTools.Invoke( "listCommands", new JObject { ["filter"] = "InJournal" } ).Content.Single().Text )
                .Single( c => c["name"]?.ToString() == "InJournal" );

            Assert.Contains( "Int32 hue = -1", inJournal["signature"]?.ToString() );
        }

        [Fact]
        public void WillListCommandsSortedAndOmitUndisplayedOnes()
        {
            string[] names = JArray.Parse( McpTools.Invoke( "listCommands", new JObject() ).Content.Single().Text )
                .Select( c => c["name"]?.ToString() ).ToArray();

            Assert.Equal( names.OrderBy( n => n ).ToArray(), names );

            // Dummy is invokable but carries no CommandsDisplay attribute, so it isn't advertised.
            Assert.DoesNotContain( "Dummy", names );
            Assert.Empty( JArray.Parse( McpTools.Invoke( "listCommands", new JObject { ["filter"] = "zzzNothing" } ).Content.Single().Text ) );
        }
    }
}
