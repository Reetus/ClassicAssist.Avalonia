using System;
using System.Collections.Generic;
using System.Linq;
using ClassicAssist.Data;
using ClassicAssist.Data.Counters;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using Xunit;

namespace ClassicAssist.Tests.Network.Incoming
{
    /// <summary>
    ///     Login and session state the server sends once or rarely: 0xB9 supported features (whose size
    ///     depends on the client version), 0xA9 character list (city entries grew in 7.0.13 and the flags
    ///     that decide tooltips come after them), 0xA8 shard list, prompts, movement acknowledgements, lift
    ///     rejections and the resurrection / corpse waypoints. Each is parsed by position, so a version gate
    ///     in the wrong place reads the flags from the middle of a city name.
    /// </summary>
    public class SessionPacketTests : IncomingPacketTestBase
    {
        private readonly CharacterListFlags _originalCharacterListFlags;
        private readonly bool _originalTooltips;

        public SessionPacketTests()
        {
            _originalCharacterListFlags = Engine.CharacterListFlags;
            _originalTooltips = Engine.TooltipsEnabled;
        }

        public override void Dispose()
        {
            Engine.CharacterListFlags = _originalCharacterListFlags;
            Engine.TooltipsEnabled = _originalTooltips;
            Engine.SetSequence( 5, Direction.North );

            base.Dispose();
        }

        [Fact]
        public void WillSetFeaturesOnSupportedFeatures()
        {
            Receive( new byte[] { 0xB9, 0x00, 0x00, 0x80, 0xDB } );

            Assert.Equal( (FeatureFlags) 0x80DB, Engine.Features );
            Assert.True( Engine.Features.HasFlag( FeatureFlags.AOS ) );
        }

        /// <summary>
        ///     Clients before 6.0.14.2 get a 3-byte 0xB9 with 16-bit flags (ClassicUO reads a UInt16 for them).
        ///     The handler always reads 32 bits, so the two flag bytes land in the high half: T2A | UOR | AOS
        ///     comes out as SA | HS | ... and AOS-only commands (wands, autoloot property checks) see a
        ///     pre-AOS shard.
        /// </summary>
        [Fact( Skip = "BUG: 3-byte 0xB9 from pre-6.0.14.2 clients is read as 32 bits, shifting the flags (IncomingPacketHandlers.OnSupportedFeatures)" )]
        public void WillReadSixteenBitFlagsOnOldSupportedFeatures()
        {
            Engine.ClientVersion = new Version( 5, 0, 9, 1 );

            Receive( new byte[] { 0xB9, 0x00, 0x13 } ); // T2A | UOR | AOS

            Assert.Equal( FeatureFlags.T2A | FeatureFlags.UOR | FeatureFlags.AOS, Engine.Features );
        }

        [Theory]
        [InlineData( 7, 0, 45, 1 )]
        [InlineData( 7, 0, 13, 0 )]
        [InlineData( 7, 0, 12, 0 )]
        [InlineData( 5, 0, 9, 1 )]
        public void WillReadFlagsAfterCitiesOnCharacterList( int major, int minor, int build, int revision )
        {
            Engine.ClientVersion = new Version( major, minor, build, revision );
            bool newCities = Engine.ClientVersion >= new Version( 7, 0, 13, 0 );

            Receive( CharacterList( newCities, CharacterListFlags.PaladinNecromancerClassTooltips |
                                               CharacterListFlags.SamuraiNinjaClasses |
                                               CharacterListFlags.ElvenRace ) );

            Assert.Equal(
                CharacterListFlags.PaladinNecromancerClassTooltips | CharacterListFlags.SamuraiNinjaClasses |
                CharacterListFlags.ElvenRace, Engine.CharacterListFlags );
            Assert.True( Engine.TooltipsEnabled );
        }

        [Fact]
        public void WillDisableTooltipsWithoutFlagOnCharacterList()
        {
            Engine.TooltipsEnabled = true;

            Receive( CharacterList( true, CharacterListFlags.ElvenRace ) );

            Assert.False( Engine.TooltipsEnabled );
        }

