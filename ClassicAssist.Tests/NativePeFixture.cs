using System.IO;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Builds a minimal, valid native PE32+ image with no COR20/CLI header - the shape of a
    ///     modern .NET apphost stub or a Windows NativeAOT executable, as opposed to a managed
    ///     .NET Framework/Mono assembly (which always has one). Built at test time rather than
    ///     committed as a binary fixture, since PEReader parsing is pure managed code and doesn't
    ///     need a real Windows binary to validate against - see ClientRuntimeDetectorTests.
    /// </summary>
    internal static class NativePeFixture
    {
        public static byte[] Build()
        {
            using MemoryStream ms = new MemoryStream();
            using BinaryWriter bw = new BinaryWriter( ms );

            // DOS header (64 bytes): "MZ" + padding + e_lfanew pointing right after it.
            bw.Write( (ushort) 0x5A4D );

            for ( int i = 0; i < 29; i++ )
            {
                bw.Write( (ushort) 0 );
            }

            bw.Write( (uint) 64 );

            // PE signature.
            bw.Write( (uint) 0x00004550 );

            // COFF header (20 bytes).
            bw.Write( (ushort) 0x8664 ); // IMAGE_FILE_MACHINE_AMD64
            bw.Write( (ushort) 0 ); // NumberOfSections
            bw.Write( (uint) 0 ); // TimeDateStamp
            bw.Write( (uint) 0 ); // PointerToSymbolTable
            bw.Write( (uint) 0 ); // NumberOfSymbols
            const ushort optionalHeaderSize = 112 + 16 * 8; // PE32+ fixed part + 16 data directories
            bw.Write( optionalHeaderSize );
            bw.Write( (ushort) 0x0022 ); // EXECUTABLE_IMAGE | LARGE_ADDRESS_AWARE

            // Optional header (PE32+, no COR20/CLI data directory entry - RVA/Size left zero).
            bw.Write( (ushort) 0x20b ); // PE32+
            bw.Write( (byte) 0 );
            bw.Write( (byte) 0 );
            bw.Write( (uint) 0 ); // SizeOfCode
            bw.Write( (uint) 0 ); // SizeOfInitializedData
            bw.Write( (uint) 0 ); // SizeOfUninitializedData
            bw.Write( (uint) 0x1000 ); // AddressOfEntryPoint
            bw.Write( (uint) 0x1000 ); // BaseOfCode
            bw.Write( (ulong) 0x140000000 ); // ImageBase
            bw.Write( (uint) 0x1000 ); // SectionAlignment
            bw.Write( (uint) 0x200 ); // FileAlignment
            bw.Write( (ushort) 6 );
            bw.Write( (ushort) 0 ); // OS version
            bw.Write( (ushort) 0 );
            bw.Write( (ushort) 0 ); // Image version
            bw.Write( (ushort) 6 );
            bw.Write( (ushort) 0 ); // Subsystem version
            bw.Write( (uint) 0 ); // Win32VersionValue
            bw.Write( (uint) 0x2000 ); // SizeOfImage
            bw.Write( (uint) 0x200 ); // SizeOfHeaders
            bw.Write( (uint) 0 ); // CheckSum
            bw.Write( (ushort) 3 ); // IMAGE_SUBSYSTEM_WINDOWS_CUI
            bw.Write( (ushort) 0 ); // DllCharacteristics
            bw.Write( (ulong) 0x100000 ); // SizeOfStackReserve
            bw.Write( (ulong) 0x1000 ); // SizeOfStackCommit
            bw.Write( (ulong) 0x100000 ); // SizeOfHeapReserve
            bw.Write( (ulong) 0x1000 ); // SizeOfHeapCommit
            bw.Write( (uint) 0 ); // LoaderFlags
            bw.Write( (uint) 16 ); // NumberOfRvaAndSizes

            for ( int i = 0; i < 16; i++ )
            {
                bw.Write( (uint) 0 ); // RVA
                bw.Write( (uint) 0 ); // Size - zero means "not present", including index 14 (COM descriptor)
            }

            while ( ms.Length < 0x200 )
            {
                bw.Write( (byte) 0 );
            }

            return ms.ToArray();
        }
    }
}
