#region License

// Copyright (C) 2021 Reetus
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
using System.Reflection;
using System.Threading;

namespace ClassicAssist.Plugin.Shared.Reflection.ClassicUO.Objects
{
    public static class Pathfinder
    {
        private static object _pathfinderInstance;
        private const string TYPE = "ClassicUO.Game.Pathfinder";
        private static Type _type;
        private static MethodInfo _walkMethod;

        /// <summary>
        ///     `ClassicUO.Game.Pathfinder` is <c>static</c> on legacy TazUO (Mono/net472) but an
        ///     instance class hung off the player (<c>PlayerMobile.Pathfinder</c>) on modern
        ///     TazUO/ClassicUO. <see cref="AutoWalking" />, <see cref="Cancel" /> and
        ///     <see cref="WalkTo" /> all need to resolve a live instance on demand for the modern
        ///     shape, rather than relying on <see cref="_pathfinderInstance" /> already being
        ///     populated by a prior <see cref="WalkTo" /> call. Without this, calling e.g.
        ///     `Pathfinding()` before `Pathfind()` reads `AutoWalking` with a null target, which
        ///     throws `TargetException` ("RFLCT.Targ_StatMethReqTarg" / "Non-static method requires
        ///     a target.").
        /// </summary>
        private static object GetPathfinderInstance()
        {
            if ( _pathfinderInstance != null )
            {
                return _pathfinderInstance;
            }

            object player = new World().Player?.AssociatedObject;

            PropertyInfo property = player?.GetType().GetProperty( "Pathfinder" );

            return _pathfinderInstance = property?.GetValue( player );
        }

        public static bool AutoWalking
        {
            get
            {
                if ( _type == null )
                {
                    _type = ReflectionImpl.DefaultAssembly?.GetType( TYPE );
                }

                PropertyInfo property = _type?.GetProperty( "AutoWalking" );

                if ( property?.GetMethod == null )
                {
                    return false;
                }

                if ( property.GetMethod.IsStatic )
                {
                    return (bool) property.GetValue( null );
                }

                object instance = GetPathfinderInstance();

                return instance != null && (bool) property.GetValue( instance );
            }
        }

        public static bool Cancel()
        {
            if ( _type == null )
            {
                _type = ReflectionImpl.DefaultAssembly?.GetType( TYPE );
            }

            PropertyInfo property = _type?.GetProperty( "AutoWalking" );

            if ( property?.SetMethod == null )
            {
                return false;
            }

            object instance = property.SetMethod.IsStatic ? null : GetPathfinderInstance();

            if ( !property.SetMethod.IsStatic && instance == null )
            {
                return false;
            }

            property.SetValue( instance, false );

            return !AutoWalking;
        }

        public static bool WalkTo( int x, int y, int z, int distance )
        {
            try
            {
                if ( _type == null )
                {
                    _type = ReflectionImpl.DefaultAssembly?.GetType( TYPE );
                }

                if ( _type == null )
                {
                    throw new Exception( "Cannot find type" );
                }

                if ( _walkMethod == null )
                {
                    _walkMethod = _type?.GetMethod( "WalkTo", BindingFlags.Public | BindingFlags.Static );
                }

                if ( _walkMethod == null )
                {
                    _walkMethod = _type?.GetMethod( "WalkTo", BindingFlags.Instance | BindingFlags.Public );
                }

                if ( _walkMethod == null )
                {
                    throw new Exception( "Cannot find method" );
                }

                object instance = null;

                if ( !_walkMethod.IsStatic )
                {
                    instance = GetPathfinderInstance();

                    if ( instance == null )
                    {
                        throw new InvalidOperationException( "Failed to get Pathfinder" );
                    }
                }

                AutoResetEvent are = new AutoResetEvent( false );

                bool retval = false;

                ReflectionImpl.TickWorkQueue.Enqueue( () =>
                {
                    retval = (bool) _walkMethod.Invoke( instance, new object[] { x, y, z, distance } );
                    are.Set();
                } );

                // A timeout leaves retval false, which is the right answer: the client never drained
                // the tick queue, so the walk was never handed over at all.
                are.WaitOne( 5000 );

                return retval;
            }
            catch ( Exception e )
            {
                // Surfaced here rather than swallowed: a caller that gets `true` back has no way to
                // tell a real walk apart from a reflection failure that never touched the client at
                // all, which is indistinguishable from Pathfinding() reading false immediately after.
                try
                {
                    Console.WriteLine( $"ClassicAssist: Pathfinder.WalkTo failed: {e}" );
                    Console.Out.Flush();
                }
                catch ( Exception )
                {
                    // A plugin must never take the client down over a log line.
                }

                return false;
            }
        }
    }
}