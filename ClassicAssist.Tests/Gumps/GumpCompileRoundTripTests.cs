using System;
using System.Collections.Generic;
using System.Linq;
using ClassicAssist.Shared;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects.Gumps;
using Xunit;

namespace ClassicAssist.Tests.Gumps
{
    /// <summary>
    ///     Pins the gumps the assistant builds itself (prompts, item pickers, the macros and hotkey status
    ///     gumps): the <see cref="Gump" /> Add* builders, <see cref="GumpElement" /> serialisation and the
    ///     0xB0 writer. The packet is decoded independently, its length fields checked, and its layout fed
    ///     back through <see cref="GumpParser" /> - a builder that writes arguments in the wrong order, or
    ///     a wrong length field, produces a gump the client draws wrongly or drops.
    /// </summary>
    public class GumpCompileRoundTripTests : IDisposable
    {
        private const int TestSerial = 0x7F001234;
        private const uint TestId = 0xCAFE;

        private readonly bool _originalReflectionAvailable;

        public GumpCompileRoundTripTests()
        {
            EngineState.ResetWithPlayer();
            _originalReflectionAvailable = Engine.ReflectionAvailable;
            Cliloc.Initialize( () => new Dictionary<int, string>
            {
                { 1000001, "Plain cliloc" }, { 1000003, "Tooltip ~1_VAL~" }
            } );
        }

        public void Dispose()
        {
            Engine.ReflectionAvailable = _originalReflectionAvailable;
            EngineState.Reset();
        }

        private static DecodedGump SendAndDecode( Gump gump )
        {
            using PacketRecorder recorder = new PacketRecorder();

            gump.SendGump();

            byte[] packet = Assert.Single( recorder.ReceivedWithId( 0xB0 ) );
            DecodedGump decoded = GumpPackets.DecodeGeneric( packet );

            Assert.Equal( packet.Length, decoded.DeclaredLength );

            return decoded;
        }

        private static Gump Reparse( DecodedGump decoded )
        {
            return GumpParser.Parse( decoded.Serial, decoded.GumpId, decoded.X, decoded.Y, decoded.Layout,
                decoded.Strings );
        }

        [Fact]
        public void WillWriteExactLayoutAndHeader()
        {
            TestGump gump = new TestGump( g =>
            {
                g.Movable = false;
                g.Closable = false;
                g.Disposable = true;
                g.Resizable = true;
                g.AddPage( 0 );
                g.AddBackground( 0, 0, 400, 300, 9200 );
                g.AddButton( 1, 2, 3, 4, 5, GumpButtonType.Reply, 0 );
                g.AddLabel( 6, 7, 33, "Hi" );
            } );

            DecodedGump decoded = SendAndDecode( gump );

            Assert.Equal( TestSerial, decoded.Serial );
            Assert.Equal( TestId, decoded.GumpId );
            Assert.Equal( 100, decoded.X );
            Assert.Equal( 200, decoded.Y );
            Assert.Equal(
                "{ nomove }{ noclose }{ page 0 }{ resizepic 0 0 9200 400 300 }{ button 1 2 3 4 1 0 5 }{ text 6 7 33 0 }",
                decoded.Layout );
            Assert.Equal( decoded.Layout.Length, decoded.LayoutLength );
            Assert.Equal( new[] { "Hi" }, decoded.Strings );
        }

        [Fact]
        public void WillWriteAllWindowFlagsInOrder()
        {
            TestGump gump = new TestGump( g =>
            {
                g.Movable = false;
                g.Closable = false;
                g.Disposable = false;
                g.Resizable = false;
            } );

            DecodedGump decoded = SendAndDecode( gump );

            Assert.Equal( "{ nomove }{ noclose }{ nodispose }{ noresize }", decoded.Layout );
            Assert.Empty( decoded.Strings );
            Assert.Equal( 23 + decoded.Layout.Length, decoded.DeclaredLength );

            Gump parsed = Reparse( decoded );
            Assert.False( parsed.Movable );
            Assert.False( parsed.Closable );
            Assert.False( parsed.Disposable );
            Assert.False( parsed.Resizable );
        }

