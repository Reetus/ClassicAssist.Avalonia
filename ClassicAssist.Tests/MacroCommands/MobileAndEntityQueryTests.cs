using System;
using ClassicAssist.Data;
using ClassicAssist.Data.Friends;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using Xunit;
using static ClassicAssist.Tests.MacroCommands.MacroCommandTestSupport;

namespace ClassicAssist.Tests.MacroCommands
{
    /// <summary>
    ///     The read-only queries macros branch on (<c>if Dead("enemy")</c>, <c>while DiffHits() &gt; 20</c>):
    ///     mobile status flags, health bar colours, friend and party membership, and the entity position /
    ///     distance helpers. Each has its own way of treating an unknown alias or a missing mobile, and a
    ///     macro that misreads "not found" as a real value acts on it - so those paths are pinned too.
    /// </summary>
    public class MobileAndEntityQueryTests : IDisposable
    {
        private const int OTHER = 0x00000002;
        private readonly Options _originalOptions;
        private readonly PlayerMobile _player;

        public MobileAndEntityQueryTests()
        {
            _originalOptions = Options.CurrentOptions;
            Options.CurrentOptions = FastOptions();

            _player = EngineState.ResetWithPlayer();
            AliasCommands.SetAlias( "self", _player.Serial );
        }

        public void Dispose()
        {
            Options.CurrentOptions = _originalOptions;
            EngineState.Reset();
        }

        private Mobile AddMobile( int serial = OTHER, int x = 0, int y = 0 )
        {
            Mobile mobile = new Mobile( serial ) { ID = 0x190, X = x, Y = y };
            Engine.Mobiles.Add( mobile );

            return mobile;
        }

        [Fact]
        public void StatusFlagQueriesWillReadTheMobilesStatus()
        {
            Mobile mobile = AddMobile();
            mobile.Status = MobileStatus.Hidden | MobileStatus.WarMode | MobileStatus.Frozen;

            Assert.True( MobileCommands.Hidden( OTHER ) );
            Assert.True( MobileCommands.War( OTHER ) );
            Assert.True( MobileCommands.Paralyzed( OTHER ) );

            mobile.Status = MobileStatus.None;

            Assert.False( MobileCommands.Hidden( OTHER ) );
            Assert.False( MobileCommands.War( OTHER ) );
            Assert.False( MobileCommands.Paralyzed( OTHER ) );
        }

        [Fact]
        public void PoisonedAndYellowHitsWillFollowTheHealthbarColour()
        {
            Mobile mobile = AddMobile();

            mobile.HealthbarColour = HealthbarColour.Green;
            Assert.True( MobileCommands.Poisoned( OTHER ) );
            Assert.False( MobileCommands.YellowHits( OTHER ) );

            mobile.HealthbarColour = HealthbarColour.Yellow;
            Assert.False( MobileCommands.Poisoned( OTHER ) );
            Assert.True( MobileCommands.YellowHits( OTHER ) );
        }

        [Fact]
        public void PoisonedWillReadTheFlyingBitOnPreSevenClients()
        {
            // Before 7.0 the server sent poison as status bit 0x04, which this tree names Flying.
            Engine.ClientVersion = new Version( 5, 0, 9, 1 );
            AddMobile().Status = MobileStatus.Flying;

            Assert.True( MobileCommands.Poisoned( OTHER ) );

            Engine.ClientVersion = EngineState.DefaultClientVersion;

            Assert.False( MobileCommands.Poisoned( OTHER ) );
        }

        [Fact]
        public void DeadWillBeTrueForGhostBodiesWithoutTheDeadFlag()
        {
            Mobile mobile = AddMobile();

            Assert.False( MobileCommands.Dead( OTHER ) );

            mobile.ID = 0x192;

            Assert.True( MobileCommands.Dead( OTHER ) );
        }

