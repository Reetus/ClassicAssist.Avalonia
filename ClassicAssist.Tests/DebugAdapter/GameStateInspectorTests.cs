using System;
using ClassicAssist.DebugAdapter;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Objects;
using Xunit;
using Engine = ClassicAssist.Shared.Engine;

namespace ClassicAssist.Tests.DebugAdapter
{
    /// <summary>
    ///     The debug console's "!props &lt;serial&gt;" command, answered from the live game state whether or
    ///     not a macro is paused. Pins serial parsing (0x-hex, bare hex, decimal), where entities are looked
    ///     up (items, mobiles, the player) and the text it reports.
    /// </summary>
    public class GameStateInspectorTests
    {
        public GameStateInspectorTests()
        {
            EngineState.Reset();
        }

        [Fact]
        public void WillExplainUsageWithoutASerial()
        {
            Tuple<bool, string> result = GameStateInspector.Execute( "!props" );

            Assert.True( result.Item1 );
            Assert.StartsWith( "Usage: !props <serial>", result.Item2 );
        }

        [Fact]
        public void WillRejectUnknownCommandsAndEmptyInput()
        {
            Tuple<bool, string> unknown = GameStateInspector.Execute( "!frob 1" );
            Assert.False( unknown.Item1 );
            Assert.Equal( "Unknown command: !frob", unknown.Item2 );

            Tuple<bool, string> empty = GameStateInspector.Execute( "!" );
            Assert.False( empty.Item1 );
            Assert.Equal( "", empty.Item2 );
        }

        [Fact]
        public void WillTreatTheCommandNameCaseInsensitively()
        {
            Assert.StartsWith( "Usage:", GameStateInspector.Execute( "!PROPS" ).Item2 );
        }

        [Fact]
        public void WillReportAnUnparsableSerial()
        {
            Assert.Equal( "Invalid serial: zzz", GameStateInspector.Execute( "!props zzz" ).Item2 );
            Assert.Equal( "Invalid serial: 0xZZ", GameStateInspector.Execute( "!props 0xZZ" ).Item2 );
        }

        [Fact]
        public void WillReportAMissingEntity()
        {
            Assert.Equal( "Entity 0x40000099 not found", GameStateInspector.Execute( "!props 0x40000099" ).Item2 );
            Assert.Equal( "Entity 0x00000099 not found", GameStateInspector.Execute( "!props 0x99" ).Item2 );
        }

        [Fact]
        public void WillDescribeAnItemAndItsProperties()
        {
            Engine.Items.Add( new Item( 0x40000001, 0x40000002 )
            {
                Name = "Dagger",
                ID = 0x0F52,
                Hue = 0x0481,
                X = 10,
                Y = 20,
                Z = -5,
                Count = 3,
                Properties = new[]
                {
                    new Property { Cliloc = 1042971, Text = "Dagger", Arguments = new[] { "a", "b" } },
                    new Property { Cliloc = 1060659, Text = null }
                }
            } );

            string text = GameStateInspector.Execute( "!props 0x40000001" ).Item2;

            Assert.Contains( "--- Item: 0x40000001 ---", text );
            Assert.Contains( "Name: Dagger", text );
            Assert.Contains( "Graphic: 0x0F52  Hue: 0x0481", text );
            Assert.Contains( "Location: 10, 20, -5", text );
            Assert.Contains( "Count: 3", text );
            Assert.Contains( "Owner: 0x40000002", text );
            Assert.Contains( "Properties (2):", text );
            Assert.Contains( "[0] Cliloc: 1042971", text );
            Assert.Contains( "Args: [a, b]", text );
            Assert.Contains( "Text: (null)", text );
        }

        /// <summary>The same item, asked for by decimal serial and by hex without a prefix.</summary>
        [Fact]
        public void WillAcceptDecimalAndBareHexSerials()
        {
            Engine.Items.Add( new Item( 0x4000000A ) { Name = "Bag" } );

            Assert.Contains( "0x4000000A", GameStateInspector.Execute( "!props 1073741834" ).Item2 );
            Assert.Contains( "0x4000000A", GameStateInspector.Execute( "!props 4000000a" ).Item2 );
            Assert.Contains( "0x4000000A", GameStateInspector.Execute( "!props   0X4000000A  " ).Item2 );
        }

        [Fact]
        public void WillSayWhenAnEntityHasNoProperties()
        {
            Engine.Items.Add( new Item( 0x40000001 ) { Name = "Rock", Count = 0 } );

            string text = GameStateInspector.Execute( "!props 0x40000001" ).Item2;

            Assert.Contains( "(no properties)", text );
            Assert.DoesNotContain( "Count:", text );
            Assert.DoesNotContain( "Owner:", text );
        }

        [Fact]
        public void WillDescribeAMobile()
        {
            Engine.Mobiles.Add( new Mobile( 0x00001234 )
            {
                Name = "Orc", Hits = 7, HitsMax = 25, Notoriety = Notoriety.Murderer
            } );

            string text = GameStateInspector.Execute( "!props 0x1234" ).Item2;

            Assert.Contains( "--- Mobile: 0x00001234 ---", text );
            Assert.Contains( "Hits: 7/25", text );
            Assert.Contains( "Notoriety: Murderer", text );
        }

        /// <summary>The player is not in Engine.Mobiles, but is still a mobile serial to look up.</summary>
        [Fact]
        public void WillFindThePlayerOutsideTheMobileList()
        {
            EngineState.ResetWithPlayer( 0x00000042 );
            Engine.Player.Name = "Me";

            string text = GameStateInspector.Execute( "!props 66" ).Item2;

            Assert.Contains( "--- PlayerMobile: 0x00000042 ---", text );
            Assert.Contains( "Name: Me", text );
        }
    }
}
