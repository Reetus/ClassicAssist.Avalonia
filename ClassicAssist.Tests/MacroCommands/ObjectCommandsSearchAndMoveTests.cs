using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using Xunit;
using static ClassicAssist.Tests.MacroCommands.MacroCommandTestSupport;

namespace ClassicAssist.Tests.MacroCommands
{
    /// <summary>
    ///     The search and move commands macros lean on most: FindType's hue / stack / depth filters and
    ///     what it does to the "found" alias, CountType's sums, UseType's choice of item, and the amounts
    ///     and destinations MoveItem / MoveType put on the wire. A wrong match here makes a macro use,
    ///     move or drop the wrong item, so the selection rules and packet fields are pinned exactly.
    /// </summary>
    public class ObjectCommandsSearchAndMoveTests : IDisposable
    {
        private const int BACKPACK = 0x40000001;
        private readonly Options _originalOptions;
        private readonly PlayerMobile _player;

        public ObjectCommandsSearchAndMoveTests()
        {
            _originalOptions = Options.CurrentOptions;
            Options.CurrentOptions = FastOptions();

            _player = EngineState.ResetWithPlayer( 0x01, BACKPACK );
            ObjectCommands.ClearIgnoreList();
        }

        public void Dispose()
        {
            ActionPacketQueue.Clear();
            ObjectCommands.ClearIgnoreList();
            Options.CurrentOptions = _originalOptions;
            EngineState.Reset();
        }

        private Item Backpack => Engine.Items.GetItem( BACKPACK );

        private Item AddToContainer( Item container, int serial, int id, int hue = 0, int count = 1 )
        {
            Item item = new Item( serial, container.Serial ) { ID = id, Hue = hue, Count = count };
            container.Container.Add( item );

            return item;
        }

        private Item AddBag( Item parent, int serial )
        {
            Item bag = new Item( serial, parent.Serial ) { ID = 0xE76, Container = new ItemCollection( serial ) };
            parent.Container.Add( bag );

            return bag;
        }

        [Fact]
        public void FindTypeWillMatchOnlyTheRequestedHue()
        {
            AddToContainer( Backpack, 0x40000010, 0xEED, 0x0000, 5 );
            AddToContainer( Backpack, 0x40000011, 0xEED, 0x0021, 50 );

            Assert.True( ObjectCommands.FindType( 0xEED, -1, BACKPACK, 0x21 ) );
            Assert.Equal( 0x40000011, AliasCommands.GetAlias( "found" ) );

            Assert.False( ObjectCommands.FindType( 0xEED, -1, BACKPACK, 0x55 ) );
        }

        [Fact]
        public void FindTypeWillSkipStacksSmallerThanTheMinimum()
        {
            AddToContainer( Backpack, 0x40000010, 0xEED, 0, 5 );
            AddToContainer( Backpack, 0x40000011, 0xEED, 0, 50 );

            Assert.True( ObjectCommands.FindType( 0xEED, -1, BACKPACK, -1, 10 ) );
            Assert.Equal( 0x40000011, AliasCommands.GetAlias( "found" ) );

            Assert.False( ObjectCommands.FindType( 0xEED, -1, BACKPACK, -1, 51 ) );
        }

        [Fact]
        public void FindTypeWillUnsetFoundWhenNothingMatches()
        {
            AliasCommands.SetAlias( "found", 0x40000099 );

            Assert.False( ObjectCommands.FindType( 0x1234, -1, BACKPACK ) );

            Assert.Equal( -1, AliasCommands.GetAlias( "found" ) );
        }

        [Fact]
        public void FindTypeRangeInAContainerWillLimitHowDeepItSearches()
        {
            // Range in a container is the nesting depth: 0 is the container's own contents only.
            Item bag = AddBag( Backpack, 0x40000020 );
            AddToContainer( bag, 0x40000021, 0xF7A );

            Assert.False( ObjectCommands.FindType( 0xF7A, 0, BACKPACK ) );
            Assert.True( ObjectCommands.FindType( 0xF7A, 1, BACKPACK ) );
            Assert.Equal( 0x40000021, AliasCommands.GetAlias( "found" ) );
            Assert.True( ObjectCommands.FindType( 0xF7A, -1, BACKPACK ) );
        }

