using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Gumps;
using ClassicAssist.UO.Objects;
using ClassicAssist.UO.Objects.Gumps;
using Xunit;

namespace ClassicAssist.Tests.Gumps
{
    /// <summary>
    ///     Pins the assistant's own interactive gumps - the item picker, the confirm / selection / message
    ///     prompts and the running-macros gump. Each is checked twice: the layout it sends must parse back
    ///     to the expected controls (sizes, paging, button ids the response is decoded from), and a reply
    ///     from the client must come back out of the blocking call as the right result. A mismatch between
    ///     the ids a builder writes and the ids its response handler expects makes a prompt return the
    ///     wrong answer without any error.
    /// </summary>
    public class GumpBuilderTests : IDisposable
    {
        private readonly CharacterListFlags _originalCharacterListFlags;
        private readonly Options _originalOptions;
        private readonly bool _originalReflectionAvailable;
        private readonly ArtState _originalArt;
        private readonly ClassicAssist.UI.Misc.ObservableCollectionEx<MacroEntry> _originalMacros;

        public GumpBuilderTests()
        {
            EngineState.ResetWithPlayer();
            _originalOptions = Options.CurrentOptions;
            _originalCharacterListFlags = Engine.CharacterListFlags;
            _originalReflectionAvailable = Engine.ReflectionAvailable;
            Options.CurrentOptions = new Options();
            Engine.ReflectionAvailable = false;

            // MacroEntry names are checked for uniqueness against the manager's list.
            _originalMacros = MacroManager.GetInstance().Items;
            MacroManager.GetInstance().Items = new ClassicAssist.UI.Misc.ObservableCollectionEx<MacroEntry>();

            // The item picker sizes cells from the art; point Art at a folder with no art so every
            // tile is empty instead of needing a client install.
            _originalArt = ArtState.Capture();
            Art.Initialize( Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString( "N" ) ) );
            Engine.CharacterListFlags = 0;
        }

        public void Dispose()
        {
            Options.CurrentOptions = _originalOptions;
            Engine.CharacterListFlags = _originalCharacterListFlags;
            Engine.ReflectionAvailable = _originalReflectionAvailable;
            MacroManager.GetInstance().Items = _originalMacros;
            _originalArt.Restore();
            EngineState.Reset();
        }

        private static List<Item> AddItems( int count )
        {
            List<Item> items = new List<Item>();

            for ( int i = 0; i < count; i++ )
            {
                Item item = new Item( 0x40000100 + i ) { ID = 0x0EED + i, Hue = i };
                Engine.Items.Add( item );
                items.Add( item );
            }

            return items;
        }

        private static Gump Compile( Gump gump )
        {
            using PacketRecorder recorder = new PacketRecorder();

            gump.SendGump();

            DecodedGump decoded = GumpPackets.DecodeGeneric( Assert.Single( recorder.ReceivedWithId( 0xB0 ) ) );

            return GumpParser.Parse( decoded.Serial, decoded.GumpId, decoded.X, decoded.Y, decoded.Layout,
                decoded.Strings );
        }

        /// <summary>
        ///     Runs a blocking prompt on a worker thread, waits for it to send its gump, answers it the way
        ///     the client would and returns the prompt's result.
        /// </summary>
        private static async Task<T> AnswerPromptAsync<T>( Func<T> prompt,
            Func<DecodedGump, byte[]> response )
        {
            using PacketRecorder recorder = new PacketRecorder();

            Task<T> task = Task.Run( prompt, TestContext.Current.CancellationToken );

            Assert.True( SpinWait.SpinUntil( () => recorder.ReceivedWithId( 0xB0 ).Count > 0 || task.IsCompleted,
                5000 ) );

            DecodedGump decoded = GumpPackets.DecodeGeneric( recorder.ReceivedWithId( 0xB0 )[0] );

            EngineState.Send( response( decoded ) );

            Assert.Same( task, await Task.WhenAny( task, Task.Delay( 5000, TestContext.Current.CancellationToken ) ) );

            return await task;
        }

