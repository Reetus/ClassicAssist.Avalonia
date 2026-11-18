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

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ClassicAssist.Launcher.Models;
using Xunit;

namespace ClassicAssist.Tests.Launcher
{
    /// <summary>
    ///     The launcher's shard list: built-in presets, presets refreshed from the remote list
    ///     (<see cref="ShardManager.ImportPresets" />), and the user's own shards, kept in one sorted
    ///     collection. A preset and a user shard of the same name are different shards, a hidden preset stays
    ///     in the list (so it is remembered as hidden) but out of <see cref="ShardManager.VisibleShards" />, and
    ///     the grid only refreshes if VisibleShards change notifications fire - all easy to break unnoticed.
    /// </summary>
    public class ShardManagerTests
    {
        private static ShardEntry Preset( string name, string address = "preset.example", bool encryption = false )
        {
            return new ShardEntry { Name = name, Address = address, Port = 2593, IsPreset = true, Encryption = encryption };
        }

        [Fact]
        public void WillSortEncryptedFirstThenPresetsThenByName()
        {
            ShardManager manager = ShardManagerScope.Create();

            manager.Shards.AddSorted( new ShardEntry { Name = "AAA user shard" }, new ShardEntryComparer() );
            manager.Shards.AddSorted( new ShardEntry { Name = "Encrypted user shard", Encryption = true }, new ShardEntryComparer() );

            List<ShardEntry> shards = manager.Shards.ToList();

            Assert.Equal( "Official EA Servers", shards[0].Name );
            Assert.Equal( "Encrypted user shard", shards[1].Name );
            Assert.Equal( "AAA user shard", shards[^1].Name );

            List<string> presetNames = shards.Where( s => s.IsPreset && !s.Encryption ).Select( s => s.Name ).ToList();

            Assert.Equal( presetNames.OrderBy( n => n, System.StringComparer.Ordinal ).ToList(), presetNames );
            Assert.All( shards.Skip( 2 ).Take( presetNames.Count ), s => Assert.True( s.IsPreset ) );
        }

        [Fact]
        public void WillTreatAPresetAndAUserShardOfTheSameNameAsDifferent()
        {
            ShardEntry preset = Preset( "Same" );
            ShardEntry user = new ShardEntry { Name = "Same" };

            Assert.NotEqual( preset, user );
            Assert.Equal( preset, Preset( "Same", "elsewhere.example" ) );
            Assert.Equal( preset.GetHashCode(), Preset( "Same", "elsewhere.example" ).GetHashCode() );
        }

        /// <summary>
        ///     The remote list replaces the presets: presets it no longer names go, ones it still names are
        ///     updated in place (keeping the user's hidden flag), new ones are added in sorted position, and the
        ///     user's own shards are left alone.
        /// </summary>
        [Fact]
        public void WillReplacePresetsFromTheRemoteListAndKeepUserShards()
        {
            ShardManager manager = ShardManagerScope.Create();

            ShardEntry user = new ShardEntry { Name = "UO Forever" };
            manager.Shards.AddSorted( user, new ShardEntryComparer() );

            ShardEntry heritage = manager.Shards.Single( s => s.IsPreset && s.Name == "Heritage UO" );
            heritage.Deleted = true;

            manager.ImportPresets( new List<ShardEntry>
            {
                new ShardEntry
                {
                    Name = "Heritage UO", Address = "new.trueuo.example", Port = 2600, IsPreset = true,
                    Website = "https://new.example/", HasStatusProtocol = false
                },
                Preset( "A Brand New Shard" )
            } );

            Assert.True( manager.OverridePresets );

            List<ShardEntry> presets = manager.Shards.Where( s => s.IsPreset ).ToList();

            Assert.Equal( new[] { "A Brand New Shard", "Heritage UO" }, presets.Select( s => s.Name ).ToArray() );

            Assert.Same( heritage, presets[1] );
            Assert.Equal( "new.trueuo.example", heritage.Address );
            Assert.Equal( 2600, heritage.Port );
            Assert.Equal( "https://new.example/", heritage.Website );
            Assert.False( heritage.HasStatusProtocol );
            Assert.True( heritage.Deleted );

            Assert.Contains( user, manager.Shards );
            Assert.Equal( 3, manager.Shards.Count );
        }

        [Fact]
        public void WillHideDeletedShardsAndNotifyTheGrid()
        {
            ShardManager manager = ShardManagerScope.Create();
            List<string> changes = new List<string>();

            void OnChanged( object sender, PropertyChangedEventArgs args )
            {
                changes.Add( args.PropertyName );
            }

            manager.PropertyChanged += OnChanged;

            ShardEntry preset = manager.Shards.First( s => s.IsPreset );
            int total = manager.Shards.Count;

            preset.Deleted = true;

            Assert.Equal( new[] { nameof( ShardManager.VisibleShards ) }, changes );
            Assert.DoesNotContain( preset, manager.VisibleShards );
            Assert.Equal( total - 1, manager.VisibleShards.Count );

            ShardEntry added = new ShardEntry { Name = "added" };
            manager.Shards.Add( added );

            Assert.Equal( 2, changes.Count );

            // Once removed, a shard's own changes no longer reach the manager.
            manager.Shards.Remove( added );
            changes.Clear();
            added.Deleted = true;

            Assert.Empty( changes );

            // Only Deleted matters; other edits to a shard do not churn the grid.
            preset.Address = "changed.example";

            Assert.Empty( changes );
        }

        [Fact]
        public void WillInsertInSortedPosition()
        {
            List<int> list = new List<int> { 1, 3, 5 };

            list.AddSorted( 4 );
            list.AddSorted( 9 );
            list.AddSorted( 0 );

            Assert.Equal( new[] { 0, 1, 3, 4, 5, 9 }, list );
        }
    }
}