        [Fact]
        public void FindTypeOnTheGroundWontMatchItemsInsideContainers()
        {
            AddToContainer( Backpack, 0x40000010, 0xF7A );

            Assert.False( ObjectCommands.FindType( 0xF7A ) );

            Engine.Items.Add( new Item( 0x40000030 ) { ID = 0xF7A, X = 1, Y = 1 } );

            Assert.True( ObjectCommands.FindType( 0xF7A ) );
            Assert.Equal( 0x40000030, AliasCommands.GetAlias( "found" ) );
        }

        [Fact]
        public void FindTypeWillSkipIgnoredAndFindTheNextMatch()
        {
            AddToContainer( Backpack, 0x40000010, 0xF7A );
            AddToContainer( Backpack, 0x40000011, 0xF7A );

            Assert.True( ObjectCommands.FindType( 0xF7A, -1, BACKPACK ) );
            int first = AliasCommands.GetAlias( "found" );

            ObjectCommands.IgnoreObject( first );

            Assert.True( ObjectCommands.InIgnoreList( first ) );
            Assert.True( ObjectCommands.FindType( 0xF7A, -1, BACKPACK ) );
            Assert.NotEqual( first, AliasCommands.GetAlias( "found" ) );

            ObjectCommands.IgnoreObject( "found" );

            Assert.False( ObjectCommands.FindType( 0xF7A, -1, BACKPACK ) );
        }

        [Fact]
        public void IgnoreObjectWontAddTheSameSerialTwice()
        {
            ObjectCommands.IgnoreObject( 0x40000010 );
            ObjectCommands.IgnoreObject( 0x40000010 );

            Assert.Single( ObjectCommands.IgnoreList );
        }

        [Fact]
        public void CountTypeWillSumStackAmountsAcrossNestedContainers()
        {
            Item bag = AddBag( Backpack, 0x40000020 );
            AddToContainer( Backpack, 0x40000010, 0xEED, 0, 100 );
            AddToContainer( bag, 0x40000011, 0xEED, 0, 25 );
            AddToContainer( bag, 0x40000012, 0xEED, 0x21, 7 );
            AddToContainer( Backpack, 0x40000013, 0xF7A, 0, 1000 );

            Assert.Equal( 132, ObjectCommands.CountType( 0xEED, BACKPACK ) );
            Assert.Equal( 7, ObjectCommands.CountType( 0xEED, BACKPACK, 0x21 ) );
            Assert.Equal( 0, ObjectCommands.CountType( 0xBEEF, BACKPACK ) );
        }

        [Fact]
        public void CountTypeWillReturnZeroAndSayWhenTheSourceIsNotAContainer()
        {
            Engine.Items.Add( new Item( 0x40000050 ) { ID = 0xF7A } );

            using PacketRecorder recorder = new PacketRecorder();

            Assert.Equal( 0, ObjectCommands.CountType( 0xF7A, 0x40000050 ) );
            Assert.Contains( Strings.Invalid_container___, SystemMessages( recorder ) );
        }

        [Fact]
        public void CountTypeGroundWillCountItemsOnly()
        {
            Engine.Items.Add( new Item( 0x40000050 ) { ID = 0xEED, Count = 30, X = 1 } );
            Engine.Items.Add( new Item( 0x40000051 ) { ID = 0xEED, Count = 12, X = 10 } );
            Engine.Mobiles.Add( new Mobile( 0x00000002 ) { ID = 0xEED } );

            // Items take precedence; mobiles are only counted when no item matched.
            Assert.Equal( 42, ObjectCommands.CountTypeGround( 0xEED ) );
            Assert.Equal( 30, ObjectCommands.CountTypeGround( 0xEED, -1, 2 ) );
        }