        [Fact]
        public void WillLayOutItemArrayGumpForFewItems()
        {
            List<Item> items = AddItems( 3 );

            Gump gump = Compile( new ItemArrayGump( items, 0xFD000001, false, 50, 60, false ) );

            Assert.Equal( 50, gump.X );
            Assert.Equal( 60, gump.Y );
            Assert.True( gump.Movable );
            Assert.False( gump.Disposable );
            Assert.False( gump.Resizable );

            Assert.Equal( new[] { 0, 1 }, gump.Pages.Select( p => p.Page ) );

            // 3 cells of 90 plus a 30 margin wide, one 70 row plus a 60 margin high.
            GumpElement background = gump.Pages[0].GumpElements[0];
            Assert.Equal( ElementType.resizepic, background.Type );
            Assert.Equal( 0x13BE, background.ElementID );
            Assert.Equal( 300, background.Width );

            GumpElement alpha = gump.Pages[0].GumpElements[1];
            Assert.Equal( ElementType.checkertrans, alpha.Type );
            Assert.Equal( 280, alpha.Width );
            Assert.Equal( 110, alpha.Height );

            GumpElement[] cells = gump.Pages[1].GetElementsByType( ElementType.buttontileart );
            Assert.Equal( items.Select( i => i.Serial ), cells.Select( c => c.ElementID ) );
            Assert.Equal( items.Select( i => i.ID ), cells.Select( c => c.ItemID ) );
            Assert.Equal( items.Select( i => i.Hue ), cells.Select( c => c.Hue ) );
            Assert.Equal( new[] { 20, 110, 200 }, cells.Select( c => c.X ) );
            Assert.All( cells, c => Assert.Equal( 20, c.Y ) );
            Assert.All( cells, c => Assert.Equal( (int) GumpButtonType.Reply, c.ButtonType ) );

            // Single select: no paging and no OK button.
            Assert.Empty( gump.Pages[1].GetElementsByType( ElementType.button ) );
            Assert.Empty( gump.Pages[1].GetElementsByType( ElementType.itemproperty ) );
        }

        [Fact]
        public void WillWrapItemArrayGumpRowsAtFourItems()
        {
            List<Item> items = AddItems( 6 );

            Gump gump = Compile( new ItemArrayGump( items, 0xFD000001, false, 0, 0, false ) );

            GumpElement[] cells = gump.Pages[1].GetElementsByType( ElementType.buttontileart );

            Assert.Equal( new[] { 20, 110, 200, 290, 20, 110 }, cells.Select( c => c.X ) );
            Assert.Equal( new[] { 20, 20, 20, 20, 90, 90 }, cells.Select( c => c.Y ) );

            // Width is capped at four cells; height grows with the row count.
            Assert.Equal( 390, gump.Pages[0].GumpElements[0].Width );
            Assert.Equal( 180, gump.Pages[0].GumpElements[1].Height );
        }

        [Fact]
        public void WillPageItemArrayGumpAtTwentyItems()
        {
            List<Item> items = AddItems( 25 );

            Gump gump = Compile( new ItemArrayGump( items, 0xFD000001, false, 0, 0, false ) );

            Assert.Equal( new[] { 0, 1, 2 }, gump.Pages.Select( p => p.Page ) );
            Assert.Equal( 20, gump.Pages[1].GetElementsByType( ElementType.buttontileart ).Length );
            Assert.Equal( 5, gump.Pages[2].GetElementsByType( ElementType.buttontileart ).Length );

            // Page 1 only goes forward, the last page only goes back.
            GumpElement next = Assert.Single( gump.Pages[1].GetElementsByType( ElementType.button ) );
            Assert.Equal( (int) GumpButtonType.Page, next.ButtonType );
            Assert.Equal( 2, next.Param );
            Assert.Equal( 0x26AF, next.InactiveID );

            GumpElement previous = Assert.Single( gump.Pages[2].GetElementsByType( ElementType.button ) );
            Assert.Equal( (int) GumpButtonType.Page, previous.ButtonType );
            Assert.Equal( 1, previous.Param );
            Assert.Equal( 0x26B5, previous.InactiveID );
        }

