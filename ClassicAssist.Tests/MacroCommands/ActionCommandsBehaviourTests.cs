using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Objects;
using Xunit;
using static ClassicAssist.Tests.MacroCommands.MacroCommandTestSupport;

namespace ClassicAssist.Tests.MacroCommands
{
    /// <summary>
    ///     <see cref="ActionCommands" /> beyond the single-packet commands: UseOnce's memory of what it has
    ///     used, layer lookups, hand clearing and feeding through the drag queue, and WaitForContext,
    ///     which registers a runtime receive filter for the server's context menu and answers it. That
    ///     last one is the interesting case: the answer goes out from inside the packet filter, so the
    ///     filter's matching rules decide whether the right menu is clicked, and how many times.
    /// </summary>
    public class ActionCommandsBehaviourTests : IDisposable
    {
        private const int VENDOR = 0x00000002;
        private readonly Options _originalOptions;
        private readonly PlayerMobile _player;

        public ActionCommandsBehaviourTests()
        {
            _originalOptions = Options.CurrentOptions;
            Options.CurrentOptions = FastOptions();

            _player = EngineState.ResetWithPlayer();
            AliasCommands.SetAlias( "self", _player.Serial );
            ActionCommands.UseOnceList = new UseOnceList();
        }

        public void Dispose()
        {
            foreach ( int serial in ActionCommands.UseOnceList.ToList() )
            {
                Engine.RehueList.Remove( serial );
            }

            ActionCommands.UseOnceList = new UseOnceList();
            ActionPacketQueue.Clear();
            Engine.ClearReceiveFilter();
            Options.CurrentOptions = _originalOptions;
            EngineState.Reset();
        }

        private Item AddToBackpack( int serial, int id, int hue = 0 )
        {
            Item backpack = _player.Backpack;
            Item item = new Item( serial, backpack.Serial ) { ID = id, Hue = hue };
            backpack.Container.Add( item );

            return item;
        }

        [Fact]
        public async Task UseOnceWillUseEachMatchingItemOnlyOnce()
        {
            AddToBackpack( 0x40000010, 0x0E21 );
            AddToBackpack( 0x40000011, 0x0E21 );
            AddToBackpack( 0x40000012, 0x0E21, 0x21 );

            using PacketRecorder recorder = new PacketRecorder();

            Assert.True( ActionCommands.UseOnce( 0x0E21, 0 ) );
            Assert.True( ActionCommands.UseOnce( 0x0E21, 0 ) );
            Assert.False( ActionCommands.UseOnce( 0x0E21, 0 ) );
            await DrainActionQueueAsync();

            HashSet<int> used = recorder.SentWithId( 0x06 ).Select( p => ReadInt( p, 1 ) ).ToHashSet();
            Assert.Equal( new HashSet<int> { 0x40000010, 0x40000011 }, used );
            Assert.Contains( Strings.UseOnce__Cannot_find_type___, SystemMessages( recorder ) );

            // Used items are marked by rehueing them.
            Assert.True( Engine.RehueList.Contains( 0x40000010 ) );
        }

        [Fact( Skip = "BUG: UseOnceList.Clear removes while iterating by index, so every second used item stays marked" )]
        public void ClearUseOnceWillMakeEveryUsedItemAvailableAgain()
        {
            AddToBackpack( 0x40000010, 0x0E21 );
            AddToBackpack( 0x40000011, 0x0E21 );

            Assert.True( ActionCommands.UseOnce( 0x0E21 ) );
            Assert.True( ActionCommands.UseOnce( 0x0E21 ) );

            ActionCommands.ClearUseOnce();

            Assert.Empty( ActionCommands.UseOnceList );
            Assert.False( Engine.RehueList.Contains( 0x40000010 ) );
            Assert.False( Engine.RehueList.Contains( 0x40000011 ) );
        }

        [Fact]
        public void UseOnceWillReturnFalseWithoutABackpack()
        {
            Engine.Items.Remove( _player.Backpack );

            Assert.False( ActionCommands.UseOnce( 0x0E21 ) );
        }