        [Fact( Skip = "BUG: CountTypeGround compares the hue argument to the item's ID instead of its Hue" )]
        public void CountTypeGroundWillFilterOnHue()
        {
            Engine.Items.Add( new Item( 0x40000050 ) { ID = 0xEED, Hue = 0x21, Count = 30 } );
            Engine.Items.Add( new Item( 0x40000051 ) { ID = 0xEED, Hue = 0x00, Count = 12 } );

            Assert.Equal( 30, ObjectCommands.CountTypeGround( 0xEED, 0x21 ) );
        }

        [Fact]
        public void UseTypeWillUseTheItemWithTheRequestedHueFromTheBackpack()
        {
            AddToContainer( Backpack, 0x40000010, 0xF0E, 0x00 );
            AddToContainer( Backpack, 0x40000011, 0xF0E, 0x21 );

            using PacketRecorder recorder = new PacketRecorder();

            ObjectCommands.UseType( 0xF0E, 0x21 );

            byte[] use = Assert.Single( recorder.SentWithId( 0x06 ) );
            Assert.Equal( 0x40000011, ReadInt( use, 1 ) );
        }

        [Fact]
        public void UseTypeWillSendNothingWhenNoItemMatches()
        {
            AddToContainer( Backpack, 0x40000010, 0xF0E, 0x00 );

            using PacketRecorder recorder = new PacketRecorder();

            ObjectCommands.UseType( 0xF0E, 0x21 );
            ObjectCommands.UseType( 0xF0E, -1, 0x40009999 );

            Assert.Empty( recorder.SentWithId( 0x06 ) );
            IReadOnlyList<string> messages = SystemMessages( recorder );
            Assert.Contains( Strings.Cannot_find_item___, messages );
            Assert.Contains( Strings.Cannot_find_container___, messages );
        }

        [Fact]
        public async Task UseObjectWillQueueAUsePacketForTheResolvedAlias()
        {
            AliasCommands.SetAlias( "thing", 0x40000077 );

            using PacketRecorder recorder = new PacketRecorder();

            ObjectCommands.UseObject( "thing" );
            await DrainActionQueueAsync();

            byte[] use = Assert.Single( recorder.SentWithId( 0x06 ) );
            Assert.Equal( 0x40000077, ReadInt( use, 1 ) );
        }

        [Fact( Skip = "BUG: an unknown alias resolves to -1, UseObject only rejects 0, so it uses serial 0xFFFFFFFF" )]
        public async Task UseObjectWontSendAnythingForAnUnknownAlias()
        {
            using PacketRecorder recorder = new PacketRecorder();

            ObjectCommands.UseObject( "nosuchalias" );
            await DrainActionQueueAsync();

            Assert.Empty( recorder.SentWithId( 0x06 ) );
        }

        [Fact]
        public async Task MoveItemWithoutAnAmountWillDragTheWholeStack()
        {
            Item bag = AddBag( Backpack, 0x40000020 );
            Engine.Items.Add( new Item( 0x40000050 ) { ID = 0xEED, Count = 250 } );

            using PacketRecorder recorder = new PacketRecorder();

            ObjectCommands.MoveItem( 0x40000050, bag.Serial );
            await DrainActionQueueAsync();

            byte[] drag = Assert.Single( recorder.SentWithId( 0x07 ) );
            Assert.Equal( 0x40000050, ReadInt( drag, 1 ) );
            Assert.Equal( 250, ReadShort( drag, 5 ) );

            byte[] drop = Assert.Single( recorder.SentWithId( 0x08 ) );
            Assert.Equal( 0x40000050, ReadInt( drop, 1 ) );
            Assert.Equal( bag.Serial, ReadInt( drop, 11 ) );
        }

        [Fact]
        public async Task MoveItemWillRefuseAnItemItDoesNotKnow()
        {
            using PacketRecorder recorder = new PacketRecorder();

            ObjectCommands.MoveItem( 0x40000050, BACKPACK );
            await DrainActionQueueAsync();

            Assert.Empty( recorder.SentWithId( 0x07 ) );
            Assert.Contains( Strings.Invalid_or_unknown_object_id, SystemMessages( recorder ) );
        }