        [Fact( Skip = "BUG: ItemArrayGump adds a next-page button to a page that does not exist when the count is a multiple of 20 " )]
        public void WontAddNextPageButtonWhenItemsFillExactlyOnePage()
        {
            List<Item> items = AddItems( 20 );

            Gump gump = Compile( new ItemArrayGump( items, 0xFD000001, false, 0, 0, false ) );

            Assert.Equal( new[] { 0, 1 }, gump.Pages.Select( p => p.Page ) );
            Assert.Empty( gump.Pages[1].GetElementsByType( ElementType.button ) );
        }

        [Fact]
        public void WillSizeFixedItemArrayGumpToFullGrid()
        {
            List<Item> items = AddItems( 1 );

            Gump gump = Compile( new ItemArrayGump( items, 0xFD000001, false, 0, 0, true ) );

            // Four columns by five rows regardless of the item count.
            Assert.Equal( 390, gump.Pages[0].GumpElements[0].Width );
            Assert.Equal( 370, gump.Pages[0].GumpElements[1].Width );
            Assert.Equal( 390, gump.Pages[0].GumpElements[1].Height );
        }

        [Fact]
        public void WillAddCheckboxesAndOkButtonForMultiSelect()
        {
            List<Item> items = AddItems( 2 );

            ItemArrayGump builder = new ItemArrayGump( items, 0xFD000001, true, 0, 0, false );

            // Checkbox per item, keyed by serial, next to its cell.
            GumpElement[] checks = builder.GumpElements.Where( e => e.Type == ElementType.checkbox ).ToArray();
            Assert.Equal( items.Select( i => i.Serial ), checks.Select( c => c.ElementID ) );
            Assert.Equal( new[] { 110, 240 }, checks.Select( c => c.X ) );

            Gump gump = Compile( builder );

            // Cells are 130 wide in multi select; the OK button replies with id 0.
            Assert.Equal( 290, gump.Pages[0].GumpElements[0].Width );
            GumpElement ok = Assert.Single( gump.Pages[1].GetElementsByType( ElementType.button ) );
            Assert.Equal( 0, ok.ElementID );
            Assert.Equal( (int) GumpButtonType.Reply, ok.ButtonType );
            Assert.Equal( 170, ok.X );
        }

        [Fact]
        public void WillAddItemPropertiesWhenTooltipsAreEnabled()
        {
            Engine.CharacterListFlags = CharacterListFlags.PaladinNecromancerClassTooltips;
            List<Item> items = AddItems( 2 );

            Gump gump = Compile( new ItemArrayGump( items, 0xFD000001, false, 0, 0, false ) );

            Assert.Equal( items.Select( i => i.Serial ),
                gump.Pages[1].GetElementsByType( ElementType.itemproperty ).Select( e => e.Serial ) );
        }

        [Fact]
        public async Task WillReturnClickedItemSerialFromItemArrayGump()
        {
            List<Item> items = AddItems( 3 );

            int[] result = await AnswerPromptAsync(
                () => ItemArrayGump.SendGump( items.Select( i => (object) i.Serial ).ToArray(), false, 0, 0, false,
                    5000 ), g => GumpPackets.BuildResponse( g.Serial, g.GumpId, items[1].Serial ) );

            Assert.Equal( new[] { items[1].Serial }, result );
        }

        [Fact]
        public async Task WillReturnCheckedSerialsFromMultiSelectItemArrayGump()
        {
            List<Item> items = AddItems( 3 );

            int[] result = await AnswerPromptAsync(
                () => ItemArrayGump.SendGump( items.Select( i => (object) i.Serial ).ToArray(), true, 0, 0, false,
                    5000 ),
                g => GumpPackets.BuildResponse( g.Serial, g.GumpId, 0, new[] { items[0].Serial, items[2].Serial } ) );

            Assert.Equal( new[] { items[0].Serial, items[2].Serial }, result );
        }

