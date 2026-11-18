using System;
using System.Collections.Generic;
using System.Linq;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects.Gumps;
using Xunit;

namespace ClassicAssist.Tests.Gumps
{
    /// <summary>
    ///     Pins how a parsed gump is split into pages and how elements are found again: by position,
    ///     id, cliloc, type and proximity. Multi-page gumps are where this goes wrong - page 0 is the
    ///     always-visible background page that usually carries the title and the buttons, and a lookup
    ///     that only sees the last page misses exactly the elements macros ask about.
    /// </summary>
    public class GumpPageLookupTests
    {
        public GumpPageLookupTests()
        {
            // xmfhtmlgump looks its cliloc up while parsing. Cliloc has no data until something sets it,
            // so whether that threw depended on which test had run first.
            Cliloc.Initialize( () => new Dictionary<int, string>() );
        }

        // Page 0 carries the frame, title and a close button; pages 1 and 2 carry the content.
        private const string MultiPageLayout =
            "{ page 0 }{ resizepic 0 0 2600 400 300 }{ text 20 20 0 0 }{ button 350 20 4017 4019 1 0 99 }" +
            "{ page 1 }{ text 20 60 0 1 }{ button 20 260 4005 4007 0 2 0 }" +
            "{ page 2 }{ text 20 60 0 2 }{ button 20 260 4014 4016 0 1 0 }{ button 60 260 4005 4007 1 0 5 }";

        private static readonly string[] MultiPageText = { "Title", "First page body", "Second page body" };

        private static Gump ParseMultiPage()
        {
            return GumpParser.Parse( 0x10, 0x20, 0, 0, MultiPageLayout, MultiPageText );
        }

        [Fact]
        public void WillSplitElementsIntoPagesInOrder()
        {
            Gump gump = ParseMultiPage();

            Assert.Equal( new[] { 0, 1, 2 }, gump.Pages.Select( p => p.Page ) );
            Assert.Equal( 3, gump.Pages[0].GumpElements.Length );
            Assert.Equal( 2, gump.Pages[1].GumpElements.Length );
            Assert.Equal( 3, gump.Pages[2].GumpElements.Length );

            Assert.Equal( "Title", gump.Pages[0].GumpElements[1].Text );
            Assert.Equal( "First page body", gump.Pages[1].GumpElements[0].Text );
            Assert.Equal( "Second page body", gump.Pages[2].GumpElements[0].Text );
        }

        [Fact]
        public void WillLinkElementsToTheirPageAndGump()
        {
            Gump gump = ParseMultiPage();

            foreach ( GumpPage page in gump.Pages )
            {
                Assert.Same( gump, page.ParentGump );

                foreach ( GumpElement element in page.GumpElements )
                {
                    Assert.Same( page, element.ParentPage );
                    Assert.Same( gump, element.ParentGump );
                }
            }
        }

        [Fact]
        public void WillPutElementsBeforeTheFirstPageCommandOnThatPage()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0, "{ text 1 1 0 0 }{ page 0 }{ text 2 2 0 0 }{ page 1 }",
                new[] { "t" } );

