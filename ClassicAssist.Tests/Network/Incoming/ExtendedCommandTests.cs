using System.Collections.Generic;
using ClassicAssist.Data;
using ClassicAssist.Data.Friends;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests.Network.Incoming
{
    /// <summary>
    ///     The 0xBF extended command sub-handlers: party (list, removal, message, invitation with the
    ///     auto-accept options), map change, miscellaneous status (bonded pets and the statue variant that
    ///     reuses the same sub-command), special move toggles and movement speed. Party membership feeds the
    ///     friend/party macro commands and the auto-accept option answers the server on the player's behalf,
    ///     so who it accepts from matters.
    /// </summary>
    public class ExtendedCommandTests : IncomingPacketTestBase
    {
        private const int MEMBER1 = 0x00000801;
        private const int MEMBER2 = 0x00000802;
        private const int LEADER = 0x00000803;

        [Fact]
        public void WillSetPartyMembersOnPartyList()
        {
            Receive( Extended( 0x06, w =>
            {
                w.Write( (byte) 1 );
                w.Write( (byte) 3 );
                w.Write( PLAYER );
                w.Write( MEMBER1 );
                w.Write( MEMBER2 );
            } ) );

            Assert.Equal( new[] { PLAYER, MEMBER1, MEMBER2 }, Player.Party );
        }

        /// <summary>The removal message names the removed member first; it is not part of the new list.</summary>
        [Fact]
        public void WillSkipRemovedMemberOnPartyRemove()
        {
            Player.Party = new[] { PLAYER, MEMBER1, MEMBER2 };

            Receive( Extended( 0x06, w =>
            {
                w.Write( (byte) 2 );
                w.Write( (byte) 2 );
                w.Write( MEMBER1 ); // removed
                w.Write( PLAYER );
                w.Write( MEMBER2 );
            } ) );

            Assert.Equal( new[] { PLAYER, MEMBER2 }, Player.Party );
        }

        [Fact]
        public void WillJournalPartyMessageWithSenderName()
        {
            AddMobile( MEMBER1, "Alice" );

            Receive( PartyMessage( MEMBER1, "heal me" ) );
            Receive( PartyMessage( MEMBER2, "who am i" ) );

            JournalEntry[] entries = Engine.Journal.GetEntireBuffer();

            Assert.Equal( 2, entries.Length );
            Assert.Equal( "Alice", entries[0].Name );
            Assert.Equal( "heal me", entries[0].Text );
            Assert.Equal( JournalSpeech.Whisper, entries[0].SpeechType );
            Assert.Equal( "Unknown", entries[1].Name );
        }

        [Theory]
        [InlineData( false, false, false, false )]
        [InlineData( true, false, false, true )]
        [InlineData( true, true, false, false )]
        [InlineData( true, true, true, true )]
        public void WillAutoAcceptPartyInviteOnlyAsConfigured( bool autoAccept, bool onlyFriends, bool leaderIsFriend,
            bool expectAccept )
        {
            Options.CurrentOptions.AutoAcceptPartyInvite = autoAccept;
            Options.CurrentOptions.AutoAcceptPartyOnlyFromFriends = onlyFriends;

            if ( leaderIsFriend )
            {
                Options.CurrentOptions.Friends.Add( new FriendEntry { Name = "Leader", Serial = LEADER } );
            }

            using PacketRecorder recorder = new PacketRecorder();

            Receive( Extended( 0x06, w =>
            {
                w.Write( (byte) 7 );
                w.Write( LEADER );
            } ) );

            if ( !expectAccept )
            {
                Assert.Empty( recorder.Sent );
                return;
            }

            // 0xBF 0x0006 0x08: accept, with the leader's serial
            byte[] accept = Assert.Single( recorder.SentWithId( 0xBF ) );

            Assert.Equal( new byte[] { 0xBF, 0x00, 0x0A, 0x00, 0x06, 0x08, 0x00, 0x00, 0x08, 0x03 }, accept );
        }

        [Fact]
        public void WillRaiseMapChangedEventOnMapChange()
        {
            List<Map> raised = new List<Map>();

            void OnMap( Map map )
            {
                raised.Add( map );
            }

            IncomingPacketHandlers.MapChangedEvent += OnMap;

            try
            {
                Receive( Extended( 0x08, w => w.Write( (byte) Map.Ilshenar ) ) );
            }
            finally
            {
                IncomingPacketHandlers.MapChangedEvent -= OnMap;
            }

            Assert.Equal( Map.Ilshenar, Assert.Single( raised ) );
            Assert.Equal( Map.Ilshenar, Player.Map );
        }

        [Fact]
        public void WillSetDeadOnBondedStatus()
        {
            Mobile pet = AddMobile( MEMBER1 );

            Receive( MiscStatus( 0x00, MEMBER1, 0x01 ) );

            Assert.True( pet.IsDead );

            Receive( MiscStatus( 0x00, MEMBER1, 0x00 ) );

            Assert.False( pet.IsDead );
        }

        /// <summary>
        ///     Sub-command 0x05 is shared: with type 0xFF and all-zero animation data it is the newer bonded
        ///     status form and falls back to the 0x00 parsing; with real animation data it updates a statue
        ///     and must leave the mobile alone.
        /// </summary>
        [Fact]
        public void WillTreatZeroAnimationAsBondedStatusOnMiscStatusFive()
        {
            Mobile pet = AddMobile( MEMBER1 );

            // dead byte 1, type2 0xFF, status 0, animation 0, frame 0
            Receive( MiscStatus( 0x05, MEMBER1, 0x01, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00 ) );

            Assert.True( pet.IsDead );
        }

        [Fact]
        public void WontChangeDeadOnStatueAnimationOnMiscStatusFive()
        {
            Mobile pet = AddMobile( MEMBER1 );

            // type2 0xFF with a real animation and frame: a statue
            Receive( MiscStatus( 0x05, MEMBER1, 0x01, 0xFF, 0x01, 0x00, 0x11, 0x00, 0x02 ) );

            Assert.False( pet.IsDead );
        }

        [Fact]
        public void WillIgnoreBondedStatusForUnknownMobile()
        {
            Receive( MiscStatus( 0x00, MEMBER1, 0x01 ) );

            Assert.Null( Engine.Mobiles.GetMobile( MEMBER1 ) );
        }

        [Fact]
        public void WillRaiseToggleSpecialMoveEvent()
        {
            List<(int Spell, bool Enabled)> raised = new List<(int, bool)>();

            void OnToggle( int spell, bool enabled )
            {
                raised.Add( ( spell, enabled ) );
            }

            IncomingPacketHandlers.ToggleSpecialMoveEvent += OnToggle;

            try
            {
                Receive( Extended( 0x25, w =>
                {
                    w.Write( (short) 0x0193 );
                    w.Write( (byte) 1 );
                } ) );

                Receive( Extended( 0x25, w =>
                {
                    w.Write( (short) 0x0194 );
                    w.Write( (byte) 0 );
                } ) );
            }
            finally
            {
                IncomingPacketHandlers.ToggleSpecialMoveEvent -= OnToggle;
            }

            Assert.Equal( new[] { ( 0x0193, true ), ( 0x0194, false ) }, raised.ToArray() );
        }

        [Fact]
        public void WillSetMovementSpeed()
        {
            Receive( Extended( 0x26, w => w.Write( (byte) 2 ) ) );

            Assert.Equal( 2, Player.MovementSpeed );
        }

        /// <summary>
        ///     Sphere sends sub-commands above 0xFF; unhandled ones, in or out of the table, are ignored.
        /// </summary>
        [Theory]
        [InlineData( 0x0014 )]
        [InlineData( 0x01FF )]
        [InlineData( 0xFFFF )]
        public void WillIgnoreUnhandledExtendedSubCommand( int subCommand )
        {
            Receive( Extended( subCommand, w => w.Write( 0x12345678 ) ) );

            Assert.Empty( Engine.Journal.GetEntireBuffer() );
            Assert.Equal( Map.Felucca, Player.Map );
        }

        private static byte[] PartyMessage( int serial, string text )
        {
            return Extended( 0x06, w =>
            {
                w.Write( (byte) 4 );
                w.Write( serial );
                w.WriteBigUniNull( text );
            } );
        }

        private static byte[] MiscStatus( byte type, int serial, params byte[] data )
        {
            return Extended( 0x19, w =>
            {
                w.Write( type );
                w.Write( serial );
                w.Write( data, 0, data.Length );
            } );
        }
    }
}