        [Fact]
        public async Task WillReturnNothingWhenItemArrayGumpIsClosed()
        {
            List<Item> items = AddItems( 2 );

            int[] result = await AnswerPromptAsync(
                () => ItemArrayGump.SendGump( items.Select( i => (object) i.Serial ).ToArray(), false, 0, 0, false,
                    5000 ), g => GumpPackets.BuildResponse( g.Serial, g.GumpId, 0 ) );

            Assert.Empty( result );
        }

        [Fact]
        public void WillDeriveTheSameItemArrayGumpIdForTheSameItems()
        {
            List<Item> items = AddItems( 2 );
            object[] serials = items.Select( i => (object) i.Serial ).ToArray();

            using PacketRecorder recorder = new PacketRecorder();

            Assert.Empty( ItemArrayGump.SendGump( serials, false, 0, 0, false, 1 ) );
            Assert.Empty( ItemArrayGump.SendGump( serials, false, 0, 0, false, 1 ) );
            Assert.Empty( ItemArrayGump.SendGump( serials, true, 0, 0, false, 1 ) );

            uint[] ids = recorder.ReceivedWithId( 0xB0 ).Select( p => GumpPackets.DecodeGeneric( p ).GumpId )
                .ToArray();

            Assert.Equal( 3, ids.Length );
            Assert.Equal( ids[0], ids[1] );
            Assert.NotEqual( ids[0], ids[2] );
            Assert.All( ids, id => Assert.Equal( 0xFDu, id >> 24 ) );
        }

        [Fact]
        public void WillCloseUnansweredItemArrayGumpOnTimeout()
        {
            List<Item> items = AddItems( 1 );

            using PacketRecorder recorder = new PacketRecorder();

            int[] result = ItemArrayGump.SendGump( new object[] { items[0].Serial }, false, 0, 0, false, 20 );

            Assert.Empty( result );

            uint id = GumpPackets.DecodeGeneric( Assert.Single( recorder.ReceivedWithId( 0xB0 ) ) ).GumpId;

            // One close before sending (any stale copy) and one after the timeout.
            Assert.Equal( 2, recorder.ReceivedWithId( 0xBF ).Count( p => p[4] == 0x04 &&
                                                                        (uint) ( ( p[5] << 24 ) | ( p[6] << 16 ) |
                                                                            ( p[7] << 8 ) | p[8] ) == id ) );
            Assert.False( Engine.Gumps.GetGump( id, out Gump _ ) );
        }

        [Fact]
        public void WillReportNoItemsWithoutSendingItemArrayGump()
        {
            using PacketRecorder recorder = new PacketRecorder();

            int[] result = ItemArrayGump.SendGump( new object[] { 0x40009999, "nosuchalias" }, false, 0, 0, false, 20 );

            Assert.Empty( result );
            Assert.Empty( recorder.ReceivedWithId( 0xB0 ) );
            Assert.Contains( Engine.Journal.GetEntireBuffer(), j => j.Text == Strings.No_items_to_display___ );
        }

        [Fact]
        public void WontThrowWhenItemArrayGumpRespondsAfterCompletion()
        {
            ItemArrayGump gump = new ItemArrayGump( AddItems( 1 ), 0xFD000001, false, 0, 0, false );
            gump.AutoResetEvent.Dispose();

            gump.OnResponse( 5, null );

            Assert.Equal( new[] { 5 }, gump.Result );
        }

        [Theory]
        [InlineData( 1, true )]
        [InlineData( 2, false )]
        [InlineData( 0, false )]
        public async Task WillReturnConfirmPromptAnswer( int button, bool expected )
        {
            bool result = await AnswerPromptAsync( () => ConfirmPromptGump.ConfirmPrompt( "Sure?", false, 5000 ),
                g => GumpPackets.BuildResponse( g.Serial, g.GumpId, button ) );

            Assert.Equal( expected, result );
        }

        [Fact]
        public void WillLayOutConfirmPrompt()
        {
            Gump gump = Compile( new ConfirmPromptGump( "Delete <b>everything</b>?", true ) );

            Assert.True( gump.Closable );
            Assert.Equal( "Delete <b>everything</b>?", gump.GumpElements.Single( e => e.Type == ElementType.htmlgump ).Text );
            Assert.Equal( new[] { 1, 2 },
                gump.GumpElements.Where( e => e.Type == ElementType.button ).Select( e => e.ElementID ) );
        }