        [Fact]
        public void QueriesWillReturnTheDefaultForAnUnknownAliasOrMobile()
        {
            using PacketRecorder recorder = new PacketRecorder();

            Assert.False( MobileCommands.Dead( "nosuchalias" ) );
            Assert.Equal( 0, MobileCommands.Hits( "nosuchalias" ) );
            Assert.Equal( 0, MobileCommands.DiffHits( 0x00000077 ) );
            Assert.False( MobileCommands.Hidden( 0x00000077 ) );
            Assert.False( MobileCommands.Mounted( 0x00000077 ) );

            Assert.Contains( Strings.Mobile_not_found___, SystemMessages( recorder ) );
            Assert.Contains( Strings.Invalid_or_unknown_object_id, SystemMessages( recorder ) );
        }

        [Fact]
        public void HitQueriesWillDefaultToThePlayer()
        {
            // A null argument resolves to the player, not to an alias.
            _player.Hits = 30;
            _player.HitsMax = 120;
            _player.Stamina = 7;
            _player.Mana = 9;

            Assert.Equal( 30, MobileCommands.Hits() );
            Assert.Equal( 120, MobileCommands.MaxHits() );
            Assert.Equal( 90, MobileCommands.DiffHits() );
            Assert.Equal( 75d, MobileCommands.DiffHitsPercent() );
            Assert.Equal( 7, MobileCommands.Stam() );
            Assert.Equal( 9, MobileCommands.Mana() );
        }

        [Fact]
        public void StatQueriesWillReadThePlayerThroughTheSelfAlias()
        {
            _player.Strength = 100;
            _player.Dex = 25;
            _player.Int = 60;

            Assert.Equal( 100, MobileCommands.Str() );
            Assert.Equal( 25, MobileCommands.Dex() );
            Assert.Equal( 60, MobileCommands.Int() );

            AliasCommands.UnsetAlias( "self" );

            Assert.Equal( 0, MobileCommands.Str() );
        }

        [Fact]
        public void PlayerOnlyQueriesWillBeZeroWithoutAPlayer()
        {
            _player.Weight = 300;
            _player.WeightMax = 450;

            Assert.Equal( 150, MobileCommands.DiffWeight() );

            Engine.Player = null;

            Assert.Equal( 0, MobileCommands.DiffWeight() );
            Assert.Equal( 0, MobileCommands.Weight() );
            Assert.Equal( 0, MobileCommands.Followers() );
            Assert.Equal( 0, MobileCommands.Gold() );
        }

        [Fact]
        public void MountedWillFollowTheMountLayer()
        {
            Mobile mobile = AddMobile();

            Assert.False( MobileCommands.Mounted( OTHER ) );

            Engine.Items.Add( new Item( 0x40000080, OTHER ) { ID = 0x3EA2, Layer = Layer.Mount } );
            mobile.SetLayer( Layer.Mount, 0x40000080 );

            Assert.True( MobileCommands.Mounted( OTHER ) );
        }

        [Fact]
        public void InFriendListWillIncludePartyMembersOnlyWhenTheOptionIsOn()
        {
            AddMobile();
            AddMobile( 0x00000003 );
            Options.CurrentOptions.Friends.Add( new FriendEntry { Name = "pal", Serial = 0x00000003 } );
            _player.Party = new[] { OTHER };

            Assert.True( MobileCommands.InFriendList( 0x00000003 ) );
            Assert.True( MobileCommands.InParty( OTHER ) );

            Options.CurrentOptions.IncludePartyMembersInFriends = false;
            Assert.False( MobileCommands.InFriendList( OTHER ) );

            Options.CurrentOptions.IncludePartyMembersInFriends = true;
            Assert.True( MobileCommands.InFriendList( OTHER ) );
        }

        [Fact]
        public void AddFriendWillAddOnceAndRemoveFriendWillRemoveIt()
        {
            AddMobile().Name = " Bob ";

            Assert.Equal( OTHER, MobileCommands.AddFriend( OTHER ) );
            Assert.Equal( OTHER, MobileCommands.AddFriend( OTHER ) );

            FriendEntry entry = Assert.Single( Options.CurrentOptions.Friends );
            Assert.Equal( "Bob", entry.Name );

            MobileCommands.RemoveFriend( OTHER );

            Assert.Empty( Options.CurrentOptions.Friends );
        }

