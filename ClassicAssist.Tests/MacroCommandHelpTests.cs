using System.Linq;
using System.Reflection;
using ClassicAssist.Shared.Resources;
using Xunit;

namespace ClassicAssist.Tests
{
    public class MacroCommandHelpTests
    {
        /// <summary>
        ///     The generated designer pointed at "ClassicAssist.Resources.MacroCommandHelp" while the resource
        ///     is embedded as "ClassicAssist.Shared.Resources.MacroCommandHelp", so every lookup threw
        ///     MissingManifestResourceException and no macro command had help at all.
        /// </summary>
        [Fact]
        public void ResourceManagerResolves()
        {
            string name = typeof( MacroCommandHelp ).Assembly.GetManifestResourceNames()
                .Single( n => n.EndsWith( "MacroCommandHelp.resources" ) );

            Assert.Equal( "ClassicAssist.Shared.Resources.MacroCommandHelp.resources", name );
            Assert.NotNull( MacroCommandHelp.ResourceManager.GetString( "MSG_COMMAND_INSERTTEXT" ) );
        }

        [Theory]
        [InlineData( "WaitForBuffEnabled" )]
        [InlineData( "WaitForBuffDisabled" )]
        [InlineData( "TradeAccept" )]
        [InlineData( "TradeClose" )]
        [InlineData( "TradeCurrency" )]
        [InlineData( "TradeReject" )]
        [InlineData( "ActiveAbility" )]
        [InlineData( "DiffHitsPercent" )]
        [InlineData( "FasterCastRecovery" )]
        [InlineData( "FasterCasting" )]
        [InlineData( "SwingSpeedIncrease" )]
        [InlineData( "Direction" )]
        [InlineData( "Map" )]
        [InlineData( "AutoColorPick" )]
        [InlineData( "ClearObjectQueue" )]
        [InlineData( "DropItemToGround" )]
        [InlineData( "MoveItemXYZ" )]
        [InlineData( "MoveTypeXYZ" )]
        [InlineData( "IsRunning" )]
        [InlineData( "PlaySound" )]
        [InlineData( "StopOrganizer" )]
        [InlineData( "SkillDelta" )]
        [InlineData( "UseLastSkill" )]
        [InlineData( "OpenECV" )]
        [InlineData( "PopList" )]
        [InlineData( "Autoloot" )]
        [InlineData( "Autolooting" )]
        [InlineData( "SetAutoloot" )]
        [InlineData( "SetOrganizerContainers" )]
        [InlineData( "SetScavenger" )]
        [InlineData( "StopDress" )]
        [InlineData( "WaitForTradeWindow" )]
        [InlineData( "WaitForRemoveObject" )]
        public void HasHelpText( string command )
        {
            string key = command.ToUpperInvariant();

            foreach ( string suffix in new[] { "_COMMAND_DESCRIPTION", "_COMMAND_INSERTTEXT" } )
            {
                Assert.False( string.IsNullOrWhiteSpace( MacroCommandHelp.ResourceManager.GetString( key + suffix ) ),
                    $"{key}{suffix} is missing from MacroCommandHelp.resx" );
            }
        }

        /// <summary>
        ///     Guards the wiring of the commands themselves rather than just their help.
        /// </summary>
        [Theory]
        [InlineData( "WaitForBuffEnabled" )]
        [InlineData( "WaitForBuffDisabled" )]
        [InlineData( "TradeAccept" )]
        [InlineData( "TradeClose" )]
        [InlineData( "TradeCurrency" )]
        [InlineData( "TradeReject" )]
        [InlineData( "ActiveAbility" )]
        [InlineData( "DiffHitsPercent" )]
        [InlineData( "FasterCastRecovery" )]
        [InlineData( "FasterCasting" )]
        [InlineData( "SwingSpeedIncrease" )]
        [InlineData( "Direction" )]
        [InlineData( "Map" )]
        [InlineData( "AutoColorPick" )]
        [InlineData( "ClearObjectQueue" )]
        [InlineData( "DropItemToGround" )]
        [InlineData( "MoveItemXYZ" )]
        [InlineData( "MoveTypeXYZ" )]
        [InlineData( "IsRunning" )]
        [InlineData( "PlaySound" )]
        [InlineData( "StopOrganizer" )]
        [InlineData( "SkillDelta" )]
        [InlineData( "UseLastSkill" )]
        [InlineData( "OpenECV" )]
        [InlineData( "PopList" )]
        [InlineData( "Autoloot" )]
        [InlineData( "Autolooting" )]
        [InlineData( "SetAutoloot" )]
        [InlineData( "SetOrganizerContainers" )]
        [InlineData( "SetScavenger" )]
        [InlineData( "StopDress" )]
        [InlineData( "WaitForTradeWindow" )]
        [InlineData( "WaitForRemoveObject" )]
        public void IsExposedAsAMacroCommand( string command )
        {
            MethodInfo method = typeof( ClassicAssist.Shared.Engine ).Assembly.GetTypes()
                .Where( t => t.Name.EndsWith( "Commands" ) )
                .SelectMany( t => t.GetMethods( BindingFlags.Public | BindingFlags.Static ) )
                .FirstOrDefault( m => m.Name == command );

            Assert.NotNull( method ); // $"{command} is not a public static *Commands method"
            Assert.NotNull( method.GetCustomAttributes().FirstOrDefault( a => a.GetType().Name == "CommandsDisplayAttribute" ) ); // $"{command} is missing [CommandsDisplay] so it will not appear in the UI"
        }
    }
}