        [Fact]
        public void WillReturnFalseWhenConfirmPromptTimesOut()
        {
            using PacketRecorder recorder = new PacketRecorder();

            Assert.False( ConfirmPromptGump.ConfirmPrompt( "Sure?", false, 20 ) );

            uint id = GumpPackets.DecodeGeneric( Assert.Single( recorder.ReceivedWithId( 0xB0 ) ) ).GumpId;
            Assert.False( Engine.Gumps.GetGump( id, out Gump _ ) );
            Assert.Single( recorder.ReceivedWithId( 0xBF ) );
        }

        [Fact]
        public void WillLayOutSelectionPromptWithFirstOptionSelected()
        {
            Gump gump = Compile( new SelectionPromptGump( new[] { "Red", "Green", "Blue" }, "Pick one" ) );

            GumpElement[] radios = gump.GumpElements.Where( e => e.Type == ElementType.radio ).ToArray();
            Assert.Equal( new[] { 0, 1, 2 }, radios.Select( r => r.ElementID ) );
            Assert.Equal( new[] { true, false, false }, radios.Select( r => r.InitialState ) );
            Assert.Equal( new[] { 100, 120, 140 }, radios.Select( r => r.Y ) );

            Assert.Equal( new[] { "Red", "Green", "Blue" },
                gump.GumpElements.Where( e => e.Type == ElementType.text ).Select( e => e.Text ) );
            Assert.Equal( "Pick one", gump.GumpElements.Single( e => e.Type == ElementType.htmlgump ).Text );
        }

        [Fact]
        public async Task WillReturnSelectedOptionFromSelectionPrompt()
        {
            (bool Result, int Index) result = await AnswerPromptAsync(
                () => SelectionPromptGump.SelectionPrompt( new[] { "Red", "Green", "Blue" }, "Pick", false, 5000 ),
                g => GumpPackets.BuildResponse( g.Serial, g.GumpId, 1, new[] { 2 } ) );

            Assert.True( result.Result );
            Assert.Equal( 2, result.Index );
        }

        [Fact]
        public async Task WillReturnDeclineFromSelectionPrompt()
        {
            (bool Result, int Index) result = await AnswerPromptAsync(
                () => SelectionPromptGump.SelectionPrompt( new[] { "Red", "Green" }, "Pick", false, 5000 ),
                g => GumpPackets.BuildResponse( g.Serial, g.GumpId, 0 ) );

            Assert.False( result.Result );
            Assert.Equal( 0, result.Index );
        }

        [Fact]
        public async Task WillReturnTypedTextFromMessagePrompt()
        {
            (bool Result, string Message) result = await AnswerPromptAsync(
                () => MessagePromptGump.MessagePrompt( "Name?", "initial", false, 5000 ),
                g =>
                {
                    Assert.Contains( "initial", g.Strings );
                    Assert.Contains( "{ textentry 13 135 280 29 0 1 ", g.Layout );

                    return GumpPackets.BuildResponse( g.Serial, g.GumpId, 1, null, ( 1, "Lord British" ) );
                } );

            Assert.True( result.Result );
            Assert.Equal( "Lord British", result.Message );
        }

        [Fact]
        public async Task WillReturnEmptyMessageWhenMessagePromptHasNoTextEntries()
        {
            (bool Result, string Message) result = await AnswerPromptAsync(
                () => MessagePromptGump.MessagePrompt( "Name?", "", false, 5000 ),
                g => GumpPackets.BuildResponse( g.Serial, g.GumpId, 2 ) );

            Assert.False( result.Result );
            Assert.Equal( string.Empty, result.Message );
        }