            Assert.Equal( 2, gump.Pages[0].GumpElements.Length );
            Assert.Empty( gump.Pages[1].GumpElements );
        }

        [Fact]
        public void WillHaveNoPagesWithoutPageCommands()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0, "{ text 1 1 0 0 }{ text 2 2 0 0 }", new[] { "t" } );

            Assert.Empty( gump.Pages );
            Assert.Equal( 2, gump.GumpElements.Length );
            Assert.All( gump.GumpElements, e => Assert.Null( e.ParentPage ) );
        }

        [Fact]
        public void WillFindElementByIdOnAnyPage()
        {
            Gump gump = ParseMultiPage();

            Assert.True( gump.GetElementByID( 99, out GumpElement close ) );
            Assert.Equal( 350, close.X );

            Assert.True( gump.GetElementByID( 5, out GumpElement ok ) );
            Assert.Equal( 60, ok.X );

            Assert.False( gump.GetElementByID( 12345, out GumpElement missing ) );
            Assert.Null( missing );
        }

        [Fact]
        public void WillFindElementByXYOnLastPage()
        {
            Gump gump = ParseMultiPage();

            Assert.Equal( 5, gump.GetElementByXY( 60, 260 ).ElementID );
            Assert.Null( gump.GetElementByXY( 61, 260 ) );
            Assert.False( gump.GetElementByXY( 999, 999, out GumpElement _ ) );
        }

        [Fact]
        public void WillReturnFirstElementInLayoutOrderWhenElementsOverlap()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0,
                "{ gumppic 10 10 100 }{ button 10 10 1 2 1 0 3 }{ text 10 10 0 0 }", new[] { "t" } );

            Assert.Equal( ElementType.gumppic, gump.GetElementByXY( 10, 10 ).Type );
        }

        [Fact]
        public void WillFindElementByXYOnAPage()
        {
            Gump gump = ParseMultiPage();

            // Pages 1 and 2 both have a button at (20, 260) - each page answers for its own.
            Assert.Equal( 2, gump.Pages[1].GetElementByXY( 20, 260 ).Param );
            Assert.Equal( 1, gump.Pages[2].GetElementByXY( 20, 260 ).Param );
            Assert.Null( gump.Pages[1].GetElementByXY( 60, 260 ) );
        }

        [Fact( Skip = "BUG: Gump.GumpElements from GumpParser holds only the last page, so lookups miss page 0 " )]
        public void WillFindElementByXYOnPageZeroOfAMultiPageGump()
        {
            Gump gump = ParseMultiPage();

            GumpElement close = gump.GetElementByXY( 350, 20 );

            Assert.NotNull( close );
            Assert.Equal( 99, close.ElementID );
        }

        [Fact]
        public void WillFindElementByCliloc()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0,
                "{ page 0 }{ xmfhtmlgump 1 1 10 10 1011036 0 0 }{ xmfhtmlgump 2 2 10 10 1011012 0 0 }",
                Array.Empty<string>() );

            Assert.Equal( 2, gump.GetElementByCliloc( 1011012 ).X );
            Assert.Equal( 1, gump.Pages[0].GetElementByCliloc( 1011036 ).X );
            Assert.Null( gump.GetElementByCliloc( 1 ) );
            Assert.Null( gump.Pages[0].GetElementByCliloc( 1 ) );
        }

        [Fact]
        public void WillFindNearestElementWithinThePage()
        {
            Gump gump = ParseMultiPage();
            GumpElement title = gump.Pages[0].GumpElements[1];

            // On page 0 the resize background at (0,0) is nearer the title at (20,20) than the close
            // button at (350,20); elements on other pages are not candidates.
            Assert.True( title.GetNearestElement( out GumpElement nearest ) );
            Assert.Equal( ElementType.resizepic, nearest.Type );

            Assert.True( title.GetNearestElement( new[] { ElementType.button }, out GumpElement button ) );
            Assert.Equal( 99, button.ElementID );

            Assert.False( title.GetNearestElement( new[] { ElementType.textentry }, out GumpElement none ) );
            Assert.Null( none );
        }

        [Fact]
        public void WillFindNearestElementWithoutPages()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0,
                "{ text 0 0 0 0 }{ button 100 0 1 2 1 0 1 }{ button 10 0 1 2 1 0 2 }{ text 5 5 0 0 }",
                new[] { "t" } );
            GumpElement source = gump.GumpElements[0];

            Assert.True( gump.GetNearestElement( source, out GumpElement nearest ) );
            Assert.Equal( ElementType.text, nearest.Type );

            Assert.True( gump.GetNearestElement( source, new[] { ElementType.button }, out GumpElement button ) );
            Assert.Equal( 2, button.ElementID );

            // Without a page the element itself cannot search.
            Assert.False( source.GetNearestElement( out GumpElement _ ) );
        }

        [Fact]
        public void WillFindNearestElementOfASingleElementPageAsNothing()
        {
            Gump gump = GumpParser.Parse( 1, 1, 0, 0, "{ page 0 }{ text 0 0 0 0 }", new[] { "t" } );

            Assert.False( gump.GetNearestElement( gump.Pages[0].GumpElements[0], out GumpElement nearest ) );
            Assert.Null( nearest );
        }

        [Fact]
        public void WillListElementsOfATypeOnAPage()
        {
            Gump gump = ParseMultiPage();

            Assert.Equal( 2, gump.Pages[2].GetElementsByType( ElementType.button ).Length );
            Assert.Single( gump.Pages[0].GetElementsByType( ElementType.resizepic ) );
        }

        [Fact( Skip = "BUG: Gump.GetElementsByType returns the last page's elements twice (GumpElements and Pages overlap) " )]
        public void WillListEachElementOfATypeOnce()
        {
            Gump gump = ParseMultiPage();

            // 1 close button on page 0, 1 on page 1, 2 on page 2.
            Assert.Equal( 4, gump.GetElementsByType( ElementType.button ).Length );
        }
    }
}