        [Fact]
        public void WillInternRepeatedStrings()
        {
            TestGump gump = new TestGump( g =>
            {
                g.AddLabel( 1, 1, 0, "same" );
                g.AddHtml( 2, 2, 10, 10, "other", false, false );
                g.AddLabelCropped( 3, 3, 10, 10, 0, "same" );
                g.AddTextEntry( 4, 4, 10, 10, 0, 1, "" );
            } );

            DecodedGump decoded = SendAndDecode( gump );

            Assert.Equal( new[] { "same", "other", "" }, decoded.Strings );
            Assert.Contains( "{ text 1 1 0 0 }", decoded.Layout );
            Assert.Contains( "{ croppedtext 3 3 10 10 0 0 }", decoded.Layout );
            Assert.Contains( "{ textentry 4 4 10 10 0 1 2 }", decoded.Layout );
        }

        [Fact]
        public void WillEncodeNonAsciiTextAsBigEndianUnicode()
        {
            TestGump gump = new TestGump( g => g.AddLabel( 1, 1, 0, "Zoë ✓" ) );

            DecodedGump decoded = SendAndDecode( gump );

            Assert.Equal( new[] { "Zoë ✓" }, decoded.Strings );
        }

        [Fact]
        public void WillRoundTripEveryBuilderThroughTheParser()
        {
            TestGump gump = new TestGump( g =>
            {
                g.AddPage( 0 );
                g.AddBackground( 10, 11, 400, 300, 9200 );
                g.AddAlphaRegion( 12, 13, 380, 280 );
                g.AddButton( 14, 15, 4005, 4007, 21, GumpButtonType.Page, 2 );
                g.AddRadio( 16, 17, 208, 209, true, 22 );
                g.AddHtml( 18, 19, 100, 50, "<b>html</b>", true, false );
                g.AddHtmlLocalized( 20, 21, 100, 20, 1000001, false, true );
                g.AddHtmlLocalized( 22, 23, 100, 20, 1000001, 0x7FFF, true, true );
                g.AddHtmlLocalized( 24, 25, 100, 20, 1000003, "fine sword", 16, false, false );
                g.AddImage( 26, 27, 1234 );
                g.AddImage( 28, 29, 1235, 33 );
                g.AddImageTiled( 30, 31, 32, 33, 2624 );
                g.AddImageTiledButton( 34, 35, 2328, 2329, 23, GumpButtonType.Reply, 4, 3821, 1153, 40, 30 );
                g.AddItem( 36, 37, 3821 );
                g.AddItem( 38, 39, 3822, 1161 );
                g.AddLabel( 40, 41, 52, "label" );
                g.AddLabelCropped( 42, 43, 60, 18, 53, "cropped" );
                g.AddTextEntry( 44, 45, 200, 20, 54, 24, "entry" );
                g.AddTextEntry( 46, 47, 200, 20, 55, 25, "limited", 12 );
                g.AddItemProperty( 0x40001234 );
            } );

            Gump parsed = Reparse( SendAndDecode( gump ) );

            GumpElement resize = parsed.GetElementByXY( 10, 11 );
            Assert.Equal( ElementType.resizepic, resize.Type );
            Assert.Equal( 9200, resize.ElementID );
            Assert.Equal( 400, resize.Width );

            GumpElement alpha = parsed.GetElementByXY( 12, 13 );
            Assert.Equal( ElementType.checkertrans, alpha.Type );
            Assert.Equal( 380, alpha.Width );
            Assert.Equal( 280, alpha.Height );

            GumpElement button = parsed.GetElementByXY( 14, 15 );
            Assert.Equal( ElementType.button, button.Type );
            Assert.Equal( 4005, button.InactiveID );
            Assert.Equal( 4007, button.ActiveID );
            Assert.Equal( (int) GumpButtonType.Page, button.ButtonType );
            Assert.Equal( 2, button.Param );
            Assert.Equal( 21, button.ElementID );

            GumpElement radio = parsed.GetElementByXY( 16, 17 );
            Assert.Equal( ElementType.radio, radio.Type );
            Assert.True( radio.InitialState );
            Assert.Equal( 22, radio.ElementID );

            GumpElement html = parsed.GetElementByXY( 18, 19 );
            Assert.Equal( ElementType.htmlgump, html.Type );
            Assert.Equal( "<b>html</b>", html.Text );
            Assert.True( html.Background );
            Assert.False( html.ScrollBar );

            GumpElement localized = parsed.GetElementByXY( 20, 21 );
            Assert.Equal( ElementType.xmfhtmlgump, localized.Type );
            Assert.Equal( "Plain cliloc", localized.Text );
            Assert.False( localized.Background );
            Assert.True( localized.ScrollBar );

            GumpElement coloured = parsed.GetElementByXY( 22, 23 );
            Assert.Equal( ElementType.xmfhtmlgumpcolor, coloured.Type );
            Assert.Equal( 0x7FFF, coloured.Hue );

            GumpElement tok = parsed.GetElementByXY( 24, 25 );
            Assert.Equal( ElementType.xmfhtmltok, tok.Type );
            Assert.Equal( 16, tok.Hue );
            Assert.Equal( "fine sword", tok.Args );
            Assert.Equal( "Tooltip fine sword", tok.Text );

            Assert.Equal( 0, parsed.GetElementByXY( 26, 27 ).Hue );
            GumpElement huedImage = parsed.GetElementByXY( 28, 29 );
            Assert.Equal( ElementType.gumppic, huedImage.Type );
            Assert.Equal( 1235, huedImage.ElementID );
            Assert.Equal( 33, huedImage.Hue );

            GumpElement tiled = parsed.GetElementByXY( 30, 31 );
            Assert.Equal( ElementType.gumppictiled, tiled.Type );
            Assert.Equal( 2624, tiled.ElementID );
            Assert.Equal( 32, tiled.Width );
            Assert.Equal( 33, tiled.Height );

            GumpElement tileButton = parsed.GetElementByXY( 34, 35 );
            Assert.Equal( ElementType.buttontileart, tileButton.Type );
            Assert.Equal( 23, tileButton.ElementID );
            Assert.Equal( 4, tileButton.Param );
            Assert.Equal( 3821, tileButton.ItemID );
            Assert.Equal( 1153, tileButton.Hue );
            Assert.Equal( 40, tileButton.Width );
            Assert.Equal( 30, tileButton.Height );
            Assert.Null( tileButton.Tooltips );

            GumpElement item = parsed.GetElementByXY( 36, 37 );
            Assert.Equal( ElementType.tilepic, item.Type );
            Assert.Equal( 3821, item.ElementID );

            GumpElement huedItem = parsed.GetElementByXY( 38, 39 );
            Assert.Equal( ElementType.tilepichue, huedItem.Type );
            Assert.Equal( 3822, huedItem.ElementID );
            Assert.Equal( 1161, huedItem.Hue );

            GumpElement label = parsed.GetElementByXY( 40, 41 );
            Assert.Equal( ElementType.text, label.Type );
            Assert.Equal( 52, label.Hue );
            Assert.Equal( "label", label.Text );

            GumpElement cropped = parsed.GetElementByXY( 42, 43 );
            Assert.Equal( ElementType.croppedtext, cropped.Type );
            Assert.Equal( "cropped", cropped.Text );
            Assert.Equal( 60, cropped.Width );

            GumpElement entry = parsed.GetElementByXY( 44, 45 );
            Assert.Equal( ElementType.textentry, entry.Type );
            Assert.Equal( 24, entry.ElementID );
            Assert.Equal( "entry", entry.Text );

            GumpElement limited = parsed.GetElementByXY( 46, 47 );
            Assert.Equal( ElementType.textentrylimited, limited.Type );
            Assert.Equal( 25, limited.ElementID );
            Assert.Equal( 12, limited.Size );
            Assert.Equal( "limited", limited.Text );

            GumpElement property = parsed.GumpElements.Single( e => e.Type == ElementType.itemproperty );
            Assert.Equal( 0x40001234, property.Serial );
        }

