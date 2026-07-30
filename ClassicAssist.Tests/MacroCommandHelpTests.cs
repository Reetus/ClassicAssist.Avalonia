using System.Linq;
using System.Reflection;
using ClassicAssist.Shared.Resources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class MacroCommandHelpTests
    {
        /// <summary>
        ///     The generated designer pointed at "ClassicAssist.Resources.MacroCommandHelp" while the resource
        ///     is embedded as "ClassicAssist.Shared.Resources.MacroCommandHelp", so every lookup threw
        ///     MissingManifestResourceException and no macro command had help at all.
        /// </summary>
        [TestMethod]
        public void ResourceManagerResolves()
        {
            string name = typeof( MacroCommandHelp ).Assembly.GetManifestResourceNames()
                .Single( n => n.EndsWith( "MacroCommandHelp.resources" ) );

            Assert.AreEqual( "ClassicAssist.Shared.Resources.MacroCommandHelp.resources", name );
            Assert.IsNotNull( MacroCommandHelp.ResourceManager.GetString( "MSG_COMMAND_INSERTTEXT" ) );
        }

        [DataTestMethod]
        [DataRow( "WaitForBuffEnabled" )]
        [DataRow( "WaitForBuffDisabled" )]
        [DataRow( "TradeAccept" )]
        [DataRow( "TradeClose" )]
        [DataRow( "TradeCurrency" )]
        [DataRow( "TradeReject" )]
        public void HasHelpText( string command )
        {
            string key = command.ToUpperInvariant();

            foreach ( string suffix in new[] { "_COMMAND_DESCRIPTION", "_COMMAND_INSERTTEXT" } )
            {
                Assert.IsFalse( string.IsNullOrWhiteSpace( MacroCommandHelp.ResourceManager.GetString( key + suffix ) ),
                    $"{key}{suffix} is missing from MacroCommandHelp.resx" );
            }
        }

        /// <summary>
        ///     Guards the wiring of the commands themselves rather than just their help.
        /// </summary>
        [DataTestMethod]
        [DataRow( "WaitForBuffEnabled" )]
        [DataRow( "WaitForBuffDisabled" )]
        [DataRow( "TradeAccept" )]
        [DataRow( "TradeClose" )]
        [DataRow( "TradeCurrency" )]
        [DataRow( "TradeReject" )]
        public void IsExposedAsAMacroCommand( string command )
        {
            MethodInfo method = typeof( ClassicAssist.Shared.Engine ).Assembly.GetTypes()
                .Where( t => t.Name.EndsWith( "Commands" ) )
                .SelectMany( t => t.GetMethods( BindingFlags.Public | BindingFlags.Static ) )
                .FirstOrDefault( m => m.Name == command );

            Assert.IsNotNull( method, $"{command} is not a public static *Commands method" );
            Assert.IsNotNull( method.GetCustomAttributes().FirstOrDefault( a => a.GetType().Name == "CommandsDisplayAttribute" ),
                $"{command} is missing [CommandsDisplay] so it will not appear in the UI" );
        }
    }
}
