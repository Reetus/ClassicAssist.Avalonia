using System;
using System.IO;
using ClassicAssist.Launcher.Client;
using Xunit;

namespace ClassicAssist.Tests
{
    public class ClientRuntimeDetectorTests : IDisposable
    {
        private string _tempDirectory;

        public ClientRuntimeDetectorTests()
        {
            _tempDirectory = Path.Combine( Path.GetTempPath(), "ClientRuntimeDetectorTests_" + Guid.NewGuid() );
            Directory.CreateDirectory( _tempDirectory );
        }

        public void Dispose()
        {
            if ( Directory.Exists( _tempDirectory ) )
            {
                Directory.Delete( _tempDirectory, true );
            }
        }

        [Fact]
        public void Detect_NonexistentFile_ReturnsUnknown()
        {
            ClientRuntimeFormat result = ClientRuntimeDetector.Detect( Path.Combine( _tempDirectory, "missing" ) );

            Assert.Equal( ClientRuntimeFormat.Unknown, result );
        }

        [Fact]
        public void Detect_ElfBinary_NoSiblingRuntimeConfig_ReturnsNativeAot()
        {
            string path = Path.Combine( _tempDirectory, "ClassicUO" );
            File.WriteAllBytes( path, new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0, 0, 0, 0 } );

            ClientRuntimeFormat result = ClientRuntimeDetector.Detect( path );

            Assert.Equal( ClientRuntimeFormat.NativeAot, result );
        }

        [Fact]
        public void Detect_ElfBinary_WithSiblingRuntimeConfig_ReturnsManaged()
        {
            string path = Path.Combine( _tempDirectory, "TazUO" );
            File.WriteAllBytes( path, new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0, 0, 0, 0 } );
            File.WriteAllText( Path.Combine( _tempDirectory, "TazUO.runtimeconfig.json" ), "{}" );

            ClientRuntimeFormat result = ClientRuntimeDetector.Detect( path );

            Assert.Equal( ClientRuntimeFormat.Managed, result );
        }

        [Fact]
        public void Detect_MachOBinary_NoSiblingRuntimeConfig_ReturnsNativeAot()
        {
            string path = Path.Combine( _tempDirectory, "ClassicUO" );
            File.WriteAllBytes( path, new byte[] { 0xCF, 0xFA, 0xED, 0xFE, 0, 0, 0, 0 } ); // MH_MAGIC_64

            ClientRuntimeFormat result = ClientRuntimeDetector.Detect( path );

            Assert.Equal( ClientRuntimeFormat.NativeAot, result );
        }

        [Fact]
        public void Detect_ManagedAssembly_HasCorHeader_ReturnsFramework()
        {
            // Every managed assembly - including this running test assembly - has a CLI/COR20
            // header regardless of target framework; that's exactly the ambiguity documented on
            // ClientRuntimeDetector, and why a Framework/Mono client is detected this way.
            string realManagedDll = typeof( ClientRuntimeDetectorTests ).Assembly.Location;

            ClientRuntimeFormat result = ClientRuntimeDetector.Detect( realManagedDll );

            Assert.Equal( ClientRuntimeFormat.Framework, result );
        }

        [Fact]
        public void Detect_NativePeStub_NoSiblingRuntimeConfig_ReturnsNativeAot()
        {
            string path = Path.Combine( _tempDirectory, "ClassicUO.exe" );
            File.WriteAllBytes( path, NativePeFixture.Build() );

            ClientRuntimeFormat result = ClientRuntimeDetector.Detect( path );

            Assert.Equal( ClientRuntimeFormat.NativeAot, result );
        }

        [Fact]
        public void Detect_NativePeStub_WithSiblingRuntimeConfig_ReturnsManaged()
        {
            string path = Path.Combine( _tempDirectory, "ClassicUO.exe" );
            File.WriteAllBytes( path, NativePeFixture.Build() );
            File.WriteAllText( Path.Combine( _tempDirectory, "ClassicUO.runtimeconfig.json" ), "{}" );

            ClientRuntimeFormat result = ClientRuntimeDetector.Detect( path );

            Assert.Equal( ClientRuntimeFormat.Managed, result );
        }
    }
}