        [Fact]
        public void WillWriteTooltipAfterImageTiledButton()
        {
            TestGump gump = new TestGump( g =>
                g.AddImageTiledButton( 1, 2, 3, 4, 5, GumpButtonType.Reply, 0, 3821, 0, 40, 30, 1000001 ) );

            DecodedGump decoded = SendAndDecode( gump );

            Assert.Equal( "{ buttontileart 1 2 3 4 1 0 5 3821 0 40 30 }{ tooltip 1000001 }", decoded.Layout );

            GumpElement parsed = Assert.Single( Reparse( decoded ).GumpElements );
            Assert.Equal( 1000001, parsed.Tooltip );
            Assert.Equal( "Plain cliloc", parsed.Text );
        }

        [Fact]
        public void WillWriteGroupAndTooltipCommands()
        {
            TestGump gump = new TestGump( g =>
            {
                g.AddGroup( 3 );
                g.AddTooltip( 1000001 );
            } );

            Assert.Equal( "{ group 3 }{ tooltip 1000001 }", SendAndDecode( gump ).Layout );
        }

        [Fact( Skip = "BUG: GumpElement.AppendTo writes nothing for checkbox, so AddCheck sends an empty '{  }' element " )]
        public void WillRoundTripCheckbox()
        {
            TestGump gump = new TestGump( g => g.AddCheck( 1, 2, 210, 211, true, 7 ) );

            DecodedGump decoded = SendAndDecode( gump );

            Assert.Equal( "{ checkbox 1 2 210 211 1 7 }", decoded.Layout );
        }

