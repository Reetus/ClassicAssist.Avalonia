using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Skills;
using ClassicAssist.Data.Vendors;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Objects;
using Xunit;
using UOC = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.Tests.Network.Outgoing
{
    /// <summary>
    ///     Pins the exact bytes of the packets <see cref="UOC" /> builds by hand - system and overhead
    ///     messages injected into the client, speech, party and chat messages, vendor buys, the 3D client
    ///     equip packets, target cursors and the drag/drop pair. These are written field by field with
    ///     hand-computed lengths, so each is checked against the protocol layout: id, length prefix,
    ///     big-endian serials, fixed-width ASCII and big-endian unicode text. Client-version dependent
    ///     variants are checked on both sides of the boundary.
    /// </summary>
    public class CommandsPacketTests : IDisposable
    {
        private const int PlayerSerial = 0x00001234;

        private readonly Options _originalOptions;
        private readonly bool _originalQuietMode;

        public CommandsPacketTests()
        {
            EngineState.ResetWithPlayer( PlayerSerial );
            _originalOptions = Options.CurrentOptions;
            _originalQuietMode = MacroManager.QuietMode;
            Options.CurrentOptions = new Options { ActionDelayMS = 0, DragDelay = false };
            MacroManager.QuietMode = false;
        }

        public void Dispose()
        {
            Options.CurrentOptions = _originalOptions;
            MacroManager.QuietMode = _originalQuietMode;
            EngineState.Reset();
        }

        private static byte[] Concat( params byte[][] parts )
        {
            return parts.SelectMany( p => p ).ToArray();
        }

        private static byte[] Ascii( string text, int size )
        {
            byte[] bytes = new byte[size];
            Encoding.ASCII.GetBytes( text, 0, Math.Min( text.Length, size ), bytes, 0 );

            return bytes;
        }

        private static byte[] BigUniNull( string text )
        {
            return Encoding.BigEndianUnicode.GetBytes( text + "\0" );
        }

        private static byte[] U16( int value )
        {
            return new[] { (byte) ( value >> 8 ), (byte) value };
        }

        private static byte[] U32( int value )
        {
            return new[] { (byte) ( value >> 24 ), (byte) ( value >> 16 ), (byte) ( value >> 8 ), (byte) value };
        }

        [Fact]
        public void WillInjectSystemMessageAndJournalIt()
        {
            using PacketRecorder recorder = new PacketRecorder();

            UOC.SystemMessage( "Hi there", 0x0021 );

            byte[] text = BigUniNull( "Hi there" );
            byte[] expected = Concat( new byte[] { 0xAE }, U16( 48 + text.Length ), // id, length
                U32( -1 ), // serial 0xFFFFFFFF: system
                U16( 0xFFFF ), // graphic
                new byte[] { 0x00 }, // type
                U16( 0x0021 ), // hue
                U16( 3 ), // font
                Ascii( Strings.UO_LOCALE, 4 ), // language
                Ascii( "System", 30 ), // name
                text );

            Assert.Equal( expected, Assert.Single( recorder.ReceivedWithId( 0xAE ) ) );
            Assert.Empty( recorder.Sent );
            Assert.Contains( Engine.Journal.GetEntireBuffer(), j => j.Text == "Hi there" );
        }

        [Fact]
        public void WillThrottleOnlyRepeatedSystemMessages()
        {
            string text = $"throttled {Guid.NewGuid()}";
            string other = $"other {Guid.NewGuid()}";

            using PacketRecorder recorder = new PacketRecorder();

            UOC.SystemMessage( text, 0x3B2, false, true );
            UOC.SystemMessage( text, 0x3B2, false, true );
            UOC.SystemMessage( other, 0x3B2, false, true );
            UOC.SystemMessage( text, 0x3B2, false, false );

            // Second identical throttled message is dropped; a different one and an unthrottled one go.
            Assert.Equal( 3, recorder.ReceivedWithId( 0xAE ).Count );
        }

        [Fact]
        public void WontInjectSuppressibleSystemMessageInQuietMode()
        {
            MacroManager.QuietMode = true;

            using PacketRecorder recorder = new PacketRecorder();

            UOC.SystemMessage( "quiet", true );
            UOC.SystemMessage( "loud" );

            byte[] packet = Assert.Single( recorder.ReceivedWithId( 0xAE ) );
            Assert.Equal( BigUniNull( "loud" ), packet.Skip( 48 ).ToArray() );
        }

        [Fact]
        public void WillSendUnicodeSpeechWithoutKeywords()
        {
            using PacketRecorder recorder = new PacketRecorder();

            UOC.Speak( "Bank", 0x0035, JournalSpeech.Yell );

            byte[] text = BigUniNull( "Bank" );
            byte[] expected = Concat( new byte[] { 0xAD }, U16( 12 + text.Length ), // id, length
                new byte[] { (byte) JournalSpeech.Yell }, // type, no keyword flag
                U16( 0x0035 ), // hue
                U16( 3 ), // font
                Ascii( Strings.UO_LOCALE, 4 ), // language
                text );

            Assert.Equal( expected, Assert.Single( recorder.SentWithId( 0xAD ) ) );
        }

        [Fact( Skip = "BUG: Commands.Speak writes the length high byte as (byte)(length << 8), always 0 " )]
        public void WillWriteSpeechLengthAbove255()
        {
            using PacketRecorder recorder = new PacketRecorder();

            UOC.Speak( new string( 'a', 200 ), 0x0035, JournalSpeech.Emote );

            byte[] packet = Assert.Single( recorder.SentWithId( 0xAD ) );

            Assert.Equal( 12 + 201 * 2, packet.Length );
            Assert.Equal( packet.Length, BinaryPrimitives.ReadUInt16BigEndian( packet.AsSpan( 1 ) ) );
        }

        [Theory]
        [InlineData( new[] { 0x123 }, new byte[] { 0x00, 0x11, 0x23 } )]
        [InlineData( new[] { 0x123, 0x456 }, new byte[] { 0x00, 0x21, 0x23, 0x45, 0x60 } )]
        [InlineData( new[] { 0x123, 0x456, 0x789 }, new byte[] { 0x00, 0x31, 0x23, 0x45, 0x67, 0x89 } )]
        [InlineData( new[] { 0x001, 0xFFF }, new byte[] { 0x00, 0x20, 0x01, 0xFF, 0xF0 } )]
        public void WillPackSpeechKeywordsAsTwelveBitValues( int[] keywords, byte[] expected )
        {
            // 12-bit count followed by 12 bits per keyword, padded to a whole byte.
            MethodInfo method = typeof( UOC ).GetMethod( "GetKeywordBytes", BindingFlags.NonPublic | BindingFlags.Static );

            byte[] bytes = (byte[]) method.Invoke( null, new object[] { keywords } );

            Assert.Equal( expected, bytes );
        }

        [Fact]
        public void WillSendPartyMessage()
        {
            using PacketRecorder recorder = new PacketRecorder();

            UOC.PartyMessage( "all kill" );

            byte[] text = BigUniNull( "all kill" );
            byte[] expected = Concat( new byte[] { 0xBF }, U16( 6 + text.Length ), U16( 0x06 ), // party
                new byte[] { 0x04 }, // public message
                text );

            Assert.Equal( expected, Assert.Single( recorder.SentWithId( 0xBF ) ) );
        }

        [Fact]
        public void WillInjectOverheadMessageWithEntityNameAndGraphic()
        {
            Mobile mobile = new Mobile( 0x00005678 ) { ID = 0x0190, Name = "Bob" };
            Engine.Mobiles.Add( mobile );

            using PacketRecorder recorder = new PacketRecorder();

            UOC.OverheadMessage( "Hail", 0x22, mobile.Serial );

            byte[] text = BigUniNull( "Hail" );
            byte[] expected = Concat( new byte[] { 0xAE }, U16( 48 + text.Length ), U32( 0x5678 ), U16( 0x0190 ),
                new byte[] { (byte) JournalSpeech.Say }, U16( 0x22 ), U16( 3 ), Ascii( Strings.UO_LOCALE, 4 ),
                Ascii( "Bob", 30 ), text );

            Assert.Equal( expected, Assert.Single( recorder.ReceivedWithId( 0xAE ) ) );
        }

        [Fact]
        public void WontInjectOverheadMessageForUnknownEntity()
        {
            using PacketRecorder recorder = new PacketRecorder();

            UOC.OverheadMessage( "Hail", 0x22, 0x0BADBEEF );

            Assert.Empty( recorder.Received );
        }

        [Fact]
        public void WillResendTargetCursorToClient()
        {
            Engine.TargetType = TargetType.Tile;
            Engine.TargetSerial = 0x0A0B0C0D;
            Engine.TargetFlags = TargetFlags.Harmful;
            Engine.InternalTarget = true;

            using PacketRecorder recorder = new PacketRecorder();

            UOC.ResendTargetToClient();

            byte[] expected = Concat( new byte[] { 0x6C, 0x01 }, U32( 0x0A0B0C0D ), new byte[] { 0x01 },
                new byte[12] );

            Assert.Equal( expected, Assert.Single( recorder.ReceivedWithId( 0x6C ) ) );
            Assert.True( Engine.TargetExists );
            Assert.False( Engine.InternalTarget );
        }

        [Fact]
        public void WillSendVendorBuyList()
        {
            ShopListEntry[] entries =
            {
                new ShopListEntry { Item = new Item( 0x40000010 ), Amount = 5 },
                new ShopListEntry { Item = new Item( 0x40000011 ), Amount = 300 }
            };

            using PacketRecorder recorder = new PacketRecorder();

            UOC.VendorBuy( 0x00000F00, entries );

            byte[] expected = Concat( new byte[] { 0x3B }, U16( 8 + 2 * 7 ), U32( 0x0F00 ), // vendor
                new byte[] { 0x02 }, // flag: item list follows
                new byte[] { 0x1A }, U32( 0x40000010 ), U16( 5 ), // layer ShopBuy, serial, amount
                new byte[] { 0x1A }, U32( 0x40000011 ), U16( 300 ) );

            Assert.Equal( expected, Assert.Single( recorder.SentWithId( 0x3B ) ) );
        }

        [Theory]
        [InlineData( true, 1, 2 )]
        [InlineData( false, 1, 1 )]
        [InlineData( false, 0, 0 )]
        public void WillInjectForceWalkWithPlayerSpeedUnlessForced( bool force, byte playerSpeed, byte expected )
        {
            ( (PlayerMobile) Engine.Player ).MovementSpeed = playerSpeed;

            using PacketRecorder recorder = new PacketRecorder();

            UOC.SetForceWalk( force );

            Assert.Equal( new byte[] { 0xBF, 0x00, 0x06, 0x00, 0x26, expected },
                Assert.Single( recorder.ReceivedWithId( 0xBF ) ) );
        }

        [Fact]
        public void WillSendUO3DEquipAndUnequipPackets()
        {
            using PacketRecorder recorder = new PacketRecorder();

            UOC.UO3DEquipItems( new[] { 0x40000001, 0x40000002 } );
            UOC.UO3DUnequipItems( new[] { (int) Layer.Helm, (int) Layer.Gloves } );

            Assert.Equal( Concat( new byte[] { 0xEC }, U16( 12 ), new byte[] { 2 }, U32( 0x40000001 ),
                U32( 0x40000002 ) ), Assert.Single( recorder.SentWithId( 0xEC ) ) );
            Assert.Equal( Concat( new byte[] { 0xED }, U16( 8 ), new byte[] { 2 }, U16( (int) Layer.Helm ),
                U16( (int) Layer.Gloves ) ), Assert.Single( recorder.SentWithId( 0xED ) ) );
        }

        [Fact]
        public void WontSendEmptyUO3DEquipPackets()
        {
            using PacketRecorder recorder = new PacketRecorder();

            UOC.UO3DEquipItems( Array.Empty<int>() );
            UOC.UO3DEquipItems( null );
            UOC.UO3DUnequipItems( Array.Empty<int>() );
            UOC.UO3DUnequipItems( null );

            Assert.Empty( recorder.Sent );
        }

        [Fact]
        public void WillSendChatMessageAndJoinChannel()
        {
            using PacketRecorder recorder = new PacketRecorder();

            UOC.ChatMsg( "hi" );
            UOC.JoinChatChannel( "Help" );

            IReadOnlyList<byte[]> sent = recorder.SentWithId( 0xB3 );
            Assert.Equal( 2, sent.Count );

            Assert.Equal( Concat( new byte[] { 0xB3 }, U16( 11 + 2 * 2 ), Ascii( Strings.UO_LOCALE, 4 ), U16( 0x61 ),
                BigUniNull( "hi" ) ), sent[0] );

            Assert.Equal( Concat( new byte[] { 0xB3 }, U16( 17 + 4 * 2 ), Ascii( Strings.UO_LOCALE, 4 ), U16( 0x62 ),
                BigUniNull( "Help" ), U16( 0x22 ), U16( 0x20 ), U16( 0 ) ), sent[1] );
        }

        [Fact]
        public void WillInjectRemoveObject()
        {
            using PacketRecorder recorder = new PacketRecorder();

            UOC.RemoveObject( 0x40000123 );

            Assert.Equal( Concat( new byte[] { 0x1D }, U32( 0x40000123 ) ),
                Assert.Single( recorder.ReceivedWithId( 0x1D ) ) );
        }

        [Theory]
        [InlineData( "6.0.1.7", true )]
        [InlineData( "7.0.45.1", true )]
        [InlineData( "6.0.1.6", false )]
        [InlineData( "5.0.0.0", false )]
        public void WillWriteDropItemGridByteFromClientVersion6017( string version, bool hasGrid )
        {
            Engine.ClientVersion = Version.Parse( version );

            using PacketRecorder recorder = new PacketRecorder();

            UOC.DropItem( 0x40000001, 0x40000002, 10, 20, -3 );

            byte[] head = Concat( new byte[] { 0x08 }, U32( 0x40000001 ), U16( 10 ), U16( 20 ), new byte[] { 0xFD } );
            byte[] expected = hasGrid
                ? Concat( head, new byte[] { 0x00 }, U32( 0x40000002 ) )
                : Concat( head, U32( 0x40000002 ) );

            Assert.Equal( expected, Assert.Single( recorder.SentWithId( 0x08 ) ) );
        }

        [Fact]
        public void WillDropToGroundWithMinusOneCoordinatesByDefault()
        {
            using PacketRecorder recorder = new PacketRecorder();

            UOC.DropItem( 0x40000001, -1 );

            Assert.Equal( Concat( new byte[] { 0x08 }, U32( 0x40000001 ), U16( 0xFFFF ), U16( 0xFFFF ),
                new byte[] { 0x00, 0x00 }, U32( -1 ) ), Assert.Single( recorder.SentWithId( 0x08 ) ) );
        }

        [Fact]
        public async Task WillDragThenDropUsingItemCountWhenAmountIsMinusOne()
        {
            Item item = new Item( 0x40000050 ) { ID = 0x0EED, Count = 1234 };
            Engine.Items.Add( item );

            using PacketRecorder recorder = new PacketRecorder();

            await UOC.DragDropAsync( item.Serial, -1, Engine.Player.Backpack.Serial );

            IReadOnlyList<byte[]> sent = recorder.Sent;
            Assert.Equal( 2, sent.Count );
            Assert.Equal( Concat( new byte[] { 0x07 }, U32( item.Serial ), U16( 1234 ) ), sent[0] );
            Assert.Equal( 0x08, sent[1][0] );
            Assert.Equal( Engine.Player.Backpack.Serial, BinaryPrimitives.ReadInt32BigEndian( sent[1].AsSpan( 11 ) ) );
        }

        [Fact]
        public async Task WillDragOneOfAnUnknownItemWhenAmountIsMinusOne()
        {
            using PacketRecorder recorder = new PacketRecorder();

            await UOC.DragDropAsync( 0x40000099, -1, 0x40000098 );

            Assert.Equal( Concat( new byte[] { 0x07 }, U32( 0x40000099 ), U16( 1 ) ), recorder.Sent[0] );
        }

        [Fact]
        public void WillSendMobileQueryAndSkillLockWithRequery()
        {
            SkillEntry skill = new SkillEntry { Skill = new Skill { ID = 25, Name = "Magery" } };

            using PacketRecorder recorder = new PacketRecorder();

            UOC.ChangeSkillLock( skill, LockStatus.Locked );
            UOC.ChangeSkillLock( skill, LockStatus.Down, false );

            IReadOnlyList<byte[]> sent = recorder.Sent;
            Assert.Equal( 3, sent.Count );
            Assert.Equal( new byte[] { 0x3A, 0x00, 0x06, 0x00, 25, (byte) LockStatus.Locked }, sent[0] );
            Assert.Equal( Concat( new byte[] { 0x34 }, U32( unchecked( (int) 0xEDEDEDED ) ),
                new byte[] { (byte) MobileQueryType.SkillsRequest }, U32( PlayerSerial ) ), sent[1] );
            Assert.Equal( new byte[] { 0x3A, 0x00, 0x06, 0x00, 25, (byte) LockStatus.Down }, sent[2] );
        }

        [Fact]
        public void WillSendStatLockAndStatsQuery()
        {
            using PacketRecorder recorder = new PacketRecorder();

            UOC.ChangeStatLock( StatType.Dex, LockStatus.Up );
            UOC.MobileQuery( 0x00000ABC );

            IReadOnlyList<byte[]> sent = recorder.Sent;
            Assert.Equal( new byte[] { 0xBF, 0x00, 0x07, 0x00, 0x1A, (byte) StatType.Dex, (byte) LockStatus.Up },
                sent[0] );
            Assert.Equal( Concat( new byte[] { 0x34 }, U32( unchecked( (int) 0xEDEDEDED ) ),
                new byte[] { (byte) MobileQueryType.StatsRequest }, U32( 0x0ABC ) ), sent[1] );
        }

        [Fact]
        public void WillSendSimpleRequests()
        {
            using PacketRecorder recorder = new PacketRecorder();

            UOC.Resync();
            UOC.ToggleGargoyleFlying();
            UOC.ClearWeaponAbility();
            UOC.RenameRequest( 0x00000ABC, "A name that is far longer than thirty characters" );

            IReadOnlyList<byte[]> sent = recorder.Sent;
            Assert.Equal( 4, sent.Count );
            Assert.Equal( new byte[] { 0x22, 0x00, 0x00 }, sent[0] );
            Assert.Equal( new byte[] { 0xBF, 0x00, 0x0B, 0x00, 0x32, 0x00, 0x01, 0, 0, 0, 0 }, sent[1] );
            Assert.Equal( Concat( new byte[] { 0xD7 }, U16( 15 ), U32( PlayerSerial ), U16( 0x19 ), new byte[] { 0 },
                U32( 0 ), new byte[] { 0x0A } ), sent[2] );
            Assert.Equal( Concat( new byte[] { 0x75 }, U32( 0x0ABC ),
                Ascii( "A name that is far longer than thirty characters", 30 ) ), sent[3] );
        }

        [Fact]
        public void WillReturnFalseFromGumpButtonClickForUnknownGump()
        {
            Engine.GumpList.Clear();

            using PacketRecorder recorder = new PacketRecorder();

            Assert.False( UOC.GumpButtonClick( 0x12345678, 1 ) );
            Assert.Empty( recorder.Sent );
        }

        [Fact]
        public async Task WillCancelInternalTargetCursorOnTimeout()
        {
            using PacketRecorder recorder = new PacketRecorder();

            (TargetType type, TargetFlags _, int serial, int x, int y, int _, int _) =
                await UOC.GetTargetInfoAsync( "Pick", 50, true );

            Assert.Equal( TargetType.Object, type );
            Assert.Equal( -1, serial );
            Assert.Equal( -1, x );
            Assert.Equal( -1, y );

            IReadOnlyList<byte[]> cursors = recorder.ReceivedWithId( 0x6C );
            Assert.Equal( 2, cursors.Count );

            // Request: object target, a cursor id of its own, no flags, zero padded to 19 bytes.
            byte[] request = cursors[0];
            int cursorId = BinaryPrimitives.ReadInt32BigEndian( request.AsSpan( 2 ) );
            Assert.Equal( 19, request.Length );
            Assert.Equal( 0x00, request[1] );
            Assert.NotEqual( 0, cursorId );
            Assert.All( request.Skip( 6 ), b => Assert.Equal( 0, b ) );

            // Cancel: the same cursor id with the cancel flag.
            Assert.Equal( Concat( new byte[] { 0x6C, 0x00 }, U32( cursorId ), new byte[] { 0x03 }, new byte[12] ),
                cursors[1] );

            Assert.False( Engine.TargetExists );
            Assert.False( Engine.InternalTarget );
            Assert.Contains( Engine.Journal.GetEntireBuffer(), j => j.Text == "Pick" );
            Assert.Contains( Engine.Journal.GetEntireBuffer(), j => j.Text == Strings.Timeout___ );
        }

        [Fact]
        public async Task WillRestoreClientsOwnCursorAfterInternalTarget()
        {
            Engine.TargetExists = true;
            Engine.TargetSerial = 0x00C0FFEE;

            using PacketRecorder recorder = new PacketRecorder();

            await UOC.GetTargetInfoAsync( "Pick", 20 );

            // Request (tile target when not object-only), cancel, then the client's cursor back.
            IReadOnlyList<byte[]> cursors = recorder.ReceivedWithId( 0x6C );
            Assert.Equal( 3, cursors.Count );
            Assert.Equal( 0x01, cursors[0][1] );
            Assert.Equal( 0x00C0FFEE, BinaryPrimitives.ReadInt32BigEndian( cursors[2].AsSpan( 2 ) ) );
            Assert.True( Engine.TargetExists );
        }

        [Theory]
        [InlineData( "7.0.45.1", 19 )]
        [InlineData( "6.0.1.7", 19 )]
        [InlineData( "6.0.1.6", 18 )]
        public async Task WillWaitForContainerContentsAtVersionDependentOffset( string version, int offset )
        {
            Engine.ClientVersion = Version.Parse( version );
            const int container = 0x40000AAA;

            PacketWaitEntries entries = Engine.PacketWaitEntries;
            using ManualResetEventSlim registered = new ManualResetEventSlim();

            void OnAdded( PacketWaitEntry entry )
            {
                registered.Set();
            }

            entries.WaitEntryAddedEvent += OnAdded;

            try
            {
                using PacketRecorder recorder = new PacketRecorder();

                Task<bool> wait = Task.Run( () => UOC.WaitForContainerContentsUse( container, 5000 ),
                    TestContext.Current.CancellationToken );

                Assert.True( registered.Wait( 5000, TestContext.Current.CancellationToken ) );

                // A 0x3C whose first item lives in the container: the container serial sits at the
                // offset under test.
                byte[] wrong = new byte[24];
                wrong[0] = 0x3C;
                BinaryPrimitives.WriteInt32BigEndian( wrong.AsSpan( offset == 19 ? 18 : 19 ), container );
                entries.CheckWait( wrong, PacketDirection.Incoming );

                Assert.NotSame( wait,
                    await Task.WhenAny( wait, Task.Delay( 100, TestContext.Current.CancellationToken ) ) );

                byte[] contents = new byte[24];
                contents[0] = 0x3C;
                BinaryPrimitives.WriteInt32BigEndian( contents.AsSpan( offset ), container );
                entries.CheckWait( contents, PacketDirection.Incoming );

                Assert.Same( wait,
                    await Task.WhenAny( wait, Task.Delay( 5000, TestContext.Current.CancellationToken ) ) );
                Assert.True( await wait );

                // The container was opened with a double click.
                Assert.Equal( Concat( new byte[] { 0x06 }, U32( container ) ),
                    Assert.Single( recorder.SentWithId( 0x06 ) ) );
            }
            finally
            {
                entries.WaitEntryAddedEvent -= OnAdded;
            }
        }

        [Fact]
        public async Task WillIgnoreCancelCursorWhileWaitingForTarget()
        {
            PacketWaitEntries entries = Engine.PacketWaitEntries;
            using CountdownEvent registered = new CountdownEvent( 2 );

            void OnAdded( PacketWaitEntry entry )
            {
                registered.Signal();
            }

            entries.WaitEntryAddedEvent += OnAdded;

            try
            {
                Task<bool> wait = Task.Run( () => UOC.WaitForTarget( 5000 ), TestContext.Current.CancellationToken );

                Assert.True( registered.Wait( 5000, TestContext.Current.CancellationToken ) );
                Assert.True( Engine.WaitingForTarget );

                byte[] cancel = new byte[19];
                cancel[0] = 0x6C;
                cancel[6] = 0x03;
                entries.CheckWait( cancel, PacketDirection.Incoming );

                Assert.NotSame( wait,
                    await Task.WhenAny( wait, Task.Delay( 100, TestContext.Current.CancellationToken ) ) );

                // A multi placement cursor (0x99) counts as a target.
                byte[] multi = new byte[30];
                multi[0] = 0x99;
                entries.CheckWait( multi, PacketDirection.Incoming );

                Assert.Same( wait,
                    await Task.WhenAny( wait, Task.Delay( 5000, TestContext.Current.CancellationToken ) ) );
                Assert.True( await wait );
                Assert.False( Engine.WaitingForTarget );
            }
            finally
            {
                entries.WaitEntryAddedEvent -= OnAdded;
            }
        }

        [Fact]
        public void WillTimeOutWaitForTarget()
        {
            Assert.False( UOC.WaitForTarget( 20 ) );
            Assert.False( Engine.WaitingForTarget );
        }
    }
}
