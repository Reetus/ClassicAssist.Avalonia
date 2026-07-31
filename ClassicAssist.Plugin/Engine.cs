#region License

// Copyright (C) 2025 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY

#endregion

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if !NETFRAMEWORK
using System.Runtime.Loader;
#endif

// ReSharper disable once CheckNamespace
namespace Assistant
{
    /// <summary>
    ///     The type ClassicUO/TazUO looks for by name. Its only job is to make the rest of the plugin
    ///     loadable and then hand off.
    ///     <para>
    ///         The client loads us with <c>Assembly.LoadFile</c>, which does not add our directory to the
    ///         probing path, so none of the assemblies sitting next to us resolve by default. Worse, the
    ///         runtime resolves the types in a method's *signature* before it runs the method body - so an
    ///         entry point declared as <c>Install(PluginHeader* plugin)</c> throws FileNotFoundException
    ///         before it can install a resolver for the very assembly that declares PluginHeader. Taking an
    ///         <see cref="IntPtr" /> keeps this signature corelib-only, and the real work is deferred to a
    ///         separate non-inlined method so its types are not resolved until the handler is in place.
    ///     </para>
    /// </summary>
    public static class Engine
    {
        private static readonly string _pluginDirectory =
            Path.GetDirectoryName( typeof( Engine ).Assembly.Location ) ?? AppContext.BaseDirectory;

        /// <summary>
        ///     Managed entry point, found by name through reflection. This is the path TazUO takes, and the
        ///     only one that works on Linux.
        /// </summary>
        /// <param name="header">Pointer to the client's PluginHeader.</param>
        public static void Install( IntPtr header )
        {
#if !NETFRAMEWORK
            AssemblyLoadContext.Default.Resolving += OnResolving;
#endif
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            Start( header );
        }

        /// <summary>
        ///     Native entry point, exported as <c>Install</c> by DNNE for clients that only dlopen the
        ///     plugin and look up the symbol - modern ClassicUO does not fall back to a managed load.
        ///     <para>
        ///         This has to be a second method rather than an attribute on <see cref="Install" />:
        ///         <see cref="UnmanagedCallersOnlyAttribute" /> forbids managed callers, and
        ///         <c>MethodInfo.Invoke</c> on such a method throws <see cref="NotSupportedException" />,
        ///         which would break the reflection path every client on Linux relies on.
        ///     </para>
        /// </summary>
#if !NETFRAMEWORK
        [UnmanagedCallersOnly( EntryPoint = "Install" )]
        public static void NativeInstall( IntPtr header )
        {
            Install( header );
        }
#endif

        [MethodImpl( MethodImplOptions.NoInlining )]
        private static void Start( IntPtr header )
        {
            ClassicAssist.Plugin.PluginEngine.Install( header );
        }

#if !NETFRAMEWORK
        private static Assembly OnResolving( AssemblyLoadContext context, AssemblyName name )
        {
            return LoadFromPluginDirectory( name.Name, name.CultureName );
        }
#endif

        private static Assembly OnAssemblyResolve( object sender, ResolveEventArgs args )
        {
            AssemblyName name = new AssemblyName( args.Name );

            return LoadFromPluginDirectory( name.Name, name.CultureName );
        }

        private static Assembly LoadFromPluginDirectory( string name, string culture )
        {
            if ( string.IsNullOrEmpty( name ) )
            {
                return null;
            }

            string path = Path.Combine( _pluginDirectory, name + ".dll" );

            if ( !File.Exists( path ) && !string.IsNullOrEmpty( culture ) )
            {
                path = Path.Combine( _pluginDirectory, culture, name + ".dll" );
            }

#if NETFRAMEWORK
            return File.Exists( path ) ? Assembly.LoadFrom( path ) : null;
#else
            return File.Exists( path ) ? AssemblyLoadContext.Default.LoadFromAssemblyPath( path ) : null;
#endif
        }
    }
}
