using System;
using System.Collections.Generic;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UO.Objects;
using Xunit;
using static ClassicAssist.Tests.MacroCommands.MacroCommandTestSupport;

namespace ClassicAssist.Tests.MacroCommands
{
    /// <summary>
    ///     Every serial-taking command goes through <see cref="AliasCommands.ResolveSerial" />, so its
    ///     conventions decide what a macro hits: names are case-insensitive, an unknown alias is -1 (not
    ///     0), null means the player and an unsupported type is rejected. Player aliases are a separate
    ///     namespace keyed by character, so one profile shared between characters keeps them apart.
    /// </summary>
    public class AliasResolutionTests : IDisposable
    {
        private readonly PlayerMobile _player;

        public AliasResolutionTests()
        {
            _player = EngineState.ResetWithPlayer( 0x00000011 );
        }

        public void Dispose()
        {
            EngineState.Reset();
        }

        [Fact]
        public void ResolveSerialWillHandleEachSupportedArgumentType()
        {
            AliasCommands.SetAlias( "MyBag", 0x40000010 );

            Assert.Equal( 0x40000010, AliasCommands.ResolveSerial( "mybag" ) );
            Assert.Equal( 0x40000010, AliasCommands.ResolveSerial( "MYBAG" ) );
            Assert.Equal( 0x40000020, AliasCommands.ResolveSerial( 0x40000020 ) );
            Assert.Equal( unchecked( (int) 0xC0000000 ), AliasCommands.ResolveSerial( 0xC0000000u ) );
            Assert.Equal( 0x40000030, AliasCommands.ResolveSerial( new Item( 0x40000030 ) ) );
            Assert.Equal( 0x00000011, AliasCommands.ResolveSerial( null ) );
        }

        [Fact]
        public void ResolveSerialWillReportUnknownAliasesAndUnsupportedTypes()
        {
            using PacketRecorder recorder = new PacketRecorder();

            Assert.Equal( -1, AliasCommands.ResolveSerial( "nosuchalias" ) );
            Assert.Equal( -1, AliasCommands.ResolveSerial( 1.5 ) );

            IReadOnlyList<string> messages = SystemMessages( recorder );
            Assert.Contains( string.Format( Strings.Unknown_alias___0___, "nosuchalias" ), messages );
            Assert.Contains( Strings.Invalid_or_unknown_object_id, messages );
        }

        [Fact]
        public void ResolveSerialWillBeZeroForNullWithoutAPlayer()
        {
            Engine.Player = null;

            Assert.Equal( 0, AliasCommands.ResolveSerial( null ) );
        }

        [Fact]
        public void SetAliasFromAnotherAliasWillCopyItsValue()
        {
            AliasCommands.SetAlias( "first", 0x40000010 );
            AliasCommands.SetAlias( "second", "first" );
            AliasCommands.SetAlias( "first", 0x40000099 );

            Assert.Equal( 0x40000010, AliasCommands.GetAlias( "second" ) );

            AliasCommands.UnsetAlias( "SECOND" );

            Assert.Equal( -1, AliasCommands.GetAlias( "second" ) );
        }

        [Fact]
        public void FindAliasWillCheckTheEntityStillExists()
        {
            AliasCommands.SetAlias( "pet", 0x00000022 );
            AliasCommands.SetAlias( "bag", 0x40000010 );

            Assert.False( AliasCommands.FindAlias( "pet" ) );
            Assert.False( AliasCommands.FindAlias( "bag" ) );
            Assert.False( AliasCommands.FindAlias( "nosuchalias" ) );

            Engine.Mobiles.Add( new Mobile( 0x00000022 ) );
            Engine.Items.Add( new Item( 0x40000010 ) );

            Assert.True( AliasCommands.FindAlias( "PET" ) );
            Assert.True( AliasCommands.FindAlias( "bag" ) );
        }

        [Fact]
        public void SetDefaultAliasesWillPointAtThePlayersContainers()
        {
            _player.SetLayer( ClassicAssist.UO.Data.Layer.Bank, 0x40000077 );

            AliasCommands.SetDefaultAliases();

            Assert.Equal( 0x00000011, AliasCommands.GetAlias( "self" ) );
            Assert.Equal( _player.Backpack.Serial, AliasCommands.GetAlias( "backpack" ) );
            Assert.Equal( 0x40000077, AliasCommands.GetAlias( "bank" ) );
        }

        [Fact]
        public void PlayerAliasesWillBeKeptPerCharacter()
        {
            AliasCommands.SetPlayerAlias( "DropChest", 0x40000010 );

            Assert.Equal( 0x40000010, AliasCommands.GetPlayerAlias( "dropchest" ) );

            // Player aliases do not leak into the shared namespace.
            Assert.Equal( -1, AliasCommands.GetAlias( "dropchest" ) );

            PlayerMobile other = new PlayerMobile( 0x00000012 );
            Engine.Player = other;

            Assert.Equal( -1, AliasCommands.GetPlayerAlias( "dropchest" ) );
            Assert.Empty( AliasCommands.GetPlayerAliases() );

            AliasCommands.SetPlayerAlias( "dropchest", 0x40000020 );

            Dictionary<int, Dictionary<string, int>> all = AliasCommands.GetAllPlayerAliases();
            Assert.Equal( 0x40000010, all[0x00000011]["dropchest"] );
            Assert.Equal( 0x40000020, all[0x00000012]["dropchest"] );

            AliasCommands.UnsetPlayerAlias( "DROPCHEST" );

            Assert.Equal( -1, AliasCommands.GetPlayerAlias( "dropchest" ) );

            Engine.Player = _player;

            Assert.Equal( 0x40000010, AliasCommands.GetPlayerAliases()["dropchest"] );
        }

        [Fact]
        public void PlayerAliasCommandsWillDoNothingWithoutAPlayer()
        {
            Engine.Player = null;

            AliasCommands.SetPlayerAlias( "x", 1 );
            AliasCommands.UnsetPlayerAlias( "x" );

            Assert.Equal( -1, AliasCommands.GetPlayerAlias( "x" ) );
            Assert.Empty( AliasCommands.GetPlayerAliases() );
            Assert.Empty( AliasCommands.GetAllPlayerAliases() );
        }

        [Fact]
        public void GetAllAliasesWillReturnACopy()
        {
            AliasCommands.SetAlias( "a", 1 );

            Dictionary<string, int> copy = AliasCommands.GetAllAliases();
            AliasCommands.SetAlias( "b", 2 );

            Assert.Single( copy );
        }
    }
}