        [Fact]
        public void AddFriendWillRefuseAMobileItDoesNotKnow()
        {
            Assert.Equal( 0, MobileCommands.AddFriend( 0x00000077 ) );
            Assert.Empty( Options.CurrentOptions.Friends );
        }

        [Fact]
        public void DistanceWillBeChebyshevAndMaxValueForUnknownEntities()
        {
            _player.X = 100;
            _player.Y = 100;
            AddMobile( OTHER, 103, 95 );

            Assert.Equal( 5, EntityCommands.Distance( OTHER ) );
            Assert.Equal( 5, EntityCommands.Distance( 95, 104 ) );
            Assert.Equal( int.MaxValue, EntityCommands.Distance( 0x00000077 ) );
        }

        [Fact]
        public void InRangeWillExcludeTheBoundary()
        {
            _player.X = 100;
            _player.Y = 100;
            AddMobile( OTHER, 103, 100 );

            Assert.False( EntityCommands.InRange( OTHER, 3 ) );
            Assert.True( EntityCommands.InRange( OTHER, 4 ) );
            Assert.False( EntityCommands.InRange( 0x00000077, 100 ) );
        }

        [Fact]
        public void PositionQueriesWillLookUpItemsAndMobilesBySerialKind()
        {
            AddMobile( OTHER, 10, 20 ).Z = 5;
            Engine.Items.Add( new Item( 0x40000090 ) { ID = 0xF7A, Hue = 0x21, X = 30, Y = 40, Z = -5 } );

            Assert.Equal( 10, EntityCommands.X( OTHER ) );
            Assert.Equal( 20, EntityCommands.Y( OTHER ) );
            Assert.Equal( 5, EntityCommands.Z( OTHER ) );
            Assert.Equal( 30, EntityCommands.X( 0x40000090 ) );
            Assert.Equal( -5, EntityCommands.Z( 0x40000090 ) );
            Assert.Equal( 0x21, EntityCommands.Hue( 0x40000090 ) );
            Assert.Equal( 0xF7A, EntityCommands.Graphic( 0x40000090 ) );
            Assert.Equal( 0x190, EntityCommands.Graphic( OTHER ) );
        }

        [Fact]
        public void EntityQueriesWillStayQuietInQuietModeWhenNotFound()
        {
            using PacketRecorder recorder = new PacketRecorder();

            try
            {
                MacroManager.QuietMode = true;

                Assert.Equal( 0, EntityCommands.Hue( 0x40000091 ) );
                Assert.Equal( 0, EntityCommands.X( 0x40000091 ) );

                Assert.Empty( SystemMessages( recorder ) );

                MacroManager.QuietMode = false;

                Assert.Equal( 0, EntityCommands.Graphic( 0x40000091 ) );

                Assert.Contains( Strings.Entity_not_found___, SystemMessages( recorder ) );
            }
            finally
            {
                MacroManager.QuietMode = false;
            }
        }

        [Fact]
        public void DirectionToWillMapTheBearingAndBeInvalidForUnknownEntities()
        {
            _player.X = 100;
            _player.Y = 100;
            AddMobile( OTHER, 100, 90 );
            AddMobile( 0x00000003, 110, 110 );

            Assert.Equal( nameof( Direction.North ), EntityCommands.DirectionTo( OTHER ) );
            Assert.Equal( nameof( Direction.Southeast ), EntityCommands.DirectionTo( 0x00000003 ) );
            Assert.Equal( nameof( Direction.Invalid ), EntityCommands.DirectionTo( 0x00000077 ) );
        }

        [Fact]
        public void NameWillBeTrimmedAndEmptyForUnknownEntities()
        {
            AddMobile().Name = "  Lord British ";

            Assert.Equal( "Lord British", EntityCommands.Name( OTHER ) );
            Assert.Equal( string.Empty, EntityCommands.Name( 0x00000077 ) );
            Assert.Equal( string.Empty, EntityCommands.Name( "nosuchalias" ) );
        }
    }
}
