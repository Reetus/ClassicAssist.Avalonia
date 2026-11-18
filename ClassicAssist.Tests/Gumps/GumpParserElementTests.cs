using System;
using System.Collections.Generic;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects.Gumps;
using Xunit;

namespace ClassicAssist.Tests.Gumps
{
    /// <summary>
    ///     Pins how <see cref="GumpParser" /> turns each layout command a shard can send into a
    ///     <see cref="GumpElement" />: which argument lands in which field, how text lines and clilocs are
    ///     resolved, the window flags, and what happens with malformed or unknown commands. Every
    ///     gump-reading macro command (InGump, ReplyGump by position, the gump debug view) reads these
    ///     fields, and a shifted argument index silently breaks them all, so each kind is checked with
    ///     distinct values per argument.
    /// </summary>
    public class GumpParserElementTests
    {
        public GumpParserElementTests()
        {
            Cliloc.Initialize( () => new Dictionary<int, string>
            {
                { 1000001, "Plain cliloc" },
                { 1000002, "Hello ~1_NAME~, you have ~2_COUNT~ items" },
                { 1000003, "Tooltip ~1_VAL~" },
                { 1000004, "Inner" },
                { 1000005, "Outer ~1_X~" }
            } );
        }

        private static GumpElement ParseSingle( string layout, params string[] text )
        {
            Gump gump = GumpParser.Parse( 0x1234, 0x5678, 10, 20, layout, text );

            return Assert.Single( gump.GumpElements );
        }

        [Fact]
        public void WillReturnNullForEmptyLayout()
        {
            Assert.Null( GumpParser.Parse( 1, 2, 0, 0, string.Empty, Array.Empty<string>() ) );
            Assert.Null( GumpParser.Parse( 1, 2, 0, 0, null, Array.Empty<string>() ) );
        }

        [Fact]
        public void WillCarryHeaderFieldsOntoGump()
        {
            Gump gump = GumpParser.Parse( 0x1234, 0x89ABCDEF, 10, 20, "{ text 1 2 3 0 }", new[] { "a" } );

            Assert.Equal( 0x1234, gump.Serial );
            Assert.Equal( 0x89ABCDEFu, gump.ID );
            Assert.Equal( 10, gump.X );
            Assert.Equal( 20, gump.Y );
            Assert.Equal( "{ text 1 2 3 0 }", gump.Layout );
            Assert.Equal( new[] { "a" }, gump.Strings );
        }

        [Fact]
        public void WillParseButton()
        {
            GumpElement ge = ParseSingle( "{ button 60 95 4005 4007 1 3 7 }" );

            Assert.Equal( ElementType.button, ge.Type );
            Assert.Equal( 60, ge.X );
            Assert.Equal( 95, ge.Y );
            Assert.Equal( 4005, ge.InactiveID );
            Assert.Equal( 4007, ge.ActiveID );
            Assert.Equal( 1, ge.ButtonType );
            Assert.Equal( 3, ge.Param );
            Assert.Equal( 7, ge.ElementID );
        }

        [Fact]
        public void WillParseKrButtonAsButton()
        {
            GumpElement ge = ParseSingle( "{ kr_button 1 2 3 4 0 5 6 }" );

            Assert.Equal( ElementType.button, ge.Type );
            Assert.Equal( 6, ge.ElementID );
            Assert.Equal( 5, ge.Param );
        }

        [Fact]
        public void WillParseButtonTileArt()
        {
            GumpElement ge = ParseSingle( "{ buttontileart 50 101 2328 2329 1 3 9 44963 2661 18 20 }" );

            Assert.Equal( ElementType.buttontileart, ge.Type );
            Assert.Equal( 50, ge.X );
            Assert.Equal( 101, ge.Y );
            Assert.Equal( 2328, ge.InactiveID );
            Assert.Equal( 2329, ge.ActiveID );
            Assert.Equal( 1, ge.ButtonType );
            Assert.Equal( 3, ge.Param );
            Assert.Equal( 9, ge.ElementID );
            Assert.Equal( 44963, ge.ItemID );
            Assert.Equal( 2661, ge.Hue );
            Assert.Equal( 18, ge.Width );
            Assert.Equal( 20, ge.Height );
        }