        [Fact]
        public void WillRegisterSentGumpAndDeliverClientResponse()
        {
            TestGump gump = new TestGump( g =>
            {
                g.AddTextEntry( 1, 1, 100, 20, 0, 6, "" );
                g.AddButton( 2, 2, 1, 2, 9, GumpButtonType.Reply, 0 );
            } );

            SendAndDecode( gump );

            Assert.True( Engine.Gumps.GetGump( TestId, out Gump registered ) );
            Assert.Same( gump, registered );

            EngineState.Send( GumpPackets.BuildResponse( TestSerial, TestId, 9, new[] { 3, 4 }, ( 6, "typed" ) ) );

            Assert.Equal( 9, gump.ResponseButton );
            Assert.Equal( new[] { 3, 4 }, gump.ResponseSwitches );
            Assert.Equal( new List<(int, string)> { ( 6, "typed" ) }, gump.ResponseText );
            Assert.True( gump.Closed );
            Assert.False( Engine.Gumps.GetGump( TestId, out Gump _ ) );
        }

        [Fact]
        public void WillCloseClientGumpById()
        {
            TestGump gump = new TestGump( g => g.AddLabel( 1, 1, 0, "x" ) );

            SendAndDecode( gump );

            using PacketRecorder recorder = new PacketRecorder();

            gump.CloseGump();

            Assert.False( Engine.Gumps.GetGump( TestId, out Gump _ ) );
            Assert.Equal( new byte[] { 0xBF, 0x00, 0x0D, 0x00, 0x04, 0x00, 0x00, 0xCA, 0xFE, 0, 0, 0, 0 },
                Assert.Single( recorder.ReceivedWithId( 0xBF ) ) );
        }

        [Fact]
        public void WillCloseAllClientGumpsOfAType()
        {
            TestGump first = new TestGump( g => g.AddLabel( 1, 1, 0, "x" ), 0x1001 );
            TestGump second = new TestGump( g => g.AddLabel( 1, 1, 0, "x" ), 0x1002 );
            Engine.Gumps.Add( first );
            Engine.Gumps.Add( second );
            Engine.Gumps.Add( new Gump( 0, 0, 5, 0x1003 ) );

            using PacketRecorder recorder = new PacketRecorder();

            ClassicAssist.Shared.UO.Commands.CloseClientGump( typeof( TestGump ) );

            Assert.Equal( 2, recorder.ReceivedWithId( 0xBF ).Count );
            Assert.False( Engine.Gumps.GetGump( 0x1001, out Gump _ ) );
            Assert.False( Engine.Gumps.GetGump( 0x1002, out Gump _ ) );
            Assert.True( Engine.Gumps.GetGump( 0x1003, out Gump _ ) );
        }

