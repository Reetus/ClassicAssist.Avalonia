#region License

// Copyright (C) 2026 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ClassicAssist.Launcher.Models;
using ClassicAssist.Launcher.ViewModels;
using ClassicAssist.Tests.UOData;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.Launcher
{
    /// <summary>
    ///     Launcher.json, read by the <see cref="MainViewModel" /> constructor and written on close. It is the
    ///     only place the user's clients, data folders, own shards, hidden presets, extra plugins and client
    ///     options live, so a field that fails to round trip is simply lost on the next start. Every config
    ///     sets ShardsDateTime to now: an older (or missing) stamp makes the constructor fetch the remote shard
    ///     list over the network.
    ///     <para>
    ///         Also the client options, which <c>BuildClassicOptions</c> turns into command line switches. The
    ///         rest of the command line is assembled inline in <c>Start</c> next to DNS resolution and
    ///         Process.Start, so it cannot be reached without launching a process; a
    ///         <c>BuildArguments(ip, plugins)</c> seam would make it testable.
    ///     </para>
    /// </summary>
    public class LauncherConfigTests : IDisposable
    {
        private readonly LauncherConfigScope _config = new LauncherConfigScope();
        private readonly TempDirectory _directory = new TempDirectory();
        private ShardManagerScope _shards = new ShardManagerScope();

        public void Dispose()
        {
            _shards.Dispose();
            _config.Dispose();
            _directory.Dispose();
        }

        private string Now => JsonDate( DateTime.Now );

        private static string JsonDate( DateTime value )
        {
            return value.ToString( "o" );
        }

        private static string Escape( string path )
        {
            return path.Replace( "\\", "\\\\" );
        }

        /// <summary>
        ///     Starts the next view model against a fresh shard list, as a new launcher process would.
        /// </summary>
        private MainViewModel Restart()
        {
            _shards.Dispose();
            _shards = new ShardManagerScope();

            return new MainViewModel();
        }

        [Fact]
        public void WillStartEmptyWithNoConfig()
        {
            MainViewModel vm = new MainViewModel();

            Assert.Empty( vm.ClientPaths );
            Assert.Null( vm.SelectedShard );
            Assert.Null( vm.ShardsDateTime );
            Assert.Equal( 60000, vm.ClassicOptions.ReconnectTime );
            Assert.False( vm.StartCommand.CanExecute( null ) );
        }

        /// <summary>
        ///     Paths that no longer exist are dropped rather than offered, and a selection that no longer exists
        ///     falls back to the first one that does.
        /// </summary>
        [Fact]
        public void WillDropPathsThatNoLongerExist()
        {
            string client = _directory.Write( "client.exe", new byte[1] );
            string missingClient = _directory.File( "gone.exe" );
            string dataPath = Directory.CreateDirectory( _directory.File( "uo" ) ).FullName;

            _config.Write( $$"""
                {
                  "ClientPaths": [ "{{Escape( missingClient )}}", "{{Escape( client )}}" ],
                  "SelectedClientPath": "{{Escape( missingClient )}}",
                  "DataPaths": [ "{{Escape( _directory.File( "nope" ) )}}", "{{Escape( dataPath )}}" ],
                  "SelectedDataPath": "{{Escape( dataPath )}}",
                  "ShardsDateTime": "{{Now}}"
                }
                """ );

            MainViewModel vm = new MainViewModel();

            Assert.Equal( new[] { client }, vm.ClientPaths.ToArray() );
            Assert.Equal( client, vm.SelectedClientPath );
            Assert.Equal( new[] { dataPath }, vm.DataPaths.ToArray() );
            Assert.Equal( dataPath, vm.SelectedDataPath );
        }

        [Fact]
        public void WillLoadUserShardsHiddenPresetsAndTheSelection()
        {
            _config.Write( $$"""
                {
                  "Shards": [ { "Name": "My Shard", "Address": "10.0.0.5", "Port": 2600, "Encryption": true },
                              { } ],
                  "DeletedPresets": [ { "Name": "UO Forever" }, { "Name": "Not a preset" } ],
                  "SelectedShard": "My Shard",
                  "Plugins": [ "C:/plugins/Other.dll" ],
                  "ShardsDateTime": "{{Now}}"
                }
                """ );

            MainViewModel vm = new MainViewModel();

            ShardEntry mine = vm.ShardManager.Shards.Single( s => s.Name == "My Shard" );

            Assert.False( mine.IsPreset );
            Assert.Equal( "10.0.0.5", mine.Address );
            Assert.Equal( 2600, mine.Port );
            Assert.True( mine.Encryption );
            Assert.Same( mine, vm.SelectedShard );

            // A shard entry with nothing in it gets usable defaults.
            ShardEntry blank = vm.ShardManager.Shards.Single( s => s.Name == "Unknown" );
            Assert.Equal( "localhost", blank.Address );
            Assert.Equal( 2593, blank.Port );

            Assert.True( vm.ShardManager.Shards.Single( s => s.IsPreset && s.Name == "UO Forever" ).Deleted );
            Assert.DoesNotContain( vm.ShardManager.VisibleShards, s => s.Name == "UO Forever" );

            PluginEntry plugin = Assert.Single( vm.Plugins );
            Assert.Equal( "Other.dll", plugin.Name );
        }

        /// <summary>
        ///     Once the remote list has been imported, the saved presets replace the built-in ones entirely.
        /// </summary>
        [Fact]
        public void WillReplaceBuiltInPresetsWithSavedOnes()
        {
            _config.Write( $$"""
                {
                  "OverridePresets": true,
                  "Presets": [ { "Name": "Remote Shard", "Address": "remote.example", "Port": 2599, "Website": "https://remote.example/" } ],
                  "ShardsHash": "abc123",
                  "ShardsDateTime": "{{Now}}"
                }
                """ );

            MainViewModel vm = new MainViewModel();

            ShardEntry preset = Assert.Single( vm.ShardManager.Shards );
            Assert.True( preset.IsPreset );
            Assert.Equal( "Remote Shard", preset.Name );
            Assert.Equal( 2599, preset.Port );
            Assert.Equal( "abc123", vm.ShardsHash );
            Assert.True( vm.ShardManager.OverridePresets );
        }

        /// <summary>
        ///     Everything written on close comes back on the next start, and the write goes through a temp file
        ///     that is renamed into place.
        /// </summary>
        [Fact]
        public void WillRoundTripTheConfigThroughClose()
        {
            string client = _directory.Write( "client.exe", new byte[1] );
            string dataPath = Directory.CreateDirectory( _directory.File( "uo" ) ).FullName;

            MainViewModel vm = new MainViewModel();

            vm.ClientPaths.Add( client );
            vm.SelectedClientPath = client;
            vm.DataPaths.Add( dataPath );
            vm.SelectedDataPath = dataPath;
            vm.ShardsDateTime = DateTime.Now;
            vm.ShardsHash = "hash";
            vm.Plugins.Add( new PluginEntry { Name = "Other.dll", FullPath = "C:/plugins/Other.dll" } );

            ShardEntry mine = new ShardEntry
            {
                Name = "My Shard", Address = "10.0.0.5", Port = 2600, Encryption = true, HasStatusProtocol = false
            };

            vm.ShardManager.Shards.AddSorted( mine, new ShardEntryComparer() );
            vm.ShardManager.Shards.Single( s => s.Name == "Heritage UO" ).Deleted = true;
            vm.SelectedShard = mine;

            vm.ClassicOptions = new ClassicOptions
            {
                Autologin = true,
                AutoReconnect = true,
                ReconnectTime = 15,
                Debug = true,
                OverrideClientVersion = true,
                ClientVersion = "7.0.15.1",
                ForceManagedPlugin = true
            };

            Assert.True( vm.StartCommand.CanExecute( null ) );

            vm.ClosingCommand.Execute( null );

            Assert.True( File.Exists( _config.ConfigPath ) );
            Assert.False( File.Exists( _config.ConfigPath + ".tmp" ) );

            MainViewModel reloaded = Restart();

            Assert.Equal( new[] { client }, reloaded.ClientPaths.ToArray() );
            Assert.Equal( client, reloaded.SelectedClientPath );
            Assert.Equal( dataPath, reloaded.SelectedDataPath );
            Assert.Equal( "hash", reloaded.ShardsHash );
            Assert.Equal( "C:/plugins/Other.dll", Assert.Single( reloaded.Plugins ).FullPath );

            ShardEntry loaded = reloaded.ShardManager.Shards.Single( s => !s.IsPreset );
            Assert.Equal( "My Shard", loaded.Name );
            Assert.Equal( "10.0.0.5", loaded.Address );
            Assert.Equal( 2600, loaded.Port );
            Assert.True( loaded.Encryption );
            Assert.False( loaded.HasStatusProtocol );
            Assert.Same( loaded, reloaded.SelectedShard );

            Assert.True( reloaded.ShardManager.Shards.Single( s => s.Name == "Heritage UO" ).Deleted );

            ClassicOptions options = reloaded.ClassicOptions;
            Assert.True( options.Autologin );
            Assert.True( options.AutoReconnect );
            Assert.Equal( 15, options.ReconnectTime );
            Assert.True( options.Debug );
            Assert.True( options.OverrideClientVersion );
            Assert.Equal( "7.0.15.1", options.ClientVersion );
            Assert.True( options.ForceManagedPlugin );
            Assert.False( options.SkipLoginScreen );
        }

        /// <summary>
        ///     Presets are only written once they override the built-ins; otherwise the built-in list (which
        ///     ships with each release) stays authoritative.
        /// </summary>
        [Fact]
        public void WillOnlySavePresetsWhenTheyOverrideTheBuiltIns()
        {
            MainViewModel vm = new MainViewModel();
            vm.ClosingCommand.Execute( null );

            Assert.Null( JObject.Parse( File.ReadAllText( _config.ConfigPath ) )["Presets"] );

            vm.ShardManager.ImportPresets( new List<ShardEntry>
            {
                new ShardEntry { Name = "Only Preset", Address = "only.example", Port = 1234, IsPreset = true }
            } );
            vm.ShardsDateTime = DateTime.Now;
            vm.ClosingCommand.Execute( null );

            JObject saved = JObject.Parse( File.ReadAllText( _config.ConfigPath ) );
            Assert.Equal( "Only Preset", Assert.Single( (JArray) saved["Presets"] )["Name"] );

            MainViewModel reloaded = Restart();

            Assert.Equal( "Only Preset", Assert.Single( reloaded.ShardManager.Shards ).Name );
        }

        /// <summary>
        ///     The switches each client option produces. Booleans marked IncludeIfFalse are always passed (the
        ///     client needs an explicit false), the rest only when on; a value gated on another option is only
        ///     passed when that option is on; and launcher-only options are never passed.
        /// </summary>
        [Fact]
        public void WillBuildClientSwitchesFromTheOptions()
        {
            MainViewModel vm = new MainViewModel { ClassicOptions = new ClassicOptions { ClientVersion = "7.0.15.1" } };

            Assert.Equal( new[] { "-autologin", "False", "-reconnect", "False" }, Switches( vm ) );

            vm.ClassicOptions = new ClassicOptions
            {
                Autologin = true,
                AutoReconnect = true,
                ReconnectTime = 30,
                Debug = true,
                SkipLoginScreen = true,
                OverrideClientVersion = true,
                ClientVersion = "7.0.15.1",
                ForceManagedPlugin = true
            };

            List<string> args = Switches( vm );

            Assert.Equal( 12, args.Count );
            AssertSwitch( args, "-autologin", "True" );
            AssertSwitch( args, "-reconnect", "True" );
            AssertSwitch( args, "-reconnect_time", "30" );
            AssertSwitch( args, "-clientversion", "7.0.15.1" );
            AssertSwitch( args, "-debug", "True" );
            AssertSwitch( args, "-skiploginscreen", "True" );

            vm.ClassicOptions = new ClassicOptions { OverrideClientVersion = true };

            AssertSwitch( Switches( vm ), "-clientversion", string.Empty );
        }

        private static void AssertSwitch( List<string> args, string name, string value )
        {
            int index = args.IndexOf( name );

            Assert.True( index >= 0, $"{name} missing from {string.Join( " ", args )}" );
            Assert.Equal( value, args[index + 1] );
        }

        private static List<string> Switches( MainViewModel vm )
        {
            List<string> args = new List<string>();

            typeof( MainViewModel ).GetMethod( "BuildClassicOptions", BindingFlags.Instance | BindingFlags.NonPublic )!
                .Invoke( vm, new object[] { args } );

            return args;
        }
    }
}