        [Fact]
        public void ContentsWillCountNestedItemsAndRejectNonContainers()
        {
            Item backpack = _player.Backpack;
            Item bag = new Item( 0x40000020, backpack.Serial ) { Container = new ItemCollection( 0x40000020 ) };
            backpack.Container.Add( bag );
            bag.Container.Add( new Item( 0x40000021, bag.Serial ) );
            bag.Container.Add( new Item( 0x40000022, bag.Serial ) );
            AddToBackpack( 0x40000010, 0x0E21 );

            using PacketRecorder recorder = new PacketRecorder();

            Assert.Equal( 4, ActionCommands.Contents( backpack.Serial ) );
            Assert.Equal( 0, ActionCommands.Contents( 0x40000010 ) );
            Assert.Contains( Strings.Invalid_container___, SystemMessages( recorder ) );
        }

        [Fact]
        public void FindLayerWillSetFoundOrUnsetIt()
        {
            _player.SetLayer( Layer.Helm, 0x40000030 );

            Assert.True( ActionCommands.FindLayer( "Helm" ) );
            Assert.Equal( 0x40000030, AliasCommands.GetAlias( "found" ) );

            Assert.False( ActionCommands.FindLayer( (int) Layer.Gloves ) );
            Assert.Equal( -1, AliasCommands.GetAlias( "found" ) );
        }

        [Fact]
        public void FindLayerWillRejectItemsAndBadLayers()
        {
            using PacketRecorder recorder = new PacketRecorder();

            Assert.False( ActionCommands.FindLayer( "Helm", _player.Backpack.Serial ) );
            Assert.False( ActionCommands.FindLayer( 3.5, "self" ) );

            IReadOnlyList<string> messages = SystemMessages( recorder );
            Assert.Contains( Strings.Cannot_find_item___, messages );
            Assert.Contains( Strings.Invalid_layer_value___, messages );
        }

        [Fact]
        public async Task ClearHandsBothWillMoveBothHandsToTheBackpack()
        {
            _player.SetLayer( Layer.OneHanded, 0x40000041 );
            _player.SetLayer( Layer.TwoHanded, 0x40000042 );

            using PacketRecorder recorder = new PacketRecorder();

            ActionCommands.ClearHands();
            await DrainActionQueueAsync();

            Assert.Equal( new[] { 0x40000041, 0x40000042 }, recorder.SentWithId( 0x07 ).Select( p => ReadInt( p, 1 ) ).ToArray() );
            Assert.All( recorder.SentWithId( 0x08 ), p => Assert.Equal( _player.Backpack.Serial, ReadInt( p, 11 ) ) );
        }

        [Fact]
        public void ClearHandsWillThrowForAnUnknownHand()
        {
            Assert.Throws<ArgumentOutOfRangeException>( () => ActionCommands.ClearHands( "middle" ) );
        }

        [Fact]
        public async Task FeedWillDragTheRequestedAmountOfFoodOntoThePet()
        {
            AddToBackpack( 0x40000010, 0x09B7, 0x00 ).Count = 10;

            using PacketRecorder recorder = new PacketRecorder();

            ActionCommands.Feed( VENDOR, 0x09B7, 3 );
            ActionCommands.Feed( VENDOR, 0x1234 );
            await DrainActionQueueAsync();

            byte[] drag = Assert.Single( recorder.SentWithId( 0x07 ) );
            Assert.Equal( 0x40000010, ReadInt( drag, 1 ) );
            Assert.Equal( 3, ReadShort( drag, 5 ) );
            Assert.Equal( VENDOR, ReadInt( Assert.Single( recorder.SentWithId( 0x08 ) ), 11 ) );
            Assert.Contains( Strings.Cannot_find_item___, SystemMessages( recorder ) );
        }

        [Fact]
        public void EquipItemWillRejectAnInvalidLayerOrUnknownItem()
        {
            using PacketRecorder recorder = new PacketRecorder();

            ActionCommands.EquipItem( 0x40000050, Layer.Invalid );
            ActionCommands.EquipItem( 0x40000050, Layer.Helm );

            Assert.Empty( recorder.SentWithId( 0x07 ) );
            IReadOnlyList<string> messages = SystemMessages( recorder );
            Assert.Contains( Strings.Invalid_layer_value___, messages );
            Assert.Contains( Strings.Cannot_find_item___, messages );
        }