        [Fact]
        public void WillLayOutMacrosGumpWithOneRowPerRunningMacro()
        {
            Options.CurrentOptions.MacrosGumpTransparent = false;
            Options.CurrentOptions.MacrosGumpWidth = 200;
            Options.CurrentOptions.MacrosGumpHeight = 150;
            Options.CurrentOptions.MacrosGumpTextColor = "#FF0000";
            Options.CurrentOptions.MacrosGumpX = 11;
            Options.CurrentOptions.MacrosGumpY = 22;

            MacroEntry plain = new MacroEntry { Name = "GumpBuilderTests Heal" };
            MacroEntry background = new MacroEntry { Name = "GumpBuilderTests Loot", IsBackground = true };

            MacrosGump builder = new MacrosGump( new[] { plain, background } );
            Gump gump = Compile( builder );

            Assert.Equal( 11, gump.X );
            Assert.Equal( 22, gump.Y );
            Assert.False( gump.Closable );

            GumpElement frame = gump.GumpElements.First( e => e.Type == ElementType.resizepic );
            Assert.Equal( 3500, frame.ElementID );
            Assert.Equal( 200, frame.Width );

            GumpElement[] rows = gump.GumpElements.Where( e => e.Type == ElementType.htmlgump ).ToArray();
            Assert.Equal( new[] { 20, 40 }, rows.Select( r => r.Y ) );
            Assert.Equal( "<BASEFONT face=Arial color=#FF0000>GumpBuilderTests Heal</BASEFONT>\n", rows[0].Text );
            Assert.Equal( "<BASEFONT face=Arial color=#FF0000><I>GumpBuilderTests Loot</I></BASEFONT>\n",
                rows[1].Text );

            // Stop buttons are numbered from 10 in row order; the rest is the reposition button.
            GumpElement[] stops = gump.GumpElements.Where( e => e.Type == ElementType.button && e.ElementID < 100 )
                .ToArray();
            Assert.Equal( new[] { 10, 11 }, stops.Select( b => b.ElementID ) );
            Assert.All( stops, b => Assert.Equal( 170, b.X ) );
        }

        [Fact]
        public void WillUseTransparentBackgroundForMacrosGumpWhenConfigured()
        {
            Options.CurrentOptions.MacrosGumpTransparent = true;

            Gump gump = Compile( new MacrosGump( Array.Empty<MacroEntry>() ) );

            Assert.DoesNotContain( gump.GumpElements, e => e.Type == ElementType.resizepic );
            Assert.Contains( gump.GumpElements, e => e.Type == ElementType.checkertrans );
        }

        [Fact]
        public void WillListAtMostEightMacrosInMacrosGump()
        {
            MacroEntry[] macros = Enumerable.Range( 0, 10 )
                .Select( i => new MacroEntry { Name = $"GumpBuilderTests Many {i}" } ).ToArray();

            MacrosGump gump = new MacrosGump( macros );

            Assert.Equal( 8, gump.GumpElements.Count( e => e.Type == ElementType.button ) );
        }

        [Fact]
        public void WillStopTheMacroWhoseButtonWasPressed()
        {
            MacroEntry first = new MacroEntry { Name = "GumpBuilderTests Stop A", IsRunning = true };
            MacroEntry second = new MacroEntry { Name = "GumpBuilderTests Stop B", IsRunning = true };

            MacrosGump gump = new MacrosGump( new[] { first, second } );

            gump.OnResponse( 11, null );

            Assert.True( first.IsRunning );
            Assert.False( second.IsRunning );

            // An id outside the macro rows stops nothing.
            gump.OnResponse( 12, null );
            Assert.True( first.IsRunning );
        }

        /// <summary>Art's static configuration, so a test can point it elsewhere and put it back.</summary>
        private sealed class ArtState
        {
            private static readonly string[] _fields = { "_dataPath", "_isUOPFormat", "_lazyIndex" };
            private readonly object[] _values;

            private ArtState( object[] values )
            {
                _values = values;
            }

            public static ArtState Capture()
            {
                return new ArtState( _fields.Select( f => Field( f ).GetValue( null ) ).ToArray() );
            }

            public void Restore()
            {
                for ( int i = 0; i < _fields.Length; i++ )
                {
                    Field( _fields[i] ).SetValue( null, _values[i] );
                }
            }

            private static System.Reflection.FieldInfo Field( string name )
            {
                return typeof( Art ).GetField( name,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static );
            }
        }
    }
}
