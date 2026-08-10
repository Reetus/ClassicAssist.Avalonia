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
using System.Linq;
using System.Reflection;
using System.Threading;
using ClassicAssist.Data;

namespace ClassicAssist.Misc;

/// <summary>
///     Loads the additional assemblies listed in <see cref="AssistantOptions.Assemblies" /> and invokes
///     the static Initialize hooks they opt into. Two exist: a parameterless one for general plugin
///     setup, called once when options load, and one taking the constraint collection, called every
///     time a view builds its property list.
///     <para>
///         Each path is loaded exactly once and cached. Base ClassicAssist loads the same file twice -
///         Assembly.LoadFile for the parameterless hook, Assembly.LoadFrom for the constraint hook -
///         which .NET Framework largely papered over. Here LoadFile would give the file its own load
///         context, so the two copies would carry distinct Type identities: a plugin whose
///         Initialize() stashed static state would find it missing from the copy that registers
///         constraints. Loading once via LoadFrom puts plugin types in the default context, where they
///         unify with ours.
///     </para>
/// </summary>
public static class PluginAssemblies
{
    private static readonly Dictionary<string, Assembly> _loaded =
        new( StringComparer.OrdinalIgnoreCase );

    private static readonly Lock _lock = new();

    /// <summary>
    ///     Every configured assembly that could be loaded. One that throws is skipped rather than
    ///     aborting the rest, matching base ClassicAssist - a plugin built against a different version
    ///     shouldn't take the constraint list down with it.
    /// </summary>
    public static Assembly[] GetAssemblies()
    {
        string[] fileNames = AssistantOptions.Assemblies;

        if ( fileNames == null )
        {
            return [];
        }

        List<Assembly> assemblies = new( fileNames.Length );

        lock ( _lock )
        {
            foreach ( string fileName in fileNames )
            {
                if ( string.IsNullOrEmpty( fileName ) )
                {
                    continue;
                }

                if ( _loaded.TryGetValue( fileName, out Assembly cached ) )
                {
                    if ( cached != null )
                    {
                        assemblies.Add( cached );
                    }

                    continue;
                }

                Assembly assembly = null;

                try
                {
                    assembly = Assembly.LoadFrom( fileName );
                }
                catch ( Exception )
                {
                    // ignored
                }

                // Cached either way - a null entry stops a broken path being retried on every
                // constraint rebuild, which happens whenever the ECV or Autoloot tab reloads.
                _loaded[fileName] = assembly;

                if ( assembly != null )
                {
                    assemblies.Add( assembly );
                }
            }
        }

        return [.. assemblies];
    }

    /// <summary>
    ///     Invokes every public static Initialize method matching <paramref name="parameterTypes" /> on
    ///     every public class in every loaded assembly.
    /// </summary>
    /// <param name="parameterTypes">Signature to match, <see cref="Type.EmptyTypes" /> for no parameters.</param>
    /// <param name="arguments">Arguments to pass, null when there are none.</param>
    public static void InvokeInitialize( Type[] parameterTypes, object[] arguments )
    {
        foreach ( Assembly assembly in GetAssemblies() )
        {
            try
            {
                IEnumerable<MethodInfo> initializeMethods = assembly.GetTypes()
                    .Where( e => e.IsClass && e.IsPublic && GetInitializeMethod( e, parameterTypes ) != null )
                    .Select( e => GetInitializeMethod( e, parameterTypes ) );

                foreach ( MethodInfo initializeMethod in initializeMethods )
                {
                    initializeMethod?.Invoke( null, arguments );
                }
            }
            catch ( Exception )
            {
                // ignored
            }
        }
    }

    private static MethodInfo GetInitializeMethod( Type type, Type[] parameterTypes )
    {
        return type.GetMethod( "Initialize", BindingFlags.Public | BindingFlags.Static, null, parameterTypes,
            null );
    }
}
