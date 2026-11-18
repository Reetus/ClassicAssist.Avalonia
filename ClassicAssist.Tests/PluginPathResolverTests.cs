using System;
using System.Collections.Generic;
using System.IO;
using ClassicAssist.Launcher.Client;
using Xunit;

namespace ClassicAssist.Tests
{
    public class PluginPathResolverTests : IDisposable
    {
        [Fact]
        public void Resolve_Framework_ReturnsFrameworkSubfolderPath()
        {
            string result = PluginPathResolver.Resolve( ClientRuntimeFormat.Framework );

            Assert.Equal( Path.Combine( AppContext.BaseDirectory, "framework", "ClassicAssist.dll" ), result );
        }

        [Fact]
        public void Resolve_Managed_ReturnsBaseDirectoryPath()
        {
            string result = PluginPathResolver.Resolve( ClientRuntimeFormat.Managed );

            Assert.Equal( Path.Combine( AppContext.BaseDirectory, "ClassicAssist.dll" ), result );
        }

        [Fact]
        public void Resolve_NativeAot_ReturnsPlatformAppropriateShimExtension()
        {
            string directory = CreateTempDirectory();

            File.WriteAllText( Path.Combine( directory, "ClassicAssistNE" + ShimExtension ), "" );

            string result = PluginPathResolver.Resolve( ClientRuntimeFormat.NativeAot, directory );

            Assert.Equal( Path.Combine( directory, "ClassicAssistNE" + ShimExtension ), result );
        }

        /// <summary>
        ///     The shim needs a C toolchain and is absent without one - on Windows always, since the
        ///     plugin project probes for clang at a Unix path. A NativeAOT client can still take the
        ///     net472 build, which ClassicUO.Bootstrap loads by reflection, so an absent shim is a
        ///     fallback rather than a dead end.
        /// </summary>
        [Fact]
        public void Resolve_NativeAot_FallsBackToFrameworkWhenTheShimIsAbsent()
        {
            string directory = CreateTempDirectory();

            string result = PluginPathResolver.Resolve( ClientRuntimeFormat.NativeAot, directory );

            Assert.Equal( Path.Combine( directory, "framework", "ClassicAssist.dll" ), result );
        }

        [Fact]
        public void Resolve_NativeAot_ForceManaged_IgnoresTheShim()
        {
            string directory = CreateTempDirectory();

            File.WriteAllText( Path.Combine( directory, "ClassicAssistNE" + ShimExtension ), "" );

            string result = PluginPathResolver.Resolve( ClientRuntimeFormat.NativeAot, directory, true );

            Assert.Equal( Path.Combine( directory, "ClassicAssist.dll" ), result );
        }

        [Fact]
        public void Resolve_Unknown_Throws()
        {
            Assert.Throws<InvalidOperationException>( () => PluginPathResolver.Resolve( ClientRuntimeFormat.Unknown ) );
        }

        private static string ShimExtension =>
            OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";

        private readonly List<string> _tempDirectories = [];

        private string CreateTempDirectory()
        {
            string directory = Path.Combine( Path.GetTempPath(), "ClassicAssistTests", Guid.NewGuid().ToString( "N" ) );

            Directory.CreateDirectory( directory );
            _tempDirectories.Add( directory );

            return directory;
        }

        public void Dispose()
        {
            foreach ( string directory in _tempDirectories )
            {
                try
                {
                    Directory.Delete( directory, true );
                }
                catch ( IOException )
                {
                    // best effort
                }
            }
        }
    }
}
