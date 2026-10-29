using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using ClassicAssist.Data;
using ClassicAssist.Mcp;
using ClassicAssist.Shared;
using ClassicAssist.UO.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Tests.Mcp
{
    [TestClass]
    public class McpAutoStartTests
    {
        private const int SERIAL = 0x0000ABCD;
        private const string SERIAL_KEY = "0x0000ABCD";
        private const string OTHER_KEY = "0x00001234";

        private string _originalStartupPath;
        private string _originalProfileDirectory;
        private Options _originalCurrentOptions;
        private PlayerMobile _originalPlayer;
        private string _tempDir;

        [TestInitialize]
        public void SetUp()
        {
            _originalStartupPath = Engine.StartupPath;
            _originalProfileDirectory = AssistantOptions.ProfileDirectory;
            _originalCurrentOptions = Options.CurrentOptions;
            _originalPlayer = Engine.Player;

            _tempDir = Path.Combine( Path.GetTempPath(), "ClassicAssistMcpAutoStart", Guid.NewGuid().ToString( "N" ) );
            Engine.StartupPath = _tempDir;
            AssistantOptions.ProfileDirectory = Path.Combine( _tempDir, "Profiles" );
            Directory.CreateDirectory( AssistantOptions.ProfileDirectory );

            // Named up front: a profile loaded before its file exists would otherwise save as settings.json.
            Options.CurrentOptions = new Options { Name = "test.json" };
            Engine.Player = new PlayerMobile( SERIAL );

            McpAutoStart.Initialize();
        }

        [TestCleanup]
        public void TearDown()
        {
            McpServer.Shutdown();

            Engine.Player = _originalPlayer;
            Options.CurrentOptions = _originalCurrentOptions;
            AssistantOptions.ProfileDirectory = _originalProfileDirectory;
            Engine.StartupPath = _originalStartupPath;

            try
            {
                Directory.Delete( _tempDir, true );
            }
            catch ( IOException )
            {
                // best effort
            }
            catch ( UnauthorizedAccessException )
            {
                // best effort
            }
        }

        [TestMethod]
        public void LoadRestartsOnTheSavedPort()
        {
            int port = FreePort();
            WriteProfile( new JObject { [SERIAL_KEY] = port } );

            Options.Load( "test.json", Options.CurrentOptions );

            Assert.IsTrue( McpServer.IsRunning );
            Assert.AreEqual( port, McpServer.Port );
            Assert.IsTrue( Options.CurrentOptions.McpEnabled );
            Assert.AreEqual( port, Options.CurrentOptions.McpPort );
        }

        [TestMethod]
        public void LoadTakesTheNextFreePortWhenTheSavedOneIsTaken()
        {
            using TcpListener taken = new( IPAddress.Loopback, 0 );
            taken.Start();
            int port = ( (IPEndPoint) taken.LocalEndpoint ).Port;

            WriteProfile( new JObject { [SERIAL_KEY] = port } );

            Options.Load( "test.json", Options.CurrentOptions );

            Assert.IsTrue( McpServer.IsRunning );
            Assert.IsTrue( McpServer.Port > port, $"Expected a port above {port}, got {McpServer.Port}." );
            Assert.AreEqual( McpServer.Port, Options.CurrentOptions.McpPort );
        }

        [TestMethod]
        public void LoadDoesNothingForACharacterWithoutAnEntry()
        {
            WriteProfile( new JObject { [OTHER_KEY] = FreePort() } );

            Options.Load( "test.json", Options.CurrentOptions );

            Assert.IsFalse( McpServer.IsRunning );
            Assert.IsFalse( Options.CurrentOptions.McpEnabled );
        }

        [TestMethod]
        public void SaveRecordsTheRunningPortAndKeepsOtherCharacters()
        {
            WriteProfile( new JObject { [OTHER_KEY] = 5000 } );
            Options.Load( "test.json", Options.CurrentOptions );

            int port = FreePort();
            McpServer.Initialize( port );

            Options.Save( Options.CurrentOptions );

            JObject ports = ReadPorts();

            Assert.AreEqual( port, ports[SERIAL_KEY]?.ToObject<int>() );
            Assert.AreEqual( 5000, ports[OTHER_KEY]?.ToObject<int>() );
        }

        [TestMethod]
        public void SaveKeepsAnEntryWrittenByAnotherInstanceSinceLoad()
        {
            Options.Load( "test.json", Options.CurrentOptions );

            // Another instance sharing the profile saves its character in the meantime.
            WriteProfile( new JObject { [OTHER_KEY] = 5001 } );

            McpServer.Initialize( FreePort() );
            Options.Save( Options.CurrentOptions );

            Assert.AreEqual( 5001, ReadPorts()[OTHER_KEY]?.ToObject<int>() );
        }

        [TestMethod]
        public void SaveDropsTheCharacterWhenMcpIsOff()
        {
            int port = FreePort();
            WriteProfile( new JObject { [SERIAL_KEY] = port, [OTHER_KEY] = 5000 } );
            Options.Load( "test.json", Options.CurrentOptions );

            McpServer.Shutdown();
            Options.Save( Options.CurrentOptions );

            JObject ports = ReadPorts();

            Assert.IsNull( ports[SERIAL_KEY] );
            Assert.AreEqual( 5000, ports[OTHER_KEY]?.ToObject<int>() );
        }

        private void WriteProfile( JObject ports )
        {
            JObject profile = new() { ["Name"] = "test.json", ["McpAutoStart"] = new JObject { ["Ports"] = ports } };

            File.WriteAllText( Path.Combine( AssistantOptions.ProfileDirectory, "test.json" ), profile.ToString() );
        }

        private JObject ReadPorts()
        {
            JObject profile =
                JObject.Parse( File.ReadAllText( Path.Combine( AssistantOptions.ProfileDirectory, "test.json" ) ) );

            return profile["McpAutoStart"]?["Ports"] as JObject ?? [];
        }

        private static int FreePort()
        {
            using TcpListener listener = new( IPAddress.Loopback, 0 );

            listener.Start();

            return ( (IPEndPoint) listener.LocalEndpoint ).Port;
        }
    }
}