        [Fact]
        public void WillParseCheckerTrans()
        {
            GumpElement ge = ParseSingle( "{ checkertrans 1 2 300 400 }" );

            Assert.Equal( ElementType.checkertrans, ge.Type );
            Assert.Equal( 1, ge.X );
            Assert.Equal( 2, ge.Y );
            Assert.Equal( 300, ge.Width );
            Assert.Equal( 400, ge.Height );
        }

        [Fact]
        public void WillParseCroppedTextFromTextLine()
        {
            GumpElement ge = ParseSingle( "{ croppedtext 10 20 100 18 33 1 }", "zero", "one" );

            Assert.Equal( ElementType.croppedtext, ge.Type );
            Assert.Equal( 10, ge.X );
            Assert.Equal( 20, ge.Y );
            Assert.Equal( 100, ge.Width );
            Assert.Equal( 18, ge.Height );
            Assert.Equal( 33, ge.Hue );
            Assert.Equal( "one", ge.Text );
        }

        [Fact]
        public void WillParseCheckboxAndRadioInitialState()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0,
                "{ checkbox 1 2 210 211 1 5 }{ radio 3 4 208 209 0 6 }{ radio 5 6 208 209 1 7 }",
                Array.Empty<string>() );

            GumpElement checkbox = gump.GetElementByXY( 1, 2 );
            Assert.Equal( ElementType.checkbox, checkbox.Type );
            Assert.Equal( 210, checkbox.InactiveID );
            Assert.Equal( 211, checkbox.ActiveID );
            Assert.True( checkbox.InitialState );
            Assert.Equal( 5, checkbox.ElementID );

            GumpElement radioOff = gump.GetElementByXY( 3, 4 );
            Assert.Equal( ElementType.radio, radioOff.Type );
            Assert.Equal( 208, radioOff.InactiveID );
            Assert.Equal( 209, radioOff.ActiveID );
            Assert.False( radioOff.InitialState );
            Assert.Equal( 6, radioOff.ElementID );

