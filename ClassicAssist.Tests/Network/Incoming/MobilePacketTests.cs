using System;
using System.Collections.Generic;
using System.Linq;
using ClassicAssist.Data.Screenshot;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests.Network.Incoming
{
    /// <summary>
    ///     The server packets that create and update mobiles: 0x78 incoming (both equipment layouts), 0x77
    ///     moving, 0x20 update, 0x11 status (each feature level), 0xA1-0xA3 hits/mana/stamina, 0x98 name,
    ///     0x1B player initialisation, 0x72 war mode and the death packets. These are what every mobile
    ///     command, the friends list, the ECV and the agents read, and a field read from the wrong offset
    ///     (a status block one version too long, a hue present only in the old layout) silently shifts every
    ///     later field rather than failing.
    /// </summary>
    public class MobilePacketTests : IncomingPacketTestBase
    {
        private const int MOBILE = 0x00000202;

        [Fact]
        public void WillAddMobileWithEquipmentAndLayersFromMobileIncoming()
        {
            List<(Mobile Mobile, ItemCollection Equipment)> raised = new List<(Mobile, ItemCollection)>();

            void OnIncoming( Mobile mobile, ItemCollection equipment )
            {
                raised.Add( ( mobile, equipment ) );
            }

            IncomingPacketHandlers.MobileIncomingEvent += OnIncoming;

            try
            {
                // Direction 0x82: running bit set, facing east (2).
                Receive( MobileIncoming( MOBILE, 0x190, 1010, 1020, -5, 0x82, 0x83EA, MobileStatus.Female,
                    Notoriety.Murderer, false, ( 0x40000301, 0x1415, Layer.InnerTorso, 0x0455 ),
                    ( 0x40000302, 0x0E75, Layer.Backpack, 0 ) ) );
            }
            finally
            {
                IncomingPacketHandlers.MobileIncomingEvent -= OnIncoming;
            }

            Mobile mobile = Engine.Mobiles.GetMobile( MOBILE );

            Assert.NotNull( mobile );
            Assert.Equal( 0x190, mobile.ID );
            Assert.Equal( 1010, mobile.X );
            Assert.Equal( 1020, mobile.Y );
            Assert.Equal( -5, mobile.Z );
            Assert.Equal( Direction.East, mobile.Direction );
            Assert.Equal( 0x83EA, mobile.Hue );
            Assert.Equal( MobileStatus.Female, mobile.Status );
            Assert.Equal( Notoriety.Murderer, mobile.Notoriety );

            Assert.Equal( 0x40000301, mobile.GetLayer( Layer.InnerTorso ) );
            Assert.Equal( 0x40000302, mobile.GetLayer( Layer.Backpack ) );

            Item torso = Engine.Items.GetItem( 0x40000301 );

            Assert.NotNull( torso );
            Assert.Equal( 0x1415, torso.ID );
            Assert.Equal( 0x0455, torso.Hue );
            Assert.Equal( MOBILE, torso.Owner );
            Assert.Equal( Layer.InnerTorso, torso.Layer );

            (Mobile eventMobile, ItemCollection eventEquipment) = Assert.Single( raised );
            Assert.Same( mobile, eventMobile );
            Assert.Equal( 2, eventEquipment.GetItemCount() );
        }

        /// <summary>
        ///     Before 7.0.33.1 a 0x78 equipment entry carries a hue only when its id has the 0x8000 bit set.
        ///     Reading a hue for every entry, as the new layout does, would take the next entry's serial
        ///     apart.
        /// </summary>
        [Fact]
        public void WillReadHueOnlyWhenFlaggedInOldMobileIncoming()
        {
            Engine.ClientVersion = new Version( 7, 0, 10, 0 );

            Receive( MobileIncoming( MOBILE, 0x190, 1010, 1020, 0, 0, 0x0400, MobileStatus.None, Notoriety.Innocent,
                true, ( 0x40000301, 0x1415, Layer.InnerTorso, 0x0455 ), ( 0x40000302, 0x0E75, Layer.Backpack, 0 ),
                ( 0x40000303, 0x13B9, Layer.OneHanded, 0x0021 ) ) );

            Mobile mobile = Engine.Mobiles.GetMobile( MOBILE );

            Item torso = Engine.Items.GetItem( 0x40000301 );
            Item backpack = Engine.Items.GetItem( 0x40000302 );
            Item sword = Engine.Items.GetItem( 0x40000303 );

            Assert.Equal( 0x1415, torso.ID ); // the 0x8000 flag is stripped from the id
            Assert.Equal( 0x0455, torso.Hue );
            Assert.Equal( 0x0E75, backpack.ID );
            Assert.Equal( 0, backpack.Hue );
            Assert.Equal( 0x13B9, sword.ID );
            Assert.Equal( 0x0021, sword.Hue );
            Assert.Equal( 0x40000303, mobile.GetLayer( Layer.OneHanded ) );
        }

        /// <summary>
        ///     A 0x78 is the mobile's whole equipment list. The previous list must not survive alongside it.
        /// </summary>
        [Fact]
        public void WillReplaceEquipmentListOnRepeatedMobileIncoming()
        {
            Receive( MobileIncoming( MOBILE, 0x190, 1010, 1020, 0, 0, 0, MobileStatus.None, Notoriety.Innocent, false,
                ( 0x40000301, 0x1415, Layer.InnerTorso, 0 ), ( 0x40000302, 0x0E75, Layer.Backpack, 0 ) ) );

            Receive( MobileIncoming( MOBILE, 0x190, 1011, 1021, 0, 0, 0, MobileStatus.None, Notoriety.Innocent, false,
                ( 0x40000302, 0x0E75, Layer.Backpack, 0 ), ( 0x40000304, 0x1F03, Layer.OuterTorso, 0 ) ) );

            Mobile mobile = Engine.Mobiles.GetMobile( MOBILE );

            Assert.Equivalent( new[] { 0x40000302, 0x40000304 },
                mobile.Equipment.GetItems().Select( i => i.Serial ).ToArray(), true );
            Assert.Equal( 1011, mobile.X );
            Assert.Single( Engine.Mobiles.GetMobiles(), m => m.Serial == MOBILE );
        }

        [Fact]
        public void WillUpdatePlayerRatherThanAddMobileOnMobileIncomingForPlayer()
        {
            Receive( MobileIncoming( PLAYER, 0x191, 1500, 1600, 10, 3, 0x0410, MobileStatus.Female,
                Notoriety.Innocent, false ) );

            Assert.Same( Player, Engine.Player );
            Assert.Equal( 0x191, Player.ID );
            Assert.Equal( 1500, Player.X );
            Assert.Equal( 1600, Player.Y );
            Assert.Equal( 0x0410, Player.Hue );
            Assert.Equal( MobileStatus.Female, Player.Status );
        }

        /// <summary>
        ///     0x77's direction byte carries the running flag in its high bit. Only the low three bits are a
        ///     direction; keeping the flag would give a Direction outside the enum.
        /// </summary>
        [Fact]
        public void WillMaskRunningFlagAndSetNotorietyOnMobileMoving()
        {
            Mobile mobile = AddMobile( MOBILE );

            Receive( Fixed( 0x77, w =>
            {
                w.Write( MOBILE );
                w.Write( (short) 0x0191 ); // body
                w.Write( (short) 1234 ); // x
                w.Write( (short) 2345 ); // y
                w.Write( (sbyte) -20 ); // z
                w.Write( (byte) 0x86 ); // running, west
                w.Write( (ushort) 0x8021 ); // hue
                w.Write( (byte) ( MobileStatus.WarMode | MobileStatus.Hidden ) );
                w.Write( (byte) Notoriety.Criminal );
            } ) );

            Assert.Same( mobile, Engine.Mobiles.GetMobile( MOBILE ) );
            Assert.Equal( 0x0191, mobile.ID );
            Assert.Equal( 1234, mobile.X );
            Assert.Equal( 2345, mobile.Y );
            Assert.Equal( -20, mobile.Z );
            Assert.Equal( Direction.West, mobile.Direction );
            Assert.Equal( 0x8021, mobile.Hue );
            Assert.Equal( MobileStatus.WarMode | MobileStatus.Hidden, mobile.Status );
            Assert.Equal( Notoriety.Criminal, mobile.Notoriety );
        }

        [Fact]
        public void WillAddUnknownMobileOnMobileMoving()
        {
            Receive( Fixed( 0x77, w =>
            {
                w.Write( MOBILE );
                w.Write( (short) 0x00C9 );
                w.Write( (short) 10 );
                w.Write( (short) 20 );
                w.Write( (sbyte) 0 );
                w.Write( (byte) 0 );
                w.Write( (short) 0 );
                w.Write( (byte) 0 );
                w.Write( (byte) Notoriety.Attackable );
            } ) );

            Mobile mobile = Engine.Mobiles.GetMobile( MOBILE );

            Assert.NotNull( mobile );
            Assert.Equal( Notoriety.Attackable, mobile.Notoriety );
        }

        [Fact]
        public void WillRaiseMobileUpdatedEventOnMobileUpdate()
        {
            List<Mobile> raised = new List<Mobile>();

            void OnUpdated( Mobile mobile )
            {
                raised.Add( mobile );
            }

            IncomingPacketHandlers.MobileUpdatedEvent += OnUpdated;

            try
            {
                Receive( Fixed( 0x20, w =>
                {
                    w.Write( MOBILE );
                    w.Write( (short) 0x0190 );
                    w.Write( (byte) 0 );
                    w.Write( (short) 0x0455 ); // hue
                    w.Write( (byte) MobileStatus.Frozen );
                    w.Write( (short) 777 ); // x
                    w.Write( (short) 888 ); // y
                    w.Write( (short) 0 );
                    w.Write( (byte) 0x84 ); // running, south
                    w.Write( (sbyte) 15 ); // z
                } ) );
            }
            finally
            {
                IncomingPacketHandlers.MobileUpdatedEvent -= OnUpdated;
            }

            Mobile mobile = Assert.Single( raised );

            Assert.Same( Engine.Mobiles.GetMobile( MOBILE ), mobile );
            Assert.Equal( 0x0455, mobile.Hue );
            Assert.True( mobile.IsFrozen );
            Assert.Equal( 777, mobile.X );
            Assert.Equal( 888, mobile.Y );
            Assert.Equal( 15, mobile.Z );
            Assert.Equal( Direction.South, mobile.Direction );
        }

        /// <summary>
        ///     A 0x11 for another mobile carries only name and hits; a features byte above zero must not lead
        ///     the handler into reading the player's stat block into it.
        /// </summary>
        [Fact]
        public void WillOnlySetNameAndHitsOnMobileStatusForOtherMobile()
        {
            Mobile mobile = AddMobile( MOBILE, "Old Name" );
            int playerStrength = Player.Strength;

            Receive( StatusPacket( MOBILE, "A Mongbat", 7, 12, true, 6, 100 ) );

            Assert.Equal( "A Mongbat", mobile.Name );
            Assert.Equal( 7, mobile.Hits );
            Assert.Equal( 12, mobile.HitsMax );
            Assert.True( mobile.IsRenamable );
            Assert.Equal( playerStrength, Player.Strength );
        }

        [Fact]
        public void WillStopAfterHitsOnMobileStatusWithNoFeatures()
        {
            Player.Strength = 55;

            Receive( StatusPacket( PLAYER, "Player Renamed", 40, 50, false, 0, 100 ) );

            Assert.Equal( "Player Renamed", Player.Name );
            Assert.Equal( 40, Player.Hits );
            Assert.Equal( 50, Player.HitsMax );
            Assert.Equal( 55, Player.Strength );
        }

        /// <summary>
        ///     Version 1 has no max weight on the wire, so the client's formula (40 + 3.5 * str) stands in.
        /// </summary>
        [Fact]
        public void WillDeriveMaxWeightOnMobileStatusVersionOne()
        {
            Receive( StatusPacket( PLAYER, "Player", 40, 50, false, 1, 100 ) );

            AssertBaseBlock( Player );
            Assert.Equal( 40 + (int) ( 3.5 * 100 ), Player.WeightMax );
            Assert.Equal( 0, Player.StatCap );
        }

        [Fact]
        public void WillReadEveryBlockOnMobileStatusVersionSix()
        {
            Receive( StatusPacket( PLAYER, "Player", 40, 50, false, 6, 100 ) );

            AssertBaseBlock( Player );

            // version 5
            Assert.Equal( 450, Player.WeightMax );
            Assert.Equal( MobileRace.Gargoyle, Player.Race );

            // version 3
            Assert.Equal( 225, Player.StatCap );
            Assert.Equal( 2, Player.Followers );
            Assert.Equal( 5, Player.FollowersMax );

            // version 4
            Assert.Equal( 61, Player.FireResistance );
            Assert.Equal( 62, Player.ColdResistance );
            Assert.Equal( 63, Player.PoisonResistance );
            Assert.Equal( 64, Player.EnergyResistance );
            Assert.Equal( 140, Player.Luck );
            Assert.Equal( 11, Player.Damage );
            Assert.Equal( 17, Player.DamageMax );
            Assert.Equal( 123456, Player.TithingPoints );

            // version 6
            Assert.Equal( 71, Player.PhysicalResistanceMax );
            Assert.Equal( 72, Player.FireResistanceMax );
            Assert.Equal( 73, Player.ColdResistanceMax );
            Assert.Equal( 74, Player.PoisonResistanceMax );
            Assert.Equal( 75, Player.EnergyResistanceMax );
            Assert.Equal( 20, Player.DefenseChanceIncrease );
            Assert.Equal( 45, Player.DefenseChanceIncreaseMax );
            Assert.Equal( 15, Player.HitChanceIncrease );
            Assert.Equal( 30, Player.SwingSpeedIncrease );
            Assert.Equal( 100, Player.DamageIncrease );
            Assert.Equal( 40, Player.LowerReagentCost );
            Assert.Equal( 35, Player.SpellDamageIncrease );
            Assert.Equal( 6, Player.FasterCastRecovery );
            Assert.Equal( 2, Player.FasterCasting );
            Assert.Equal( 45, Player.LowerManaCost );
        }

        /// <summary>
        ///     Version 3 has the stat cap and followers but not the version 5 weight/race block that sits in
        ///     front of them in later versions.
        /// </summary>
        [Fact]
        public void WillSkipVersionFiveBlockOnMobileStatusVersionThree()
        {
            Receive( StatusPacket( PLAYER, "Player", 40, 50, false, 3, 100 ) );

            AssertBaseBlock( Player );
            Assert.Equal( 390, Player.WeightMax );
            Assert.Equal( 225, Player.StatCap );
            Assert.Equal( 2, Player.Followers );
            Assert.Equal( 5, Player.FollowersMax );
            Assert.Equal( 0, Player.FireResistance );
        }

        [Fact]
        public void WillUpdateKnownMobileOnHitsManaStamina()
        {
            Mobile mobile = AddMobile( MOBILE );

            Receive( StatPacket( 0xA1, MOBILE, 100, 25 ) );
            Receive( StatPacket( 0xA2, MOBILE, 80, 30 ) );
            Receive( StatPacket( 0xA3, MOBILE, 60, 35 ) );

            Assert.Equal( 25, mobile.Hits );
            Assert.Equal( 100, mobile.HitsMax );
            Assert.Equal( 30, mobile.Mana );
            Assert.Equal( 80, mobile.ManaMax );
            Assert.Equal( 35, mobile.Stamina );
            Assert.Equal( 60, mobile.StaminaMax );
        }

        /// <summary>
        ///     Like the client, a hits/mana/stamina update or a name for a mobile not on screen is not
        ///     remembered: the mobile arrives later with a 0x78 / 0x77 and its own values.
        /// </summary>
        [Fact]
        public void WontAddUnknownMobileOnStatUpdateOrName()
        {
            Receive( StatPacket( 0xA1, MOBILE, 100, 25 ) );
            Receive( StatPacket( 0xA2, MOBILE, 100, 25 ) );
            Receive( StatPacket( 0xA3, MOBILE, 100, 25 ) );
            Receive( Dynamic( 0x98, w =>
            {
                w.Write( MOBILE );
                w.WriteAsciiFixed( "Nobody", 30 );
            } ) );

            Assert.Null( Engine.Mobiles.GetMobile( MOBILE ) );
        }

        [Fact]
        public void WillSetNameOnMobileName()
        {
            Mobile mobile = AddMobile( MOBILE, "Old" );

            Receive( Dynamic( 0x98, w =>
            {
                w.Write( MOBILE );
                w.WriteAsciiFixed( "Lord British", 30 );
            } ) );

            Assert.Equal( "Lord British", mobile.Name );
        }

        /// <summary>
        ///     Some servers send packets about the player before 0x1B says which mobile that is, so it can
        ///     already be in the mobile list as an ordinary mobile. 0x1B must take its equipment across and
        ///     not leave the stale entry behind.
        /// </summary>
        [Fact]
        public void WillPromoteExistingMobileOnInitializePlayer()
        {
            const int newPlayer = 0x00000303;

            Receive( MobileIncoming( newPlayer, 0x190, 1, 1, 0, 0, 0, MobileStatus.None, Notoriety.Innocent, false,
                ( 0x40000311, 0x0E75, Layer.Backpack, 0 ) ) );

            Assert.IsNotType<PlayerMobile>( Engine.Mobiles.GetMobile( newPlayer ) );

            Receive( Fixed( 0x1B, w =>
            {
                w.Write( newPlayer );
                w.Write( 0 );
                w.Write( (short) 0x0191 ); // body
                w.Write( (short) 1496 ); // x
                w.Write( (short) 1628 ); // y
                w.Write( (short) 10 ); // z
                w.Write( (byte) Direction.Southeast );
                w.Fill( 37 - 18 );
            } ) );

            PlayerMobile player = Engine.Player;

            Assert.Equal( newPlayer, player.Serial );
            Assert.Equal( 0x0191, player.ID );
            Assert.Equal( 1496, player.X );
            Assert.Equal( 1628, player.Y );
            Assert.Equal( 10, player.Z );
            Assert.Equal( Direction.Southeast, player.Direction );
            Assert.NotNull( player.Equipment.GetItem( 0x40000311 ) );
            Assert.False( Engine.Mobiles.GetMobile( newPlayer, out _ ) );
        }

        [Fact]
        public void WillToggleWarModeAndRaiseMobileUpdatedOnWarMode()
        {
            List<Mobile> raised = new List<Mobile>();

            void OnUpdated( Mobile mobile )
            {
                raised.Add( mobile );
            }

            Player.Status = MobileStatus.Female;

            IncomingPacketHandlers.MobileUpdatedEvent += OnUpdated;

            try
            {
                Receive( new byte[] { 0x72, 0x01, 0x00, 0x32, 0x00 } );

                Assert.Equal( MobileStatus.Female | MobileStatus.WarMode, Player.Status );

                Receive( new byte[] { 0x72, 0x00, 0x00, 0x32, 0x00 } );

                Assert.Equal( MobileStatus.Female, Player.Status );
            }
            finally
            {
                IncomingPacketHandlers.MobileUpdatedEvent -= OnUpdated;
            }

            Assert.Equal( 2, raised.Count );
            Assert.All( raised, m => Assert.Same( Player, m ) );
        }

        /// <summary>0x2C status 0 means the player died; 1 is the resurrection.</summary>
        [Fact]
        public void WillReportPlayerDeathOnlyForDeathStatusZero()
        {
            ScreenshotManager manager = ScreenshotManager.GetInstance();
            Action<string> original = manager.OnPlayerDeath;
            List<string> deaths = new List<string>();

            manager.OnPlayerDeath = name => deaths.Add( name );

            try
            {
                Receive( new byte[] { 0x2C, 0x01 } );
                Receive( new byte[] { 0x2C, 0x00 } );
            }
            finally
            {
                manager.OnPlayerDeath = original;
            }

            Assert.Equal( "Player", Assert.Single( deaths ) );
        }

        [Fact]
        public void WillReportMobileDeathOnlyForKnownMobileOnDeathAnimation()
        {
            Mobile mobile = AddMobile( MOBILE );

            ScreenshotManager manager = ScreenshotManager.GetInstance();
            Action<Mobile> original = manager.OnMobileDeath;
            List<Mobile> deaths = new List<Mobile>();

            manager.OnMobileDeath = m => deaths.Add( m );

            try
            {
                Receive( DeathAnimation( 0x00000999 ) );
                Receive( DeathAnimation( MOBILE ) );
            }
            finally
            {
                manager.OnMobileDeath = original;
            }

            Assert.Same( mobile, Assert.Single( deaths ) );
        }

        [Fact]
        public void WillRecordPetsOnUO3DPetWindow()
        {
            Receive( Dynamic( 0x31, w =>
            {
                w.Write( PLAYER );
                w.Write( (byte) 2 );
                w.Write( 0x00000501 );
                w.Write( (byte) 0 );
                w.Write( 0x00000502 );
                w.Write( (byte) 0 );
            } ) );

            Assert.Equal( new[] { 0x00000501, 0x00000502 }, Player.Pets );
        }

        private static byte[] DeathAnimation( int serial )
        {
            return Fixed( 0xAF, w =>
            {
                w.Write( serial );
                w.Write( 0x40000999 ); // corpse
                w.Write( 0 );
            } );
        }

        private static byte[] StatPacket( byte id, int serial, int max, int current )
        {
            return Fixed( id, w =>
            {
                w.Write( serial );
                w.Write( (short) max );
                w.Write( (short) current );
            } );
        }

        private static void AssertBaseBlock( PlayerMobile player )
        {
            Assert.Equal( 100, player.Strength );
            Assert.Equal( 90, player.Dex );
            Assert.Equal( 80, player.Int );
            Assert.Equal( 85, player.Stamina );
            Assert.Equal( 90, player.StaminaMax );
            Assert.Equal( 75, player.Mana );
            Assert.Equal( 80, player.ManaMax );
            Assert.Equal( 1234567, player.Gold );
            Assert.Equal( 60, player.PhysicalResistance );
            Assert.Equal( 321, player.Weight );
        }

        /// <summary>
        ///     0x11 with every block up to <paramref name="version" />, laid out as in the packet guide.
        /// </summary>
        private static byte[] StatusPacket( int serial, string name, int hits, int hitsMax, bool renamable,
            int version, int strength )
        {
            return Dynamic( 0x11, w =>
            {
                w.Write( serial );
                w.WriteAsciiFixed( name, 30 );
                w.Write( (short) hits );
                w.Write( (short) hitsMax );
                w.Write( renamable );
                w.Write( (byte) version );

                if ( version == 0 )
                {
                    return;
                }

                w.Write( (byte) 0 ); // sex / race
                w.Write( (short) strength );
                w.Write( (short) 90 ); // dex
                w.Write( (short) 80 ); // int
                w.Write( (short) 85 ); // stamina
                w.Write( (short) 90 ); // max stamina
                w.Write( (short) 75 ); // mana
                w.Write( (short) 80 ); // max mana
                w.Write( 1234567 ); // gold
                w.Write( (short) 60 ); // physical resistance
                w.Write( (short) 321 ); // weight

                if ( version >= 5 )
                {
                    w.Write( (short) 450 ); // max weight
                    w.Write( (byte) MobileRace.Gargoyle );
                }

                if ( version >= 3 )
                {
                    w.Write( (short) 225 ); // stat cap
                    w.Write( (byte) 2 ); // followers
                    w.Write( (byte) 5 ); // max followers
                }

                if ( version >= 4 )
                {
                    w.Write( (short) 61 );
                    w.Write( (short) 62 );
                    w.Write( (short) 63 );
                    w.Write( (short) 64 );
                    w.Write( (short) 140 ); // luck
                    w.Write( (short) 11 ); // damage min
                    w.Write( (short) 17 ); // damage max
                    w.Write( 123456 ); // tithing
                }

                if ( version >= 6 )
                {
                    foreach ( short value in new short[] { 71, 72, 73, 74, 75, 20, 45, 15, 30, 100, 40, 35, 6, 2, 45 } )
                    {
                        w.Write( value );
                    }
                }
            } );
        }
    }
}