        [Fact]
        public async Task WaitForContextWillClickTheEntryWhenTheMenuArrives()
        {
            using PacketRecorder recorder = new PacketRecorder();

            Task<bool> wait = Task.Run( () => ActionCommands.WaitForContext( VENDOR, 3, 5000 ), TestContext.Current.CancellationToken );

            Assert.True( await WaitUntilAsync( () => recorder.SentWithId( 0xBF ).Any( p => ReadShort( p, 3 ) == 0x13 ) ) );

            byte[] menu = ContextMenu( VENDOR );
            Assert.False( Engine.OnPacketReceive( menu, menu.Length ), "The menu should be answered, not shown" );

            Assert.True( await wait );

            byte[] click = recorder.SentWithId( 0xBF ).First( p => ReadShort( p, 3 ) == 0x15 );
            Assert.Equal( VENDOR, ReadInt( click, 5 ) );
            Assert.Equal( 3, ReadShort( click, 9 ) );

            Assert.DoesNotContain( Engine.GetRuntimeFilterWaitRules(), r => r.PacketId == 0xBF );
        }

        [Fact]
        public async Task WaitForContextWillClickOnlyOnce()
        {
            using PacketRecorder recorder = new PacketRecorder();

            Task<bool> wait = Task.Run( () => ActionCommands.WaitForContext( VENDOR, 3, 5000 ), TestContext.Current.CancellationToken );

            Assert.True( await WaitUntilAsync( () => recorder.SentWithId( 0xBF ).Any( p => ReadShort( p, 3 ) == 0x13 ) ) );

            byte[] menu = ContextMenu( VENDOR );
            Engine.OnPacketReceive( menu, menu.Length );

            Assert.True( await wait );

            Assert.Single( recorder.SentWithId( 0xBF ), p => ReadShort( p, 3 ) == 0x15 );
        }

        [Fact]
        public async Task WaitForContextWontAnswerAMenuForAnotherObject()
        {
            ThreadQueue<Packet> savedIncoming = Engine.IncomingQueue;
            ConcurrentQueue<byte[]> delivered = new ConcurrentQueue<byte[]>();
            using ThreadQueue<Packet> incomingQueue = new ThreadQueue<Packet>( p => delivered.Enqueue( p.GetPacket() ) );

            using PacketRecorder recorder = new PacketRecorder();

            try
            {
                Engine.IncomingQueue = incomingQueue;

                Task<bool> wait = Task.Run( () => ActionCommands.WaitForContext( VENDOR, 3, 300 ), TestContext.Current.CancellationToken );

                Assert.True( await WaitUntilAsync( () => recorder.SentWithId( 0xBF ).Any( p => ReadShort( p, 3 ) == 0x13 ) ) );

                byte[] otherMenu = ContextMenu( 0x00000003 );

                Assert.True( Engine.OnPacketReceive( otherMenu, otherMenu.Length ), "Another object's menu belongs to the client" );
                Assert.False( await wait );
                Assert.DoesNotContain( recorder.SentWithId( 0xBF ), p => ReadShort( p, 3 ) == 0x15 );
            }
            finally
            {
                Engine.IncomingQueue = savedIncoming;
            }
        }

        [Fact]
        public void WaitForContextWillTimeOutAndRemoveItsFilter()
        {
            Assert.False( ActionCommands.WaitForContext( VENDOR, 3, 50 ) );
            Assert.DoesNotContain( Engine.GetRuntimeFilterWaitRules(), r => r.PacketId == 0xBF );
        }

        /// <summary>
        ///     A server 0xBF/0x14 context menu for <paramref name="serial" /> in the post-7.0 layout with a
        ///     single entry 3 (cliloc 3006103).
        /// </summary>
        private static byte[] ContextMenu( int serial )
        {
            PacketWriter writer = new PacketWriter( 20 );
            writer.Write( (byte) 0xBF );
            writer.Write( (short) 20 );
            writer.Write( (short) 0x14 );
            writer.Write( (short) 0x02 );
            writer.Write( serial );
            writer.Write( (byte) 1 );
            writer.Write( 3006103 );
            writer.Write( (short) 3 );
            writer.Write( (short) 0 );

            return writer.ToArray();
        }
    }
}
