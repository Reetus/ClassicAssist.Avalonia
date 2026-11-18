using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Targeting;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Objects;
using Xunit;
using static ClassicAssist.Tests.MacroCommands.MacroCommandTestSupport;

namespace ClassicAssist.Tests.MacroCommands
{
    /// <summary>
    ///     What <see cref="TargetCommands" /> does with a live target cursor: the 0x6C it answers with
    ///     (cursor id, flags, tile coordinates), the range check and target queue that can hold the
    ///     answer back, the wait commands that block a macro on a cursor or a fizzle, and the
    ///     Nearest / Previous selection modes of <see cref="TargetManager" />. Getting any of these wrong
    ///     targets the wrong thing or leaves a macro blocked, so the wire bytes and state flags are pinned.
    /// </summary>
    public class TargetCommandsCursorTests : IDisposable
    {
        private const int CURSOR = 0x00ABCDEF;
        private const int MOBILE = 0x00000002;
        private readonly Options _originalOptions;
        private readonly PlayerMobile _player;

        public TargetCommandsCursorTests()
        {
            _originalOptions = Options.CurrentOptions;
            Options.CurrentOptions = FastOptions();

            _player = EngineState.ResetWithPlayer();
            _player.X = 100;
            _player.Y = 100;
            ObjectCommands.ClearIgnoreList();
            ClearTargetManagerCycle();
        }

        public void Dispose()
        {
            Options.CurrentOptions = _originalOptions;
            ClearTargetManagerCycle();
            EngineState.Reset();
        }

        /// <summary>
        ///     Next/Previous remember which mobiles they have already handed out in a private static list,
        ///     which outlives a test; clear it so a cycle starts fresh.
        /// </summary>
        private static void ClearTargetManagerCycle()
        {
            FieldInfo field = typeof( TargetManager ).GetField( "_ignoreList", BindingFlags.NonPublic | BindingFlags.Static );
            ( (List<Mobile>) field?.GetValue( null ) )?.Clear();
        }

        private static void OpenCursor( TargetFlags flags = TargetFlags.Harmful, TargetType type = TargetType.Object )
        {
            Engine.TargetExists = true;
            Engine.TargetSerial = CURSOR;
            Engine.TargetFlags = flags;
            Engine.TargetType = type;
        }

        private static Mobile AddMobile( int serial, int x, int y, Notoriety notoriety = Notoriety.Murderer )
        {
            Mobile mobile = new Mobile( serial ) { ID = 0x190, X = x, Y = y, Notoriety = notoriety, Hits = 25, HitsMax = 25 };
            Engine.Mobiles.Add( mobile );

            return mobile;
        }

        [Fact]
        public void TargetWillAnswerTheCursorWithItsIdAndFlags()
        {
            AddMobile( MOBILE, 101, 100 );
            OpenCursor();

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.Target( MOBILE );

            byte[] target = Assert.Single( recorder.SentWithId( 0x6C ) );
            Assert.Equal( (byte) TargetType.Object, target[1] );
            Assert.Equal( CURSOR, ReadInt( target, 2 ) );
            Assert.Equal( (byte) TargetFlags.Harmful, target[6] );
            Assert.Equal( MOBILE, ReadInt( target, 7 ) );
            Assert.False( Engine.TargetExists );
            Assert.Equal( MOBILE, _player.LastTargetSerial );
        }

        [Fact]
        public void TargetOnATileCursorWillCarryTheEntitysLocationAndGraphic()
        {
            Engine.Items.Add( new Item( 0x40000050 ) { ID = 0x0F7A, X = 120, Y = 130, Z = 7 } );
            OpenCursor( TargetFlags.None, TargetType.Tile );

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.Target( 0x40000050 );

            byte[] target = Assert.Single( recorder.SentWithId( 0x6C ) );
            Assert.Equal( 120, ReadShort( target, 11 ) );
            Assert.Equal( 130, ReadShort( target, 13 ) );
            Assert.Equal( 7, ReadShort( target, 15 ) );
            Assert.Equal( 0x0F7A, ReadShort( target, 17 ) );
        }

        [Fact]
        public void TargetWontSendAnythingForSerialZero()
        {
            OpenCursor();

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.Target( 0 );

            Assert.Empty( recorder.SentWithId( 0x6C ) );
            Assert.True( Engine.TargetExists );
        }