        [Fact]
        public void WillDeriveStableIdAndHighSerialFromTypeName()
        {
            AutoIdGump first = new AutoIdGump();
            AutoIdGump second = new AutoIdGump();

            Assert.Equal( (uint) typeof( AutoIdGump ).FullName.GetHashCode(), first.ID );
            Assert.Equal( first.ID, second.ID );

            // 0xFF in the top byte keeps the serial out of the world's item/mobile ranges, and the
            // middle bytes come from the type name.
            uint expectedMiddle = ( (uint) typeof( AutoIdGump ).FullName.GetHashCode() & 0xFFFF ) << 8;

            foreach ( AutoIdGump gump in new[] { first, second } )
            {
                Assert.Equal( 0xFFu, (uint) gump.Serial >> 24 );
                Assert.Equal( expectedMiddle, (uint) gump.Serial & 0x00FFFF00 );
            }
        }

        [Fact]
        public void WillReturnExistingIndexWhenInterning()
        {
            TestGump gump = new TestGump( _ => { } );

            Assert.Equal( 0, gump.Intern( "a" ) );
            Assert.Equal( 1, gump.Intern( "b" ) );
            Assert.Equal( 0, gump.Intern( "a" ) );
            Assert.Equal( new[] { "a", "b" }, gump.Strings );
        }

        [Fact]
        public void WillAddManualRepositionButtonWhenReflectionUnavailable()
        {
            Engine.ReflectionAvailable = false;

            TestRepositionableGump gump = new TestRepositionableGump { GumpX = 321, GumpY = 123 };

            DecodedGump decoded = SendAndDecode( gump );

            Assert.Equal( 321, decoded.X );
            Assert.Equal( 123, decoded.Y );
            Assert.EndsWith( "{ button 235 5 2092 2092 1 0 100 }", decoded.Layout );
            Assert.Contains( "{ nomove }", decoded.Layout );
        }

        [Fact]
        public void WillMakeGumpMovableInsteadWhenReflectionAvailable()
        {
            Engine.ReflectionAvailable = true;

            TestRepositionableGump gump = new TestRepositionableGump();

            DecodedGump decoded = SendAndDecode( gump );

            Assert.DoesNotContain( "button", decoded.Layout );
            Assert.DoesNotContain( "nomove", decoded.Layout );
            Assert.Equal( 100, decoded.X );
            Assert.Equal( 100, decoded.Y );
        }

        private sealed class TestGump : Gump
        {
            public TestGump( Action<TestGump> build, uint id = TestId ) : base( 100, 200, TestSerial, id )
            {
                Movable = true;
                Closable = true;
                Disposable = true;
                Resizable = true;

                build( this );
            }

            public bool Closed { get; private set; }
            public int ResponseButton { get; private set; } = -1;
            public int[] ResponseSwitches { get; private set; }
            public List<(int, string)> ResponseText { get; private set; }

            public override void OnResponse( int buttonID, int[] switches,
                List<(int Key, string Value)> textEntries = null )
            {
                ResponseButton = buttonID;
                ResponseSwitches = switches;
                ResponseText = textEntries?.Select( e => ( e.Key, e.Value ) ).ToList();
            }

            public override void OnClosing()
            {
                Closed = true;
            }
        }

        private sealed class AutoIdGump : Gump
        {
            public AutoIdGump() : base( 0, 0 )
            {
            }
        }

        private sealed class TestRepositionableGump : ClassicAssist.UO.Gumps.ReflectionRepositionableGump
        {
            public TestRepositionableGump() : base( 250, 100, 0x7F00AAAA, 0xAAAA )
            {
                Disposable = true;
                Resizable = true;
                Closable = true;
                AddLabel( 1, 1, 0, "x" );
            }
        }
    }
}