            Assert.True( gump.GetElementByXY( 5, 6 ).InitialState );
        }

        [Fact]
        public void WillParseGumpPicWithAndWithoutHue()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0,
                "{ gumppic 5 6 1234 }{ gumppic 7 8 1235 hue=33 }{ kr_gumppic 9 10 1236 class=VirtueGumpItem }",
                Array.Empty<string>() );

            GumpElement plain = gump.GetElementByXY( 5, 6 );
            Assert.Equal( ElementType.gumppic, plain.Type );
            Assert.Equal( 1234, plain.ElementID );
            Assert.Equal( 0, plain.Hue );
            Assert.Null( plain.Args );

            GumpElement hued = gump.GetElementByXY( 7, 8 );
            Assert.Equal( 1235, hued.ElementID );
            Assert.Equal( 33, hued.Hue );

            GumpElement withArgs = gump.GetElementByXY( 9, 10 );
            Assert.Equal( ElementType.gumppic, withArgs.Type );
            Assert.Equal( 1236, withArgs.ElementID );
            Assert.Equal( "class=VirtueGumpItem", withArgs.Args );
        }

        [Fact]
        public void WillThrowWhenGumpPicHueIsNotANumber()
        {
            Assert.Throws<InvalidOperationException>( () =>
                GumpParser.Parse( 1, 1, 0, 0, "{ gumppic 5 6 1234 hue=red }", Array.Empty<string>() ) );
        }

        [Fact]
        public void WillParseGumpPicTiled()
        {
            GumpElement ge = ParseSingle( "{ gumppictiled 1 2 30 40 2624 }" );

            Assert.Equal( ElementType.gumppictiled, ge.Type );
            Assert.Equal( 30, ge.Width );
            Assert.Equal( 40, ge.Height );
            Assert.Equal( 2624, ge.ElementID );
        }

        [Fact]
        public void WillParseXmfHtmlGumpAndResolveCliloc()
        {
            GumpElement ge = ParseSingle( "{ xmfhtmlgump 52 35 420 55 1000001 1 0 }" );

            Assert.Equal( ElementType.xmfhtmlgump, ge.Type );
            Assert.Equal( 52, ge.X );
            Assert.Equal( 35, ge.Y );
            Assert.Equal( 420, ge.Width );
            Assert.Equal( 55, ge.Height );
            Assert.Equal( 1000001, ge.Cliloc );
            Assert.True( ge.Background );
            Assert.False( ge.ScrollBar );
            Assert.Equal( "Plain cliloc", ge.Text );
        }

        [Fact]
        public void WontResolveTextForZeroCliloc()
        {
            GumpElement ge = ParseSingle( "{ xmfhtmlgump 52 35 420 55 0 0 1 }" );

            Assert.Equal( 0, ge.Cliloc );
            Assert.Null( ge.Text );
            Assert.True( ge.ScrollBar );
        }

        [Fact]
        public void WillParseKrXmfHtmlGump()
        {
            GumpElement ge = ParseSingle( "{ kr_xmfhtmlgump 1 2 3 4 1000001 0 1 }" );

            Assert.Equal( ElementType.kr_xmfhtmlgump, ge.Type );
            Assert.Equal( "Plain cliloc", ge.Text );
            Assert.False( ge.Background );
            Assert.True( ge.ScrollBar );
        }

        [Fact]
        public void WillParseXmfHtmlGumpColor()
        {
            GumpElement ge = ParseSingle( "{ xmfhtmlgumpcolor 1 2 3 4 1000001 1 1 32767 }" );

            Assert.Equal( ElementType.xmfhtmlgumpcolor, ge.Type );
            Assert.Equal( 1000001, ge.Cliloc );
            Assert.Equal( 32767, ge.Hue );
            Assert.True( ge.Background );
            Assert.True( ge.ScrollBar );
            Assert.Equal( "Plain cliloc", ge.Text );
        }

        [Fact]
        public void WillParseXmfHtmlTokWithMultiWordArgument()
        {
            // The layout is split on spaces, so the parser must re-join the pieces of an argument that
            // contains spaces.
            GumpElement ge = ParseSingle( "{ xmfhtmltok 1 2 300 20 1 0 16 1000003 @Lord   British@ }" );

            Assert.Equal( ElementType.xmfhtmltok, ge.Type );
            Assert.Equal( 1, ge.X );
            Assert.Equal( 2, ge.Y );
            Assert.Equal( 300, ge.Width );
            Assert.Equal( 20, ge.Height );
            Assert.True( ge.Background );
            Assert.False( ge.ScrollBar );
            Assert.Equal( 16, ge.Hue );
            Assert.Equal( 1000003, ge.Cliloc );
            Assert.Equal( "Lord British", ge.Args );
            Assert.Equal( "Tooltip Lord British", ge.Text );
        }

        [Fact( Skip = "BUG: GumpParser.GetTokens splits xmfhtmltok/tooltip arguments on '@', not on the tab the protocol uses " )]
        public void WillSplitXmfHtmlTokArgumentsOnTab()
        {
            // RunUO/ServUO AddHtmlLocalized(..., args, ...) and ClassicUO both separate cliloc arguments
            // with '\t' inside a single @...@ block.
            GumpElement ge = ParseSingle( "{ xmfhtmltok 1 2 300 20 0 0 0 1000002 @Lord British\t42@ }" );

            Assert.Equal( "Hello Lord British, you have 42 items", ge.Text );
        }

        [Fact]
        public void WillResolveNestedClilocArgumentInXmfHtmlTok()
        {
            GumpElement ge = ParseSingle( "{ xmfhtmltok 1 2 3 4 0 0 0 1000005 @#1000004@ }" );

            Assert.Equal( "Outer Inner", ge.Text );
        }

        [Fact]
        public void WillParseXmfHtmlTokWithoutArguments()
        {
            GumpElement ge = ParseSingle( "{ xmfhtmltok 1 2 3 4 0 0 0 1000001 }" );

            Assert.Equal( string.Empty, ge.Args );
            Assert.Equal( "Plain cliloc", ge.Text );
        }

        [Fact]
        public void WillParseMasterGump()
        {
            GumpElement ge = ParseSingle( "{ mastergump 42 }" );

            Assert.Equal( ElementType.mastergump, ge.Type );
            Assert.Equal( 42, ge.ElementID );
        }

        [Fact]
        public void WillParseResizePicDimensions()
        {
            GumpElement ge = ParseSingle( "{ resizepic 10 20 2600 500 135 }" );

            Assert.Equal( ElementType.resizepic, ge.Type );
            Assert.Equal( 10, ge.X );
            Assert.Equal( 20, ge.Y );
            Assert.Equal( 2600, ge.ElementID );
            Assert.Equal( 500, ge.Width );
        }

        [Fact( Skip = "BUG: GumpParser resizepic sets Height from the width argument and ignores the height " )]
        public void WillParseResizePicHeightFromFifthArgument()
        {
            GumpElement ge = ParseSingle( "{ resizepic 10 20 2600 500 135 }" );

            Assert.Equal( 135, ge.Height );
        }

        [Fact]
        public void WillParseTextFromTextLine()
        {
            GumpElement ge = ParseSingle( "{ text 250 50 900 2 }", "a", "b", "Hello there" );

            Assert.Equal( ElementType.text, ge.Type );
            Assert.Equal( 250, ge.X );
            Assert.Equal( 50, ge.Y );
            Assert.Equal( 900, ge.Hue );
            Assert.Equal( "Hello there", ge.Text );
        }

        [Fact]
        public void WillParseHtmlGumpFromTextLine()
        {
            GumpElement ge = ParseSingle( "{ htmlgump 202 70 200 18 1 1 0 }", "zero", "<b>one</b>" );

            Assert.Equal( ElementType.htmlgump, ge.Type );
            Assert.Equal( 200, ge.Width );
            Assert.Equal( 18, ge.Height );
            Assert.Equal( "<b>one</b>", ge.Text );
            Assert.True( ge.Background );
            Assert.False( ge.ScrollBar );
        }

        [Fact]
        public void WillParseTextEntryAndLimitedTextEntry()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0,
                "{ textentry 204 450 192 20 33 6 0 }{ textentrylimited 1 2 3 4 5 7 1 12 }",
                new[] { "initial", "limited" } );

            GumpElement entry = gump.GetElementByXY( 204, 450 );
            Assert.Equal( ElementType.textentry, entry.Type );
            Assert.Equal( 192, entry.Width );
            Assert.Equal( 20, entry.Height );
            Assert.Equal( 33, entry.Hue );
            Assert.Equal( 6, entry.ElementID );
            Assert.Equal( "initial", entry.Text );

            GumpElement limited = gump.GetElementByXY( 1, 2 );
            Assert.Equal( ElementType.textentrylimited, limited.Type );
            Assert.Equal( 3, limited.Width );
            Assert.Equal( 4, limited.Height );
            Assert.Equal( 5, limited.Hue );
            Assert.Equal( 7, limited.ElementID );
            Assert.Equal( "limited", limited.Text );
            Assert.Equal( 12, limited.Size );
        }

        [Fact]
        public void WillParseTilePicAndTilePicHue()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0,
                "{ tilepic 1 2 3821 }{ tilepic 3 4 3822,5 }{ tilepichue 90 314 8413 1152 }",
                Array.Empty<string>() );

            GumpElement plain = gump.GetElementByXY( 1, 2 );
            Assert.Equal( ElementType.tilepic, plain.Type );
            Assert.Equal( 3821, plain.ElementID );

            // Some shards append ",n" to the graphic; only the graphic is the item id.
            Assert.Equal( 3822, gump.GetElementByXY( 3, 4 ).ElementID );

            GumpElement hued = gump.GetElementByXY( 90, 314 );
            Assert.Equal( ElementType.tilepichue, hued.Type );
            Assert.Equal( 8413, hued.ElementID );
            Assert.Equal( 1152, hued.Hue );
        }

        [Fact]
        public void WillParseItemPropertySerial()
        {
            GumpElement ge = ParseSingle( "{ itemproperty 1074292781 }" );

            Assert.Equal( ElementType.itemproperty, ge.Type );
            Assert.Equal( 1074292781, ge.Serial );
        }

        [Fact]
        public void WillAttachTooltipsToPrecedingTileArtButton()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0,
                "{ buttontileart 50 101 2328 2328 0 0 0 44963 2661 18 0 }{ tooltip 1000001 }{ tooltip 1000003 @fine sword@ }",
                Array.Empty<string>() );

            GumpElement ge = Assert.Single( gump.GumpElements );

            Assert.Equal( 2, ge.Tooltips.Count );
            Assert.Equal( 1000001, ge.Tooltips[0].Cliloc );
            Assert.Equal( "Plain cliloc", ge.Tooltips[0].Text );
            Assert.Equal( new[] { "fine sword" }, ge.Tooltips[1].Arguments );
            Assert.Equal( "Tooltip fine sword", ge.Tooltips[1].Text );

            // The element reports the last tooltip.
            Assert.Equal( 1000003, ge.Tooltip );
            Assert.Equal( "Tooltip fine sword", ge.Text );
        }

        [Fact]
        public void WontThrowForTooltipWithoutPrecedingElement()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0, "{ tooltip 1000001 }{ text 1 2 3 0 }", new[] { "t" } );

            GumpElement ge = Assert.Single( gump.GumpElements );
            Assert.Null( ge.Tooltips );
        }

        [Fact]
        public void WillDefaultWindowFlagsToTrue()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0, "{ text 1 2 3 0 }", new[] { "t" } );

            Assert.True( gump.Closable );
            Assert.True( gump.Movable );
            Assert.True( gump.Disposable );
            Assert.True( gump.Resizable );
        }

        [Fact]
        public void WillClearWindowFlagsFromCommands()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0, "{ noclose }{ nomove }{ nodispose }{ noresize }",
                Array.Empty<string>() );

            Assert.False( gump.Closable );
            Assert.False( gump.Movable );
            Assert.False( gump.Disposable );
            Assert.False( gump.Resizable );
            Assert.Empty( gump.GumpElements );
        }

        [Fact]
        public void WillClearOnlyTheNamedFlag()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0, "{ nomove }", Array.Empty<string>() );

            Assert.False( gump.Movable );
            Assert.True( gump.Closable );
            Assert.True( gump.Disposable );
            Assert.True( gump.Resizable );
        }

        [Fact]
        public void WillMatchCommandsCaseInsensitively()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0, "{ BUTTON 1 2 3 4 1 0 9 }{ NoClose }", Array.Empty<string>() );

            Assert.Equal( ElementType.button, Assert.Single( gump.GumpElements ).Type );
            Assert.False( gump.Closable );
        }

        [Fact]
        public void WillIgnoreUnknownCommands()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0,
                "{ frobnicate 1 2 3 }{ echandleinput }{ group 1 }{ button 1 2 3 4 1 0 9 }", Array.Empty<string>() );

            Assert.Equal( ElementType.button, Assert.Single( gump.GumpElements ).Type );
        }

        [Fact]
        public void WillTolerateExtraWhitespaceAndLeadingNoise()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0, "  junk {   button   1   2 3 4 1 0 9   }   {text 5 6 7 0}  ",
                new[] { "t" } );

            Assert.Equal( 2, gump.GumpElements.Length );
            Assert.Equal( 9, gump.GetElementByXY( 1, 2 ).ElementID );
            Assert.Equal( "t", gump.GetElementByXY( 5, 6 ).Text );
        }

        [Theory]
        [InlineData( "{ button 1 2 x 4 1 0 9 }", "button" )]
        [InlineData( "{ button 1 2 }", "button" )]
        [InlineData( "{ buttontileart 1 2 3 4 5 6 7 8 9 10 }", "buttontileart" )]
        [InlineData( "{ checkertrans 1 2 3 }", "checkertrans" )]
        [InlineData( "{ checkbox 1 2 3 4 5 }", "checkbox" )]
        [InlineData( "{ radio a 2 3 4 5 6 }", "radio" )]
        [InlineData( "{ resizepic 1 2 3 4 }", "resizepic" )]
        [InlineData( "{ gumppic 1 2 }", "gumppic" )]
        [InlineData( "{ gumppictiled 1 2 3 4 }", "gumppictiled" )]
        [InlineData( "{ htmlgump 1 2 3 4 0 0 }", "htmlgump" )]
        [InlineData( "{ textentry 1 2 3 4 5 6 }", "textentry" )]
        [InlineData( "{ textentrylimited 1 2 3 4 5 6 0 }", "textentrylimited" )]
        [InlineData( "{ tilepic 1 2 }", "tilepic" )]
        [InlineData( "{ tilepichue 1 2 3 }", "tilepichue" )]
        [InlineData( "{ xmfhtmlgump 1 2 3 4 5 6 }", "xmfhtmlgump" )]
        [InlineData( "{ kr_xmfhtmlgump 1 2 3 4 5 6 }", "kr_xmfhtmlgump" )]
        [InlineData( "{ xmfhtmlgumpcolor 1 2 3 4 5 6 7 }", "xmfhtmlgumpcolor" )]
        [InlineData( "{ xmfhtmltok 1 2 3 4 5 6 7 }", "xmfhtmltok" )]
        [InlineData( "{ mastergump }", "mastergump" )]
        [InlineData( "{ text 1 2 3 }", "text" )]
        [InlineData( "{ croppedtext 1 2 3 4 5 }", "croppedtext" )]
        [InlineData( "{ tooltip abc }", "tooltip" )]
        [InlineData( "{ page x }", "page" )]
        public void WillThrowNamingTheElementForMalformedArguments( string layout, string element )
        {
            InvalidOperationException e = Assert.Throws<InvalidOperationException>( () =>
                GumpParser.Parse( 1, 1, 0, 0, layout, new[] { "t" } ) );

            Assert.Contains( $"element {element} ", e.Message );
            Assert.Contains( layout, e.Message );
        }

        [Theory]
        [InlineData( "{ text 1 2 3 5 }" )]
        [InlineData( "{ croppedtext 1 2 3 4 5 9 }" )]
        [InlineData( "{ htmlgump 1 2 3 4 9 0 0 }" )]
        [InlineData( "{ textentry 1 2 3 4 5 6 9 }" )]
        public void WillThrowWhenTextIndexIsOutOfRange( string layout )
        {
            Assert.Throws<InvalidOperationException>( () => GumpParser.Parse( 1, 1, 0, 0, layout, new[] { "only" } ) );
        }

        [Fact]
        public void WillIgnoreItemPropertyWithInvalidSerial()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0, "{ itemproperty nope }", Array.Empty<string>() );

            Assert.Empty( gump.GumpElements );
        }
    }
}