        [Fact]
        public void TargetWithRangeCheckWillRefuseAFarMobileAndReissueTheCursor()
        {
            Options.CurrentOptions.RangeCheckLastTargetAmount = 5;
            AddMobile( MOBILE, 110, 100 );
            OpenCursor();

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.Target( MOBILE, true );

            Assert.Empty( recorder.SentWithId( 0x6C ) );

            byte[] resent = Assert.Single( recorder.ReceivedWithId( 0x6C ) );
            Assert.Equal( CURSOR, ReadInt( resent, 2 ) );
            Assert.Equal( (byte) TargetFlags.Harmful, resent[6] );
            Assert.True( Engine.TargetExists );
            Assert.Contains( Strings.Target_out_of_range__try_again___, SystemMessages( recorder ) );
        }

        [Fact]
        public void TargetWithRangeCheckWillRefuseAMobileItCannotSee()
        {
            OpenCursor();

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.Target( MOBILE, true );

            Assert.Empty( recorder.SentWithId( 0x6C ) );
            Assert.Single( recorder.ReceivedWithId( 0x6C ) );
        }

        [Fact]
        public void TargetWithRangeCheckWillAllowAMobileAtTheLimit()
        {
            Options.CurrentOptions.RangeCheckLastTargetAmount = 5;
            AddMobile( MOBILE, 105, 100 );
            OpenCursor();

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.Target( MOBILE, true );

            Assert.Single( recorder.SentWithId( 0x6C ) );
        }

        [Fact]
        public void TargetWithTheQueueWillHoldTheTargetUntilACursorExists()
        {
            AddMobile( MOBILE, 101, 100 );

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.Target( MOBILE, false, true );

            Assert.Empty( recorder.SentWithId( 0x6C ) );
            Assert.Equal( 1, Engine.LastTargetQueue.Count );
            Assert.Equal( MOBILE, Engine.LastTargetQueue.Peek().Object );

            TargetCommands.ClearTargetQueue();

            Assert.Equal( 0, Engine.LastTargetQueue.Count );
        }

        [Fact]
        public void TargetWithTheQueueWillSendStraightAwayWhenACursorIsUp()
        {
            AddMobile( MOBILE, 101, 100 );
            OpenCursor();

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.Target( MOBILE, false, true );

            Assert.Single( recorder.SentWithId( 0x6C ) );
            Assert.Equal( 0, Engine.LastTargetQueue.Count );
        }

        [Fact]
        public void TargetXYZWillSendATileTargetWithNoSerial()
        {
            OpenCursor( TargetFlags.Beneficial );

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.TargetXYZ( 1000, 2000, -5, 0x1234 );

            byte[] target = Assert.Single( recorder.SentWithId( 0x6C ) );
            Assert.Equal( (byte) TargetType.Tile, target[1] );
            Assert.Equal( (byte) TargetFlags.Beneficial, target[6] );
            Assert.Equal( 0, ReadInt( target, 7 ) );
            Assert.Equal( 1000, ReadShort( target, 11 ) );
            Assert.Equal( 2000, ReadShort( target, 13 ) );
            Assert.Equal( -5, (short) ReadShort( target, 15 ) );
            Assert.Equal( 0x1234, ReadShort( target, 17 ) );
            Assert.False( Engine.TargetExists );
        }

        [Fact]
        public void TargetTileOffsetWillOffsetFromThePlayer()
        {
            _player.Z = 10;
            OpenCursor( TargetFlags.None, TargetType.Tile );

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.TargetTileOffset( -2, 3, 1 );

            byte[] target = Assert.Single( recorder.SentWithId( 0x6C ) );
            Assert.Equal( 98, ReadShort( target, 11 ) );
            Assert.Equal( 103, ReadShort( target, 13 ) );
            Assert.Equal( 11, ReadShort( target, 15 ) );
        }

        [Fact]
        public void CancelTargetWillSendACancelForTheCurrentCursor()
        {
            OpenCursor();

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.CancelTarget();

            byte[] cancel = Assert.Single( recorder.SentWithId( 0x6C ) );
            Assert.Equal( CURSOR, ReadInt( cancel, 2 ) );
            Assert.Equal( (byte) TargetFlags.Cancel, cancel[6] );
        }

        [Fact]
        public void TargetExistsWillMatchTheCursorKind()
        {
            Assert.False( TargetCommands.TargetExists() );

            OpenCursor( TargetFlags.Beneficial );

            Assert.True( TargetCommands.TargetExists() );
            Assert.True( TargetCommands.TargetExists( "Beneficial" ) );
            Assert.False( TargetCommands.TargetExists( "Harmful" ) );
            Assert.False( TargetCommands.TargetExists( "Neutral" ) );
            Assert.True( TargetCommands.TargetExists( "NotAKind" ) );

            Engine.TargetFlags = TargetFlags.None;

            Assert.True( TargetCommands.TargetExists( "Neutral" ) );
        }

