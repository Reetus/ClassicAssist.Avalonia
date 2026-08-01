using System;
using System.IO;
using ClassicAssist.Launcher.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class PluginPathResolverTests
    {
        [TestMethod]
        public void Resolve_Framework_ReturnsFrameworkSubfolderPath()
        {
            string result = PluginPathResolver.Resolve( ClientRuntimeFormat.Framework );

            Assert.AreEqual( Path.Combine( AppContext.BaseDirectory, "framework", "ClassicAssist.dll" ), result );
        }

        [TestMethod]
        public void Resolve_Managed_ReturnsBaseDirectoryPath()
        {
            string result = PluginPathResolver.Resolve( ClientRuntimeFormat.Managed );

            Assert.AreEqual( Path.Combine( AppContext.BaseDirectory, "ClassicAssist.dll" ), result );
        }

        [TestMethod]
        public void Resolve_NativeAot_ReturnsPlatformAppropriateShimExtension()
        {
            string result = PluginPathResolver.Resolve( ClientRuntimeFormat.NativeAot );

            string expectedExtension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";

            Assert.AreEqual( Path.Combine( AppContext.BaseDirectory, "ClassicAssistNE" + expectedExtension ), result );
        }

        [TestMethod]
        public void Resolve_Unknown_Throws()
        {
            Assert.ThrowsException<InvalidOperationException>( () => PluginPathResolver.Resolve( ClientRuntimeFormat.Unknown ) );
        }
    }
}