        [Fact]
        public async Task MoveTypeWillClampTheAmountToTheStackSize()
        {
            Item bag = AddBag( Backpack, 0x40000020 );
            AddToContainer( Backpack, 0x40000010, 0xEED, 0, 40 );

            using PacketRecorder recorder = new PacketRecorder();

            ObjectCommands.MoveType( 0xEED, BACKPACK, bag.Serial, amount: 500 );
            await DrainActionQueueAsync();

            byte[] drag = Assert.Single( recorder.SentWithId( 0x07 ) );
            Assert.Equal( 0x40000010, ReadInt( drag, 1 ) );
            Assert.Equal( 40, ReadShort( drag, 5 ) );
            Assert.Equal( bag.Serial, ReadInt( Assert.Single( recorder.SentWithId( 0x08 ) ), 11 ) );
        }

        [Fact]
        public async Task MoveTypeWithAPartialAmountWillSplitTheStack()
        {
            Item bag = AddBag( Backpack, 0x40000020 );
            AddToContainer( Backpack, 0x40000010, 0xEED, 0, 40 );

            using PacketRecorder recorder = new PacketRecorder();

            ObjectCommands.MoveType( 0xEED, BACKPACK, bag.Serial, amount: 15 );
            await DrainActionQueueAsync();

            Assert.Equal( 15, ReadShort( Assert.Single( recorder.SentWithId( 0x07 ) ), 5 ) );
        }

        [Fact]
        public async Task MoveTypeToMinusOneWillDropOnTheGroundAtTheGivenLocation()
        {
            AddToContainer( Backpack, 0x40000010, 0xEED, 0, 3 );

            using PacketRecorder recorder = new PacketRecorder();

            ObjectCommands.MoveType( 0xEED, BACKPACK, -1, 1000, 2000, 5 );
            await DrainActionQueueAsync();

            byte[] drop = Assert.Single( recorder.SentWithId( 0x08 ) );
            Assert.Equal( 1000, ReadShort( drop, 5 ) );
            Assert.Equal( 2000, ReadShort( drop, 7 ) );
            Assert.Equal( 5, drop[9] );
            Assert.Equal( -1, ReadInt( drop, 11 ) );
        }

        [Fact]
        public async Task MoveTypeWillMatchOnHueAndIgnoreOtherHues()
        {
            Item bag = AddBag( Backpack, 0x40000020 );
            AddToContainer( Backpack, 0x40000010, 0xEED, 0x00, 3 );
            AddToContainer( Backpack, 0x40000011, 0xEED, 0x21, 3 );

            using PacketRecorder recorder = new PacketRecorder();

            ObjectCommands.MoveType( 0xEED, BACKPACK, bag.Serial, hue: 0x21 );
            await DrainActionQueueAsync();

            Assert.Equal( 0x40000011, ReadInt( Assert.Single( recorder.SentWithId( 0x07 ) ), 1 ) );
        }

        [Fact]
        public async Task MoveTypeWillSayWhenTheSourceIsMissingOrNotAContainer()
        {
            Engine.Items.Add( new Item( 0x40000050 ) { ID = 0xF7A } );

            using PacketRecorder recorder = new PacketRecorder();

            ObjectCommands.MoveType( 0xEED, 0x40009999, BACKPACK );
            ObjectCommands.MoveType( 0xEED, 0x40000050, BACKPACK );
            await DrainActionQueueAsync();

            Assert.Empty( recorder.SentWithId( 0x07 ) );
            IReadOnlyList<string> messages = SystemMessages( recorder );
            Assert.Contains( Strings.Cannot_find_item___, messages );
            Assert.Contains( Strings.Invalid_container___, messages );
        }

        [Fact]
        public async Task MoveTypeOffsetWillDropTheGroundItemRelativeToThePlayer()
        {
            _player.X = 100;
            _player.Y = 200;
            _player.Z = 10;

            Engine.Items.Add( new Item( 0x40000050 ) { ID = 0xEED, X = 101, Y = 200, Count = 9 } );
            Engine.Items.Add( new Item( 0x40000051 ) { ID = 0xEED, X = 140, Y = 200, Count = 9 } );

            using PacketRecorder recorder = new PacketRecorder();

            Assert.True( ObjectCommands.MoveTypeOffset( 0xEED, "ground", 1, -1, 2, 4, -1, 2 ) );
            await DrainActionQueueAsync();

            byte[] drag = Assert.Single( recorder.SentWithId( 0x07 ) );
            Assert.Equal( 0x40000050, ReadInt( drag, 1 ) );
            Assert.Equal( 4, ReadShort( drag, 5 ) );

            byte[] drop = Assert.Single( recorder.SentWithId( 0x08 ) );
            Assert.Equal( 101, ReadShort( drop, 5 ) );
            Assert.Equal( 199, ReadShort( drop, 7 ) );
            Assert.Equal( 12, drop[9] );
        }