        [Fact]
        public void TargetTypeWillTargetTheMatchingItemInTheBackpack()
        {
            Item backpack = _player.Backpack;
            backpack.Container.Add( new Item( 0x40000010, backpack.Serial ) { ID = 0x0F7A, Hue = 0x00 } );
            backpack.Container.Add( new Item( 0x40000011, backpack.Serial ) { ID = 0x0F7A, Hue = 0x21 } );
            OpenCursor( TargetFlags.None );

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.TargetType( 0x0F7A, 0x21 );

            Assert.Equal( 0x40000011, ReadInt( Assert.Single( recorder.SentWithId( 0x6C ) ), 7 ) );
        }

        [Fact]
        public void TargetTypeWillSayWhenItCannotFindTheItemOrTheBackpack()
        {
            OpenCursor( TargetFlags.None );

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.TargetType( 0x0F7A );

            Engine.Items.Remove( _player.Backpack );

            TargetCommands.TargetType( 0x0F7A );

            Assert.Empty( recorder.SentWithId( 0x6C ) );
            IReadOnlyList<string> messages = SystemMessages( recorder );
            Assert.Contains( Strings.Cannot_find_item___, messages );
            Assert.Contains( Strings.Error__Cannot_find_player_backpack, messages );
        }

        [Fact]
        public void TargetGroundWillPreferItemsAndFallBackToMobiles()
        {
            Engine.Items.Add( new Item( 0x40000050 ) { ID = 0x0190, X = 140, Y = 100 } );
            AddMobile( MOBILE, 102, 100 );
            OpenCursor();

            using PacketRecorder recorder = new PacketRecorder();

            // Within 10 tiles the item is out of range, so the mobile with the same graphic is taken.
            TargetCommands.TargetGround( 0x0190, -1, 10 );
            OpenCursor();
            TargetCommands.TargetGround( 0x0190 );

            IReadOnlyList<byte[]> sent = recorder.SentWithId( 0x6C );
            Assert.Equal( 2, sent.Count );
            Assert.Equal( MOBILE, ReadInt( sent[0], 7 ) );
            Assert.Equal( 0x40000050, ReadInt( sent[1], 7 ) );
        }

        [Fact]
        public void SetEnemyWillRefuseUnknownAndIgnoreItems()
        {
            Engine.Items.Add( new Item( 0x40000050 ) );

            using PacketRecorder recorder = new PacketRecorder();

            TargetCommands.SetEnemy( "nosuchalias" );
            TargetCommands.SetEnemy( 0x00000077 );
            TargetCommands.SetEnemy( 0x40000050 );

            Assert.Equal( 0, _player.EnemyTargetSerial );
            Assert.Contains( Strings.Invalid_or_unknown_object_id, SystemMessages( recorder ) );
        }

        [Fact]
        public void SetFriendAndSetLastTargetWillUpdateThePlayerAndAliases()
        {
            AddMobile( MOBILE, 101, 100 );
            Engine.Items.Add( new Item( 0x40000050 ) );

            TargetCommands.SetFriend( MOBILE );

            Assert.Equal( MOBILE, _player.FriendTargetSerial );
            Assert.Equal( MOBILE, AliasCommands.GetAlias( "friend" ) );

            // An item can be the last target, but is not a combatant.
            TargetCommands.SetLastTarget( 0x40000050 );

            Assert.Equal( 0x40000050, _player.LastTargetSerial );
        }

        [Fact]
        public void GetEnemyNearestWillStartClosestThenStepToTheNeighbourOfTheLastEnemy()
        {
            AddMobile( 0x02, 101, 100 );
            AddMobile( 0x03, 110, 100 );
            AddMobile( 0x04, 103, 100 );

            Assert.True( TargetCommands.GetEnemy( new[] { "Murderer" }, "Any", "Nearest" ) );
            Assert.Equal( 0x02, _player.EnemyTargetSerial );

            Assert.True( TargetCommands.GetEnemy( new[] { "Murderer" }, "Any", "Nearest" ) );
            Assert.Equal( 0x04, _player.EnemyTargetSerial );
        }

        [Fact]
        public void GetEnemyPreviousWillVisitEveryCandidateBeforeRepeating()
        {
            AddMobile( 0x02, 101, 100 );
            AddMobile( 0x03, 102, 100 );
            AddMobile( 0x04, 103, 100, Notoriety.Innocent );

            HashSet<int> seen = new HashSet<int>();

            Assert.True( TargetCommands.GetEnemy( new[] { "Murderer" }, "Any", "Previous" ) );
            seen.Add( _player.EnemyTargetSerial );
            Assert.True( TargetCommands.GetEnemy( new[] { "Murderer" }, "Any", "Previous" ) );
            seen.Add( _player.EnemyTargetSerial );

            Assert.Equal( new HashSet<int> { 0x02, 0x03 }, seen );
        }