        [Fact]
        public void WillListShardsOnShardList()
        {
            Receive( Dynamic( 0xA8, w =>
            {
                w.Write( (byte) 0x5D );
                w.Write( (short) 2 );

                w.Write( (short) 0 );
                w.WriteAsciiFixed( "Atlantic", 32 );
                w.Write( (byte) 45 );
                w.Write( (sbyte) -5 );
                w.Write( 0x0100007F );

                w.Write( (short) 1 );
                w.WriteAsciiFixed( "Europa", 32 );
                w.Write( (byte) 0 );
                w.Write( (sbyte) 1 );
                w.Write( 0x0200007F );
            } ) );

            Assert.Equal( 2, Engine.Shards.Count );
            Assert.Equal( "Atlantic", Engine.Shards[0].Name );
            Assert.Equal( 45, Engine.Shards[0].PercentFull );
            Assert.Equal( -5, Engine.Shards[0].Timezone );
            Assert.Equal( 0x0100007F, Engine.Shards[0].IP );
            Assert.Equal( 1, Engine.Shards[1].Index );
            Assert.Equal( "Europa", Engine.Shards[1].Name );
        }

        /// <summary>The prompt type decides whether PromptMsg answers with 0x9A or 0xC2.</summary>
        [Fact]
        public void WillRecordPromptAndTypeOnAsciiAndUnicodePrompts()
        {
            Receive( Prompt( 0xC2, 0x00000B01, 0x0B0B ) );

            Assert.Equal( 0x00000B01, Engine.LastPromptSerial );
            Assert.Equal( 0x0B0B, Engine.LastPromptID );
            Assert.Equal( 1, Engine.LastPromptType );

            Receive( Prompt( 0x9A, 0x00000B02, 0x0C0C ) );

            Assert.Equal( 0x00000B02, Engine.LastPromptSerial );
            Assert.Equal( 0x0C0C, Engine.LastPromptID );
            Assert.Equal( 0, Engine.LastPromptType );
        }

        [Fact]
        public void WillTakeDirectionFromRejectedMove()
        {
            Player.Direction = Direction.North;

            Receive( new byte[] { 0x21, 0x05, 0x03, 0xE8, 0x03, 0xE8, (byte) Direction.Southwest, 0x00 } );

            Assert.Equal( Direction.Southwest, Player.Direction );
        }

        /// <summary>0x22 only names the sequence; the direction is the one recorded when that step was sent.</summary>
        [Fact]
        public void WillTakeDirectionOfSequenceFromAcceptedMove()
        {
            Engine.SetSequence( 5, Direction.East );

            Receive( new byte[] { 0x22, 0x05, 0x01 } );

            Assert.Equal( Direction.East, Player.Direction );
        }

        [Theory]
        [InlineData( new byte[] { 0x27, 0x00 } )]
        [InlineData( new byte[] { 0x28, 0x40, 0x00, 0x0C, 0x01 } )]
        [InlineData( new byte[] { 0x29 } )]
        public void WillClearHoldingAndRecountOnLiftRejectOrDrop( byte[] packet )
        {
            Player.Holding = 0x40000C01;
            Player.HoldingAmount = 5;

            CountersManager counters = CountersManager.GetInstance();
            Action original = counters.RecountAll;
            int recounts = 0;

            counters.RecountAll = () => recounts++;

            try
            {
                Receive( packet );
            }
            finally
            {
                counters.RecountAll = original;
            }

            Assert.Equal( 0, Player.Holding );
            Assert.Equal( 0, Player.HoldingAmount );
            Assert.Equal( 1, recounts );
        }

        [Fact]
        public void WillAddCorpsePointerOnDisplayWaypoint()
        {
            Options.CurrentOptions.ShowResurrectionWaypoints = true;

            Receive( Waypoint( 0x40000D01, 1200, 1300, 0x01 ) );

            QuestPointer pointer = Assert.Single( Engine.QuestPointers );

            Assert.Equal( 0x40000D01, pointer.Serial );
            Assert.Equal( QuestPointerType.Corpse, pointer.Type );
            Assert.Equal( 1200, pointer.X );
            Assert.Equal( 1300, pointer.Y );
        }

