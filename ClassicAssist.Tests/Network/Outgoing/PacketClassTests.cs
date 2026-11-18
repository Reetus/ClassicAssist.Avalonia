using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ClassicAssist.Data;
using ClassicAssist.Data.Friends;
using ClassicAssist.Data.Spells;
using ClassicAssist.Data.Vendors;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests.Network.Outgoing
{
    /// <summary>
    ///     Pins the bytes of the packet classes in <c>UO/Network/Packets</c> that no other test reaches,
    ///     field by field against the UO protocol, including the client-version dependent variants
    ///     (legacy vs. modern cast, quest arrow with and without serial), and the macro recorder's
    ///     <see cref="IMacroCommandParser.Parse" /> for the ones that round trip into a macro line. A
    ///     wrong byte here is invisible until a shard ignores the action or the client draws garbage.
    /// </summary>
    public class PacketClassTests : IDisposable
    {
        private const int PlayerSerial = 0x00001234;

        private readonly Options _originalOptions;

        public PacketClassTests()
        {
            EngineState.ResetWithPlayer( PlayerSerial );
            _originalOptions = Options.CurrentOptions;
            Options.CurrentOptions = new Options();
        }

        public void Dispose()
        {
            Options.CurrentOptions = _originalOptions;
            EngineState.Reset();
        }

        private static byte[] Concat( params byte[][] parts )
        {
            return parts.SelectMany( p => p ).ToArray();
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
        public void WillBuildModernCastSpellFromClient60142()
        {
            Engine.ClientVersion = new Version( 6, 0, 14, 2 );

            byte[] packet = new CastSpell( 0x0123 ).ToArray();

            Assert.Equal( new byte[] { 0xBF, 0x00, 0x09, 0x00, 0x1C, 0x00, 0x02, 0x01, 0x23 }, packet );
        }

        [Fact]
        public void WillBuildLegacyCastSpellBeforeClient60142()
        {
            Engine.ClientVersion = new Version( 6, 0, 14, 1 );

            byte[] packet = new CastSpell( 42 ).ToArray();

            // 0x12 action request, type 0x56 (cast), ASCII spell id, null terminator.
            Assert.Equal( new byte[] { 0x12, 0x00, 0x07, 0x56, (byte) '4', (byte) '2', 0x00 }, packet );
        }

        [Theory]
        [InlineData( "7.0.45.1" )]
        [InlineData( "5.0.0.0" )]
        public void WillParseOwnCastSpellBackToMacro( string version )
        {
            Engine.ClientVersion = Version.Parse( version );
            SpellData spell = SpellManager.GetInstance().GetSpellData( 1 );

            byte[] packet = new CastSpell( 1 ).ToArray();

            Assert.Equal( $"Cast(\"{spell.Name}\")\r\n",
                new CastSpell().Parse( packet, packet.Length, PacketDirection.Outgoing ) );
            Assert.Null( new CastSpell().Parse( packet, packet.Length, PacketDirection.Incoming ) );
        }

        [Fact]
        public void WillBuildSAWorldItemFromItem()
        {
            Item item = new Item( 0x40000123 )
            {
                ID = 0x0EED,
                ArtDataID = 0,
                Direction = Direction.East,
                Count = 500,
                X = 1000,
                Y = 2000,
                Z = -5,
                Light = 3,
                Hue = 0x0035,
                Flags = 0x20
            };

            byte[] packet = new SAWorldItem( item ).ToArray();

            Assert.Equal( Concat( new byte[] { 0xF3 }, U16( 0x01 ), new byte[] { 0x00 }, U32( 0x40000123 ),
                U16( 0x0EED ), new byte[] { (byte) Direction.East }, U16( 500 ), U16( 500 ), U16( 1000 ), U16( 2000 ),
                new byte[] { 0xFB, 3 }, U16( 0x0035 ), new byte[] { 0x20 }, new byte[2] ), packet );

            // A hue override replaces only the hue.
            byte[] rehued = new SAWorldItem( item, 0x0480 ).ToArray();
            Assert.Equal( U16( 0x0480 ), rehued.Skip( 21 ).Take( 2 ).ToArray() );
            Assert.Equal( packet.Take( 21 ), rehued.Take( 21 ) );
        }

        [Fact]
        public void WillRehueCapturedSAWorldItemInPlace()
        {
            byte[] original = new SAWorldItem( new Item( 0x40000123 ) { ID = 0x0EED, Hue = 0x11 } ).ToArray();

            byte[] rehued = new SAWorldItem( original, original.Length, 0x0480 ).ToArray();
            byte[] untouched = new SAWorldItem( original, original.Length, 0 ).ToArray();

            Assert.Equal( 26, rehued.Length );
            Assert.Equal( U16( 0x0480 ), rehued.Skip( 21 ).Take( 2 ).ToArray() );
            Assert.Equal( original.Where( ( b, i ) => i != 21 && i != 22 ),
                rehued.Where( ( b, i ) => i != 21 && i != 22 ) );
            Assert.Equal( original, untouched );
        }

        [Fact]
        public void WillBuildObjectTargetResponse()
        {
            Mobile mobile = new Mobile( 0x00000ABC ) { ID = 0x0190 };

            byte[] packet = new Target( 0x01020304, mobile, false ).ToArray();

            Assert.Equal( Concat( new byte[] { 0x6C, 0x00 }, U32( 0x01020304 ), new byte[] { 0x00 }, U32( 0x0ABC ),
                U16( 0xFFFF ), U16( 0xFFFF ), U16( 0xFFFF ), U16( 0x0190 ) ), packet );
            Assert.Equal( 0x0ABC, Engine.Player.LastTargetSerial );
            Assert.False( Engine.TargetExists );
        }

        [Fact]
        public void WillUseClientCursorSerialWhenSenderIsMinusOne()
        {
            Engine.TargetSerial = 0x0055AA55;

            byte[] packet = new Target( TargetType.Tile, -1, TargetFlags.Beneficial, 0, 100, 200, -10, 0x1234, false )
                .ToArray();

            Assert.Equal( Concat( new byte[] { 0x6C, 0x01 }, U32( 0x0055AA55 ), new byte[] { 0x02 }, U32( 0 ),
                U16( 100 ), U16( 200 ), U16( -10 ), U16( 0x1234 ) ), packet );
        }

        [Fact]
        public void WillBlockHarmfulTargetOnFriend()
        {
            Options.CurrentOptions.PreventTargetingFriendsWithHarmful = true;
            Options.CurrentOptions.Friends.Add( new FriendEntry { Name = "pal", Serial = 0x00000ABC } );
            Engine.TargetFlags = TargetFlags.Harmful;
            Engine.TargetSerial = 0x77;

            using PacketRecorder recorder = new PacketRecorder();

            Target target = new Target( TargetType.Object, 0x77, TargetFlags.Harmful, 0x00000ABC, 0, 0, 0, 0x190,
                false );

            Assert.Null( target.ToArray() );
            Assert.Contains( Engine.Journal.GetEntireBuffer(), j => j.Text == Strings.Target_blocked____try_again___ );

            // The client gets its cursor back so the player can pick again.
            Assert.Single( recorder.ReceivedWithId( 0x6C ) );
        }

        [Theory]
        [InlineData( new byte[] { 0x6C, 0x00, 0, 0, 0, 1, 0, 0x01, 0x02, 0x03, 0x04, 0, 0, 0, 0, 0, 0, 0, 0 },
            "Target(0x1020304)\r\n" )]
        [InlineData( new byte[] { 0x6C, 0x01, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0x01, 0x00, 0x02, 0x00, 0xFF, 0xF6, 0, 0 },
            "TargetXYZ(256, 512, -10)\r\n" )]
        [InlineData( new byte[] { 0x6C, 0x01, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0x00, 0x05, 0x00, 0x06, 0x00, 0x07, 0x0F, 0xED },
            "TargetXYZ(5, 6, 7, 4077)\r\n" )]
        public void WillParseTargetResponseIntoMacro( byte[] packet, string expected )
        {
            Assert.Equal( expected, new Target().Parse( packet, packet.Length, PacketDirection.Outgoing ) );
            Assert.Equal( "WaitForTarget(5000)\r\n", new Target().Parse( packet, packet.Length, PacketDirection.Incoming ) );
        }

        [Theory]
        [InlineData( "7.0.45.1" )]
        [InlineData( "6.0.1.6" )]
        public void WillParseDropItemIntoMoveItemForEitherVersion( string version )
        {
            Engine.ClientVersion = Version.Parse( version );

            byte[] toContainer = new DropItem( 0x40000001, 0x40000002, 10, 20, 0 ).ToArray();
            byte[] toGround = new DropItem( 0x40000001, -1, 1000, 2000, 5 ).ToArray();

            Assert.Equal( "MoveItem(0x40000001, 0x40000002)\r\n",
                new DropItem().Parse( toContainer, toContainer.Length, PacketDirection.Outgoing ) );
            Assert.Equal( "MoveItemXYZ(0x40000001, 1000, 2000, 5)\r\n",
                new DropItem().Parse( toGround, toGround.Length, PacketDirection.Outgoing ) );
            Assert.Null( new DropItem().Parse( toGround, toGround.Length, PacketDirection.Incoming ) );
        }

        [Fact]
        public void WillResolveDragAmountFromItemOnlyWhenAsked()
        {
            Engine.Items.Add( new Item( 0x40000010 ) { Count = 77 } );

            Assert.Equal( Concat( new byte[] { 0x07 }, U32( 0x40000010 ), U16( 77 ) ),
                new DragItem( 0x40000010, -1, true ).ToArray() );
            Assert.Equal( Concat( new byte[] { 0x07 }, U32( 0x40000010 ), U16( 0xFFFF ) ),
                new DragItem( 0x40000010, -1 ).ToArray() );
        }

        [Fact]
        public void WillBuildCancelTargetCursorFromEitherSerialType()
        {
            byte[] expected = Concat( new byte[] { 0x6C, 0x00 }, U32( unchecked( (int) 0xDEADBEEF ) ),
                new byte[] { 0x03 }, new byte[12] );

            Assert.Equal( expected, new CancelTargetCursor( 0xDEADBEEFu ).ToArray() );
            Assert.Equal( expected, new CancelTargetCursor( unchecked( (int) 0xDEADBEEF ) ).ToArray() );
        }

        [Fact]
        public void WillBuildUseSkillAndRecordLastSkill()
        {
            byte[] packet = new UseSkill( 21 ).ToArray();

            // 0x12 action request, type 0x24 (skill), "21 0" and a null terminator.
            Assert.Equal( Concat( new byte[] { 0x12, 0x00, 0x09, 0x24 }, Encoding.ASCII.GetBytes( "21 0" ),
                new byte[] { 0 } ), packet );
            Assert.Equal( 21, Engine.LastSkillID );

            byte[] last = new UseSkill( 0 ).ToArray();
            Assert.Equal( "UseLastSkill()\r\n", new UseSkill().Parse( last, last.Length, PacketDirection.Outgoing ) );
        }

        [Fact]
        public void WillBuildUnicodeText()
        {
            byte[] packet = new UnicodeText( 0x00000ABC, 0x0190, JournalSpeech.Emote, 0x22, 3, "ENU", "Bob", "hi" )
                .ToArray();

            byte[] name = new byte[30];
            Encoding.ASCII.GetBytes( "Bob" ).CopyTo( name, 0 );

            Assert.Equal( Concat( new byte[] { 0xAE }, U16( 52 ), U32( 0x0ABC ), U16( 0x0190 ),
                new byte[] { (byte) JournalSpeech.Emote }, U16( 0x22 ), U16( 3 ), Encoding.ASCII.GetBytes( "ENU\0" ),
                name, Encoding.BigEndianUnicode.GetBytes( "hi" ) ), packet );
        }

        [Fact]
        public void WillBuildVendorBuyFromEntriesWithVendorOfFirstEntry()
        {
            ShopListEntry[] entries =
            {
                new ShopListEntry { VendorSerial = 0x0F00, Item = new Item( 0x40000010 ), Amount = 2 },
                new ShopListEntry { VendorSerial = 0x0F00, Item = new Item( 0x40000011 ), Amount = 3 }
            };

            Assert.Equal( Concat( new byte[] { 0x3B }, U16( 22 ), U32( 0x0F00 ), new byte[] { 0x02, 0x1A },
                U32( 0x40000010 ), U16( 2 ), new byte[] { 0x1A }, U32( 0x40000011 ), U16( 3 ) ),
                new VendorBuy( entries ).ToArray() );

            Assert.Equal( Concat( new byte[] { 0x3B }, U16( 8 ), U32( 0 ), new byte[] { 0x02 } ),
                new VendorBuy( Array.Empty<ShopListEntry>() ).ToArray() );
        }

        [Fact]
        public void WillBuildMobileUpdateAndMobileMoving()
        {
            Assert.Equal( Concat( new byte[] { 0x20 }, U32( 0x0ABC ), U16( 0x0190 ), new byte[] { 0 }, U16( 0x0021 ),
                    new byte[] { (byte) MobileStatus.Female }, U16( 100 ), U16( 200 ), U16( 0 ),
                    new byte[] { (byte) Direction.South, 0xFE } ),
                new MobileUpdate( 0x0ABC, 0x0190, 0x0021, MobileStatus.Female, 100, 200, -2, Direction.South )
                    .ToArray() );

            Mobile mobile = new Mobile( 0x0ABC )
            {
                ID = 0x0191,
                X = 300,
                Y = 400,
                Z = 7,
                Direction = Direction.West,
                Hue = 0x0022,
                Status = MobileStatus.Flying,
                Notoriety = Notoriety.Murderer
            };

            byte[] moving = new MobileMoving( mobile ).ToArray();
            Assert.Equal( Concat( new byte[] { 0x77 }, U32( 0x0ABC ), U16( 0x0191 ), U16( 300 ), U16( 400 ),
                new byte[] { 7, (byte) Direction.West }, U16( 0x0022 ),
                new byte[] { (byte) MobileStatus.Flying, (byte) Notoriety.Murderer } ), moving );

            Assert.Equal( U16( 0x0480 ), new MobileMoving( mobile, 0x0480 ).ToArray().Skip( 13 ).Take( 2 ).ToArray() );
        }

        [Fact]
        public void WillBuildAndParseContextMenuClick()
        {
            byte[] packet = new ContextMenuClick( 0x40000ABC, 3 ).ToArray();

            Assert.Equal( Concat( new byte[] { 0xBF }, U16( 11 ), U16( 0x15 ), U32( 0x40000ABC ), U16( 3 ) ), packet );
            Assert.Equal( "WaitForContext(0x40000abc, 3, 5000)\r\n",
                new ContextMenuClick().Parse( packet, packet.Length, PacketDirection.Outgoing ) );
            Assert.Equal( Concat( new byte[] { 0xBF }, U16( 9 ), U16( 0x13 ), U32( 0x40000ABC ) ),
                new ContextMenuRequest( 0x40000ABC ).ToArray() );
        }

        [Fact]
        public void WillBuildUnicodePromptRequest()
        {
            byte[] packet = new UnicodePromptRequest( 0x0ABC ).ToArray();

            Assert.Equal( Concat( new byte[] { 0xC2 }, U16( 0x15 ), U32( 0x0ABC ), U32( 0x0ABC ), U32( 1 ),
                Encoding.ASCII.GetBytes( Strings.UO_LOCALE + "\0" ), new byte[2] ), packet );
            Assert.Equal( "WaitForPrompt(5000)\r\n",
                new UnicodePromptRequest().Parse( packet, packet.Length, PacketDirection.Incoming ) );
        }

        [Theory]
        [InlineData( "7.0.9.0", true )]
        [InlineData( "7.0.8.99", false )]
        public void WillAddSerialToQuestArrowFromClient7090( string version, bool hasSerial )
        {
            Engine.ClientVersion = Version.Parse( version );

            byte[] packet = new DisplayQuestPointer( true, 1000, 2000, 0x0ABC ).ToArray();

            byte[] head = Concat( new byte[] { 0xBA, 0x01 }, U16( 1000 ), U16( 2000 ) );
            Assert.Equal( hasSerial ? Concat( head, U32( 0x0ABC ) ) : head, packet );
        }

        [Fact]
        public void WillBuildPlayerScopedPackets()
        {
            Engine.Player.X = 10;
            Engine.Player.Y = 20;
            Engine.Player.Z = -1;

            Assert.Equal( Concat( new byte[] { 0xBF }, U16( 12 ), U16( 0x33 ), U32( PlayerSerial ),
                new byte[] { (byte) Direction.East, (byte) Direction.East, (byte) BoatSpeed.Normal } ),
                new WheelBoatMoving( Direction.East, BoatSpeed.Normal ).ToArray() );

            Assert.Equal( Concat( new byte[] { 0x54, 0x01 }, U16( 0x0123 ), U16( 0 ), U16( 10 ), U16( 20 ), U16( -1 ) ),
                new PlaySound( 0x0123 ).ToArray() );

            Assert.Equal( Concat( new byte[] { 0xD7 }, U16( 10 ), U32( PlayerSerial ), U16( 0x1E ), new byte[] { 0x0A } ),
                new EquipLastWeapon().ToArray() );
        }

        [Fact]
        public void WillParseEquipLastWeaponOnlyForOutgoingPacket()
        {
            byte[] packet = new EquipLastWeapon().ToArray();

            Assert.Equal( "EquipLastWeapon()\r\n",
                new EquipLastWeapon().Parse( packet, packet.Length, PacketDirection.Outgoing ) );
            Assert.Null( new EquipLastWeapon().Parse( packet, packet.Length, PacketDirection.Incoming ) );
        }

        [Fact]
        public void WillBuildPlaySoundAtOriginWithoutPlayer()
        {
            Engine.Player = null;

            Assert.Equal( Concat( new byte[] { 0x54, 0x01 }, U16( 0x0123 ), new byte[8] ),
                new PlaySound( 0x0123 ).ToArray() );
            Assert.Null( new EquipLastWeapon().ToArray() );
        }

        [Fact]
        public void WillBuildBatchPropertyQueries()
        {
            Assert.Equal( Concat( new byte[] { 0xD6 }, U16( 7 ), U32( 0x40000001 ) ),
                new BatchQueryProperties( 0x40000001 ).ToArray() );
            Assert.Equal( Concat( new byte[] { 0xD6 }, U16( 11 ), U32( 0x40000001 ), U32( 0x40000002 ) ),
                new BatchQueryProperties( new List<int> { 0x40000001, 0x40000002 } ).ToArray() );
            Assert.Equal( Concat( new byte[] { 0xBF }, U16( 9 ), U16( 0x10 ), U32( 0x40000001 ) ),
                new QueryProperties( 0x40000001 ).ToArray() );
        }

        [Fact]
        public void WillBuildGeneralInfoSubcommands()
        {
            Assert.Equal( Concat( new byte[] { 0xBF }, U16( 13 ), U16( 0x2C ), U32( 0x40000001 ), U32( 0x00000ABC ) ),
                new UseTargetedItem( 0x40000001, 0x0ABC ).ToArray() );
            Assert.Equal( Concat( new byte[] { 0xBF }, U16( 11 ), U16( 0x30 ), U32( 0x40000001 ), U16( 1 ) ),
                new TargetByResource( 0x40000001, 1 ).ToArray() );
            Assert.Equal( Concat( new byte[] { 0xBF }, U16( 10 ), U16( 0x06 ), new byte[] { 0x08 }, U32( 0x0ABC ) ),
                new AcceptPartyInvitation( 0x0ABC ).ToArray() );
            Assert.Equal( Concat( new byte[] { 0xBF }, U16( 10 ), U16( 0x06 ), new byte[] { 0x09 }, U32( 0x0ABC ) ),
                new RejectPartyInvitation( 0x0ABC ).ToArray() );
        }

        [Fact]
        public void WillBuildFixedPackets()
        {
            Assert.Equal( Concat( new byte[] { 0x95 }, U32( 0x40000001 ), U16( 0x0FAB ), U16( 0x0480 ) ),
                new HuePickerResponse( 0x40000001, 0x0FAB, 0x0480 ).ToArray() );
            Assert.Equal( new byte[] { 0x73, 0x2A }, new Ping( 0x2A ).ToArray() );
            Assert.Equal( Concat( new byte[] { 0x1D }, U32( 0x40000001 ) ), new RemoveObject( 0x40000001 ).ToArray() );
        }

        [Fact]
        public void WillParseGumpPacketsIntoMacroLines()
        {
            byte[] generic = Gumps.GumpPackets.BuildGeneric( 1, 0xABCD, 0, 0, "{ text 1 1 0 0 }", "x" );
            byte[] compressed = Gumps.GumpPackets.BuildCompressed( 1, 0xABCD, 0, 0, "{ text 1 1 0 0 }", "x" );

            Assert.Equal( "WaitForGump(0xabcd, 5000)\r\n",
                new GenericGump().Parse( generic, generic.Length, PacketDirection.Incoming ) );
            Assert.Equal( "WaitForGump(0xabcd, 5000)\r\n",
                new CompressedGump().Parse( compressed, compressed.Length, PacketDirection.Incoming ) );
            Assert.Null( new GenericGump().Parse( compressed, compressed.Length, PacketDirection.Incoming ) );
            Assert.Null( new CompressedGump().Parse( compressed, compressed.Length, PacketDirection.Outgoing ) );
        }

        [Fact]
        public void WillParseGumpReplyIntoReplyGump()
        {
            byte[] plain = new GumpButtonClick( 0x1234, 1, 7 ).ToArray();
            byte[] withSwitches = new GumpButtonClick( 0x1234, 1, 7, new[] { 3, 4 } ).ToArray();

            Assert.Equal( "ReplyGump(0x1234, 7)\r\n",
                new GumpButtonClick().Parse( plain, plain.Length, PacketDirection.Outgoing ) );
            Assert.Equal( "ReplyGump(0x1234, 7, Array[int]([3,4]))\r\n",
                new GumpButtonClick().Parse( withSwitches, withSwitches.Length, PacketDirection.Outgoing ) );
            Assert.Null( new GumpButtonClick().Parse( plain, plain.Length, PacketDirection.Incoming ) );
        }
    }
}
