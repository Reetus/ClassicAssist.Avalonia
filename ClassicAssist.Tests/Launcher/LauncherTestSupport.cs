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
using System.IO;
using System.Reflection;
using ClassicAssist.Launcher.Models;

namespace ClassicAssist.Tests.Launcher
{
    /// <summary>
    ///     Swaps the <see cref="ShardManager" /> singleton for a fresh instance (built through its private
    ///     constructor, so with the built-in presets) and puts the original back on dispose.
    /// </summary>
    internal sealed class ShardManagerScope : IDisposable
    {
        private static readonly FieldInfo _instanceField =
            typeof( ShardManager ).GetField( "_instance", BindingFlags.Static | BindingFlags.NonPublic ) ??
            throw new InvalidOperationException( "ShardManager._instance not found" );

        private readonly object _original;

        public ShardManagerScope( bool clear = false )
        {
            _original = _instanceField.GetValue( null );
            Manager = Create();

            if ( clear )
            {
                Manager.Shards.Clear();
            }

            _instanceField.SetValue( null, Manager );
        }

        public ShardManager Manager { get; }

        public static ShardManager Create()
        {
            return (ShardManager) Activator.CreateInstance( typeof( ShardManager ), true );
        }

        public void Dispose()
        {
            _instanceField.SetValue( null, _original );
        }
    }

    /// <summary>
    ///     The launcher reads and writes Launcher.json beside its own binaries (AppContext.BaseDirectory, here
    ///     the test output folder) with no way to redirect it. This moves any existing file aside for the
    ///     test and puts it back afterwards.
    /// </summary>
    internal sealed class LauncherConfigScope : IDisposable
    {
        private readonly string _backup;

        public LauncherConfigScope()
        {
            ConfigPath = Path.Combine( AppContext.BaseDirectory, "Launcher.json" );
            _backup = ConfigPath + "." + Guid.NewGuid().ToString( "N" ) + ".bak";

            if ( File.Exists( ConfigPath ) )
            {
                File.Move( ConfigPath, _backup );
            }
        }

        public string ConfigPath { get; }

        public void Write( string json )
        {
            File.WriteAllText( ConfigPath, json );
        }

        public void Dispose()
        {
            File.Delete( ConfigPath );
            File.Delete( ConfigPath + ".tmp" );

            if ( File.Exists( _backup ) )
            {
                File.Move( _backup, ConfigPath );
            }
        }
    }
}