        /// <summary>
        ///     A resurrection point within 100 tiles is announced with its direction and distance; any
        ///     resurrection point is remembered as a pointer.
        /// </summary>
        [Fact]
        public void WillAnnounceNearResurrectionWaypointOnly()
        {
            Options.CurrentOptions.ShowResurrectionWaypoints = true;

            using PacketRecorder recorder = new PacketRecorder();

            Receive( Waypoint( 0x40000D02, 1050, 1000, 0x06 ) ); // 50 tiles east
            Receive( Waypoint( 0x40000D03, 1500, 1000, 0x06 ) ); // 500 tiles east

            Assert.Single( recorder.Received, p => p[0] != 0xBA ); // one system message
            Assert.Equal( 2, recorder.ReceivedWithId( 0xBA ).Count ); // both pointers shown
            Assert.Equal( 2, Engine.QuestPointers.Count() );
            Assert.All( Engine.QuestPointers, p => Assert.Equal( QuestPointerType.Resurrection, p.Type ) );
        }

        [Fact]
        public void WontAddWaypointWhenOptionDisabled()
        {
            Options.CurrentOptions.ShowResurrectionWaypoints = false;

            Receive( Waypoint( 0x40000D01, 1200, 1300, 0x01 ) );

            Assert.Empty( Engine.QuestPointers );
        }

        [Fact]
        public void WillRemoveEveryPointerForSerialOnHideWaypoint()
        {
            Options.CurrentOptions.ShowResurrectionWaypoints = true;

            Receive( Waypoint( 0x40000D01, 1200, 1300, 0x01 ) );
            Receive( Waypoint( 0x40000D01, 1201, 1301, 0x01 ) );
            Receive( Waypoint( 0x40000D02, 1200, 1300, 0x01 ) );

            Receive( new byte[] { 0xE6, 0x00, 0x07, 0x40, 0x00, 0x0D, 0x01 } );

            Assert.Equal( 0x40000D02, Assert.Single( Engine.QuestPointers ).Serial );
        }

        private static byte[] Waypoint( int serial, int x, int y, int type )
        {
            return Dynamic( 0xE5, w =>
            {
                w.Write( serial );
                w.Write( (short) x );
                w.Write( (short) y );
                w.Write( (sbyte) 0 ); // z
                w.Write( (byte) Map.Felucca );
                w.Write( (short) type );
                w.Write( (short) 0 ); // ignore object
                w.Write( 500000 ); // description cliloc
                w.WriteLittleUniNull( "" );
            } );
        }

        private static byte[] Prompt( byte id, int serial, int promptId )
        {
            return Dynamic( id, w =>
            {
                w.Write( serial );
                w.Write( promptId );
                w.Write( 0 ); // type
                w.Write( 0 ); // language / text
            } );
        }

        /// <summary>
        ///     0xA9 with two characters and two cities; a city is 63 bytes before 7.0.13, 89 bytes from it.
        /// </summary>
        private static byte[] CharacterList( bool newCities, CharacterListFlags flags )
        {
            return Dynamic( 0xA9, w =>
            {
                w.Write( (byte) 2 );

                foreach ( string name in new[] { "First", "Second" } )
                {
                    w.WriteAsciiFixed( name, 30 );
                    w.WriteAsciiFixed( "", 30 );
                }

                w.Write( (byte) 2 );

                for ( int i = 0; i < 2; i++ )
                {
                    w.Write( (byte) i );
                    w.WriteAsciiFixed( "Britain", newCities ? 32 : 31 );
                    w.WriteAsciiFixed( "The Wayfarer's Inn", newCities ? 32 : 31 );

                    if ( newCities )
                    {
                        w.Write( 1496 );
                        w.Write( 1628 );
                        w.Write( 10 );
                        w.Write( 0 );
                        w.Write( 1075074 );
                        w.Write( 0 );
                    }
                }

                w.Write( (int) flags );
            } );
        }
    }
}