        [Fact]
        public void GetEnemyWithHumanoidBodyWillSkipMonsters()
        {
            AddMobile( 0x02, 101, 100 ).ID = 0x0009;
            AddMobile( 0x03, 104, 100 );

            Assert.True( TargetCommands.GetEnemy( new[] { "Murderer" }, "Humanoid" ) );
            Assert.Equal( 0x03, _player.EnemyTargetSerial );
        }

        [Fact]
        public void GetEnemyWillFailWhenNothingIsWithinMaxDistance()
        {
            AddMobile( 0x02, 108, 100 );

            Assert.False( TargetCommands.GetEnemy( new[] { "Murderer" }, "Any", "Closest", "Any", 5 ) );
            Assert.True( TargetCommands.GetEnemy( new[] { "Murderer" }, "Any", "Closest", "Any", 8 ) );
        }

        [Fact]
        public async Task WaitForTargetWillReturnWhenTheServerSendsACursor()
        {
            Task<bool> wait = Task.Run( () => TargetCommands.WaitForTarget( 5000 ), TestContext.Current.CancellationToken );

            Assert.True( await WaitUntilAsync( () => Engine.PacketWaitEntries.GetEntries().Length == 2 ) );
            Assert.True( TargetCommands.WaitingForTarget() );

            // A cancel cursor (flags 3) is not a new target request and must not wake the macro.
            Engine.PacketWaitEntries.CheckWait( TargetCursor( 0x03 ), PacketDirection.Incoming );
            Assert.False( wait.IsCompleted );

            Engine.PacketWaitEntries.CheckWait( TargetCursor( 0x01 ), PacketDirection.Incoming );

            Assert.True( await wait );
            Assert.False( Engine.WaitingForTarget );
            Assert.Empty( Engine.PacketWaitEntries.GetEntries() );
        }

        [Fact]
        public void WaitForTargetWillTimeOutAndCleanUp()
        {
            Assert.False( TargetCommands.WaitForTarget( 50 ) );
            Assert.False( Engine.WaitingForTarget );
            Assert.Empty( Engine.PacketWaitEntries.GetEntries() );
        }

        [Fact]
        public async Task WaitForTargetOrFizzleWillReturnFalseWhenTheSpellFizzles()
        {
            Task<bool> wait = Task.Run( () => TargetCommands.WaitForTargetOrFizzle( 5000 ), TestContext.Current.CancellationToken );

            Assert.True( await WaitUntilAsync( () => Engine.PacketWaitEntries.GetEntries().Length > 2 ) );

            // 0xC1 cliloc message 502632 "The spell fizzles.", cliloc number at offset 14.
            byte[] fizzle = new byte[50];
            fizzle[0] = 0xC1;
            fizzle[14] = 502632 >> 24;
            fizzle[15] = ( 502632 >> 16 ) & 0xFF;
            fizzle[16] = ( 502632 >> 8 ) & 0xFF;
            fizzle[17] = 502632 & 0xFF;

            Engine.PacketWaitEntries.CheckWait( fizzle, PacketDirection.Incoming );

            Assert.False( await wait );
            Assert.Empty( Engine.PacketWaitEntries.GetEntries() );
        }

        [Fact]
        public async Task WaitForTargetOrFizzleWillReturnTrueForACursor()
        {
            Task<bool> wait = Task.Run( () => TargetCommands.WaitForTargetOrFizzle( 5000 ), TestContext.Current.CancellationToken );

            Assert.True( await WaitUntilAsync( () => Engine.PacketWaitEntries.GetEntries().Length > 2 ) );

            Engine.PacketWaitEntries.CheckWait( TargetCursor( 0x02 ), PacketDirection.Incoming );

            Assert.True( await wait );
        }

        /// <summary>A server 0x6C target cursor with the given flags byte.</summary>
        private static byte[] TargetCursor( byte flags )
        {
            byte[] packet = new byte[19];
            packet[0] = 0x6C;
            packet[2] = CURSOR >> 24;
            packet[3] = ( CURSOR >> 16 ) & 0xFF;
            packet[4] = ( CURSOR >> 8 ) & 0xFF;
            packet[5] = CURSOR & 0xFF;
            packet[6] = flags;

            return packet;
        }
    }
}
