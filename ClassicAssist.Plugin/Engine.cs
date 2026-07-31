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
        ///     True once <see cref="NativeInstall" /> has run - i.e. the client dlopen'd us and called the
        ///     native export directly, rather than falling back to a managed <c>Assembly.LoadFile</c> load.
        ///     TazUO always takes the managed path (see <see cref="Install" />); this is the modern-CUO
        ///     native loader. <see cref="ClassicAssist.Plugin.PluginEngine.ReflectionAvailable" /> is derived
        ///     from this - client-internals reflection is only expected to work against the TazUO shapes
        ///     this plugin was built against, so it is treated as unavailable on that path rather than
        ///     failing unpredictably per call.
        /// </summary>
        internal static bool LoadedNatively { get; private set; }

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

#if NETFRAMEWORK
            PreloadDependencies();
#endif

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
            LoadedNatively = true;
            Install( header );
        }
#endif

#if NETFRAMEWORK
        /// <summary>
        ///     Loads our own copy of every dependency before anything asks for one.
        ///     <para>
        ///         The client's folder is the AppBase, so .NET Framework probes it first, and its
        ///         <c>.exe.config</c> may redirect a shared assembly to whatever version it ships. Either
        ///         way the bind succeeds against the client's copy and <see cref="OnAssemblyResolve" />
        ///         never fires, so the plugin ends up running against assemblies older than the ones it was
        ///         built against - which surfaces as MissingMethodException deep inside StreamJsonRpc
        ///         rather than as a load failure. Loading ours by full path first means they are already in
        ///         the load set when those references are resolved.
        ///     </para>
        /// </summary>
        private static void PreloadDependencies()
        {
            foreach ( string path in Directory.GetFiles( _pluginDirectory, "*.dll" ) )
            {
                string name = Path.GetFileNameWithoutExtension( path );

                // Never cuoapi: the client already has it loaded, and a second identity is what breaks
                // the delegate exchange in PluginEngine.
                if ( string.Equals( name, "cuoapi", StringComparison.OrdinalIgnoreCase ) ||
                     string.Equals( name, "ClassicAssist", StringComparison.OrdinalIgnoreCase ) )
                {
                    continue;
                }

                try
                {
                    Assembly.LoadFrom( path );
                }
                catch ( Exception )
                {
                    // Not every file beside us has to be a loadable managed assembly.
                }
            }
        }

        /// <summary>
        ///     Answers the System.Reflection.Emit contracts with mscorlib.
        ///     <para>
        ///         StreamJsonRpc generates its client proxies at runtime and so binds these, but they are
        ///         inbox on .NET Framework - NuGet ships only a placeholder for net45 - and the Mono BCL the
        ///         legacy client bundles has no facade for them. Every type they forward to (AssemblyBuilder,
        ///         ModuleBuilder, ILGenerator and the rest) really is in mscorlib there, so pointing the type
        ///         loader at it resolves all of them.
        ///     </para>
        ///     <para>
        ///         Deliberately limited to these three names. The same trick does not work for netstandard,
        ///         which forwards across several assemblies - System.Diagnostics.TraceSource lives in
        ///         System.dll - and AssemblyResolve can only answer with one, which is why that facade is
        ///         shipped as a file instead.
        ///     </para>
        /// </summary>
        private static Assembly ResolveReflectionEmit( string name )
        {
            switch ( name )
            {
                case "System.Reflection.Emit":
                case "System.Reflection.Emit.ILGeneration":
                case "System.Reflection.Emit.Lightweight":
                    return typeof( object ).Assembly;
                default:
                    return null;
            }
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
            if ( File.Exists( path ) )
            {
                return Assembly.LoadFrom( path );
            }

            return ResolveReflectionEmit( name );
#else
            return File.Exists( path ) ? AssemblyLoadContext.Default.LoadFromAssemblyPath( path ) : null;
#endif
        }
    }
}