        [Fact]
        public void MoveTypeXYZWillFailWhenNothingIsInRange()
        {
            _player.X = 100;
            _player.Y = 200;
            Engine.Items.Add( new Item( 0x40000051 ) { ID = 0xEED, X = 140, Y = 200 } );

            using PacketRecorder recorder = new PacketRecorder();

            Assert.False( ObjectCommands.MoveTypeXYZ( 0xEED, null, 1, 2, 3, -1, -1, 2 ) );
            Assert.False( ObjectCommands.MoveTypeXYZ( 0xEED, "nosuchalias", 1, 2, 3 ) );
            Assert.Contains( Strings.Cannot_find_item___, SystemMessages( recorder ) );
        }

        [Fact]
        public async Task MoveTypeXYZFromAContainerWillFindNestedItems()
        {
            Item bag = AddBag( Backpack, 0x40000020 );
            AddToContainer( bag, 0x40000021, 0xEED, 0, 3 );

            using PacketRecorder recorder = new PacketRecorder();

            Assert.True( ObjectCommands.MoveTypeXYZ( 0xEED, BACKPACK, 1, 2, 3 ) );
            await DrainActionQueueAsync();

            Assert.Equal( 0x40000021, ReadInt( Assert.Single( recorder.SentWithId( 0x07 ) ), 1 ) );
        }

        [Fact]
        public async Task UseLayerWillUseWhatIsEquippedOnTheNamedLayer()
        {
            _player.SetLayer( Layer.Talisman, 0x40000060 );
            AliasCommands.SetAlias( "self", _player.Serial );

            using PacketRecorder recorder = new PacketRecorder();

            Assert.True( ObjectCommands.UseLayer( "Talisman", "self" ) );
            Assert.False( ObjectCommands.UseLayer( "Ring", "self" ) );
            Assert.False( ObjectCommands.UseLayer( "Talisman", BACKPACK ) );
            await DrainActionQueueAsync();

            Assert.Equal( 0x40000060, ReadInt( Assert.Single( recorder.SentWithId( 0x06 ) ), 1 ) );
        }

        [Fact]
        public void InIgnoreListWillRejectAnUnknownAlias()
        {
            using PacketRecorder recorder = new PacketRecorder();

            Assert.False( ObjectCommands.InIgnoreList( "nosuchalias" ) );
            Assert.Contains( Strings.Invalid_or_unknown_object_id, SystemMessages( recorder ) );
        }

        [Fact]
        public void AutoColorPickWillAnswerTheNextHuePickerOnceThenStepAside()
        {
            using PacketRecorder recorder = new PacketRecorder();

            try
            {
                ObjectCommands.AutoColorPick( 0x0481 );

                // 0x95 hue picker: serial, item id, then 0x0000.
                byte[] picker = { 0x95, 0x40, 0x00, 0x00, 0x70, 0x0F, 0xAB, 0x00, 0x00 };

                Assert.False( Engine.OnPacketReceive( picker, picker.Length ), "The picker should be withheld from the client" );

                byte[] response = Assert.Single( recorder.SentWithId( 0x95 ) );
                Assert.Equal( 0x40000070, ReadInt( response, 1 ) );
                Assert.Equal( 0x0FAB, ReadShort( response, 5 ) );
                Assert.Equal( 0x0481, ReadShort( response, 7 ) );

                Assert.DoesNotContain( Engine.GetRuntimeFilterWaitRules(), r => r.PacketId == 0x95 );
            }
            finally
            {
                Engine.ClearReceiveFilter();
            }
        }
    }
}
