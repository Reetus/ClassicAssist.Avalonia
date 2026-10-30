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
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using ClassicAssist.Updater;
using ClassicAssist.Updater.Models;
using ClassicAssist.Updater.Services;
using ClassicAssist.Updater.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class UpdaterTests
    {
        // Unix mode bits a zip entry claims in its external attributes: S_IFLNK | 0777 claims a
        // symlink, S_IFIFO | 0644 claims a fifo.
        private const int UnixSymlinkMode = 0xA1FF;
        private const int UnixFifoMode = 0x11A4;

        /// <summary>
        ///     The pre-flight that stops a half-applied update: every file the package would overwrite
        ///     is probed before anything is copied.
        /// </summary>
        [TestMethod]
        public void EnsureVerifyWriteAccess()
        {
            string guid = Guid.NewGuid().ToString();

            string path1 = Path.Combine( Path.GetTempPath(), $"{guid}-1" );
            string path2 = Path.Combine( Path.GetTempPath(), $"{guid}-2" );

            Directory.CreateDirectory( Path.Combine( path1, "path" ) );
            Directory.CreateDirectory( Path.Combine( path2, "path" ) );

            try
            {
                File.Create( Path.Combine( path1, guid ) ).Dispose();
                File.Create( Path.Combine( path2, guid ) ).Dispose();

                File.Create( Path.Combine( path1, "path", guid ) ).Dispose();
                File.Create( Path.Combine( path2, "path", guid ) ).Dispose();

                List<string> failList = [];

                InstallGuard.VerifyWriteAccess( new DirectoryInfo( path1 ), new DirectoryInfo( path2 ), failList );

                Assert.AreEqual( 0, failList.Count, "nothing is in use, so nothing should have failed" );

                using ( File.OpenWrite( Path.Combine( path2, "path", guid ) ) )
                {
                    InstallGuard.VerifyWriteAccess( new DirectoryInfo( path1 ), new DirectoryInfo( path2 ),
                        failList );
                }

                // Caught on every platform: Windows locks the file outright, and on Unix .NET
                // emulates FileShare with advisory locks, so a second FileStream is refused there
                // too. What Unix does not catch is a merely mapped assembly, which is why the
                // clients are closed before the copy starts - see RunningClients.
                Assert.AreNotEqual( 0, failList.Count, "an open handle should have failed the probe" );
            }
            finally
            {
                Directory.Delete( path1, true );
                Directory.Delete( path2, true );
            }
        }

        [TestMethod]
        public void WillNotReportMissingDestinationFilesAsInUse()
        {
            string guid = Guid.NewGuid().ToString();

            string source = Path.Combine( Path.GetTempPath(), $"{guid}-src" );
            string destination = Path.Combine( Path.GetTempPath(), $"{guid}-dst" );

            Directory.CreateDirectory( source );
            Directory.CreateDirectory( destination );

            try
            {
                File.Create( Path.Combine( source, "new-file" ) ).Dispose();

                List<string> failList = [];

                InstallGuard.VerifyWriteAccess( new DirectoryInfo( source ), new DirectoryInfo( destination ),
                    failList );

                Assert.AreEqual( 0, failList.Count, "a file that does not exist yet cannot be in use" );
            }
            finally
            {
                Directory.Delete( source, true );
                Directory.Delete( destination, true );
            }
        }

        [TestMethod]
        public void PrereleaseNotNewerNumberSame()
        {
            Assert.IsFalse( VersionHelpers.IsVersionNewer( "0.4.424", "0.4.424-prerelease" ) );
        }

        [TestMethod]
        public void PrereleaseNewerBuildNotSame()
        {
            Assert.IsFalse( VersionHelpers.IsVersionNewer( "0.4.424-prerelease", "0.4.424-prerelease+1" ) );
        }

        [TestMethod]
        public void PrereleaseNewerNumberNotSame()
        {
            Assert.IsTrue( VersionHelpers.IsVersionNewer( "0.4.423", "0.4.424-prerelease" ) );
        }

        /// <summary>
        ///     This tree stamps assemblies 0.5.&lt;builddate&gt;.0, which the WPF build's semver parser
        ///     rejects outright.
        /// </summary>
        [TestMethod]
        public void WillCompareFourPartVersions()
        {
            Assert.IsTrue( VersionHelpers.IsVersionNewer( "0.5.2000.0", "0.5.2001.0" ) );
            Assert.IsFalse( VersionHelpers.IsVersionNewer( "0.5.2001.0", "0.5.2000.0" ) );
            Assert.IsFalse( VersionHelpers.IsVersionNewer( "0.5.2001.0", "0.5.2001.0" ) );
        }

        /// <summary>
        ///     Three and four component spellings of the same version must compare equal, or every
        ///     check would offer the same release forever.
        /// </summary>
        [TestMethod]
        public void WillTreatMissingComponentsAsZero()
        {
            Assert.IsFalse( VersionHelpers.IsVersionNewer( "0.5.2001", "0.5.2001.0" ) );
            Assert.IsFalse( VersionHelpers.IsVersionNewer( "0.5.2001.0", "0.5.2001" ) );
        }

        [TestMethod]
        public void WillToleratePrefixedTags()
        {
            Assert.IsTrue( VersionHelpers.IsVersionNewer( "0.5.2000.0", "v0.5.2001.0" ) );
        }

        /// <summary>
        ///     A develop build is ahead of anything released, so updating would move it backwards.
        /// </summary>
        [TestMethod]
        public void WillNotUpdateDevelop()
        {
            Assert.IsFalse( VersionHelpers.IsVersionNewer( "0.5.0-develop", "9.9.9" ) );
        }

        /// <summary>
        ///     The shape this tree actually stamps on an untagged build - four numeric components and
        ///     a suffix, where WPF used three. Output/ClassicAssist is both the build output and a
        ///     working install, so this is what stops the updater overwriting a tree being worked in.
        /// </summary>
        [TestMethod]
        public void WillNotUpdateAFourPartDevelopBuild()
        {
            Assert.IsFalse( VersionHelpers.IsVersionNewer( "0.5.2230.0-develop", "0.5.9999.0" ) );
        }

        /// <summary>
        ///     An unreadable current version means a broken or absent install, which WPF treated as
        ///     grounds to force the update rather than to give up.
        /// </summary>
        [TestMethod]
        public void WillUpdateWhenCurrentVersionIsUnreadable()
        {
            Assert.IsTrue( VersionHelpers.IsVersionNewer( null, "0.5.2001.0" ) );
            Assert.IsTrue( VersionHelpers.IsVersionNewer( "not-a-version", "0.5.2001.0" ) );
        }

        [TestMethod]
        public void WillParseCommandLine()
        {
            CommandLineOptions options = CommandLineOptions.Parse( [
                "--stage", "Install", "--updatepath", "/tmp/CAUpdate-1", "--path", "/opt/my install",
                "--pid", "1234", "--version", "0.5.1.0", "--force"
            ] );

            Assert.AreEqual( UpdaterStage.Install, options.Stage );
            Assert.AreEqual( "/tmp/CAUpdate-1", options.UpdatePath );
            Assert.AreEqual( "/opt/my install", options.Path );
            Assert.AreEqual( 1234, options.PID );
            Assert.AreEqual( "0.5.1.0", options.CurrentVersion );
            Assert.IsTrue( options.Force );
        }

        [TestMethod]
        public void WillDefaultCommandLineWhenEmpty()
        {
            CommandLineOptions options = CommandLineOptions.Parse( [] );

            Assert.AreEqual( UpdaterStage.Initial, options.Stage );
            Assert.IsFalse( options.Force );
            Assert.AreEqual( 0, options.PID );
        }

        /// <summary>
        ///     A release carries one package per platform; taking the wrong one installs a Windows
        ///     build on Linux.
        /// </summary>
        [TestMethod]
        public void WillSelectThePackageForThisPlatform()
        {
            string[] assets =
            [
                "ClassicAssist-0.5.2001.0-win-x64.zip", "ClassicAssist-0.5.2001.0-linux-x64.zip",
                "ClassicAssist-0.5.2001.0-osx-x64.zip", "ClassicAssist-0.5.2001.0-osx-arm64.zip"
            ];

            string selected = PlatformPackage.Select( assets );

            Assert.IsNotNull( selected );
            StringAssert.Contains( selected, ExpectedToken() );
        }

        /// <summary>
        ///     "win" must not match "darwin", which is why matching is on separator-delimited tokens
        ///     rather than a plain Contains.
        /// </summary>
        [TestMethod]
        public void WillNotMatchATokenMidWord()
        {
            string[] assets = ["ClassicAssist-darwin.zip", $"ClassicAssist-{ExpectedToken()}.zip"];

            Assert.AreEqual( $"ClassicAssist-{ExpectedToken()}.zip", PlatformPackage.Select( assets ) );
        }

        /// <summary>
        ///     One archive and no platform naming is unambiguous, which is what makes a single-package
        ///     repository work before per-platform builds exist.
        /// </summary>
        [TestMethod]
        public void WillTakeTheOnlyArchive()
        {
            Assert.AreEqual( "ClassicAssist.zip",
                PlatformPackage.Select( ["ClassicAssist.zip", "changelog.txt"] ) );
        }

        /// <summary>
        ///     Several unnamed archives are ambiguous, and guessing would install the wrong one.
        /// </summary>
        [TestMethod]
        public void WillNotGuessBetweenUnnamedArchives()
        {
            Assert.IsNull( PlatformPackage.Select( ["one.zip", "two.zip"] ) );
        }

        [TestMethod]
        public void WillIgnoreNonArchiveAssets()
        {
            Assert.IsNull( PlatformPackage.Select( ["notes.txt", "checksums.sha256"] ) );
        }

        [TestMethod]
        public void WillParseGitHubReleases()
        {
            string json = $$"""
                [
                  {
                    "tag_name": "0.5.2001.0",
                    "body": "Release notes",
                    "prerelease": false,
                    "draft": false,
                    "published_at": "2026-01-02T03:04:05Z",
                    "assets": [
                      { "name": "ClassicAssist-{{ExpectedToken()}}.zip", "size": 1234,
                        "browser_download_url": "https://example.invalid/package.zip" }
                    ]
                  }
                ]
                """;

            ChangelogEntry entry = ReleaseSource.Parse( json ).Single();

            Assert.AreEqual( "0.5.2001.0", entry.Version );
            Assert.AreEqual( "Release notes", entry.Description );
            Assert.AreEqual( 1234, entry.DownloadSize );
            Assert.AreEqual( "https://example.invalid/package.zip", entry.DownloadURL );
            Assert.IsFalse( entry.Prerelease );
        }

        /// <summary>
        ///     A draft is not published, and a release with nothing for this platform is dropped rather
        ///     than offered and then failed on.
        /// </summary>
        [TestMethod]
        public void WillSkipDraftsAndUnusableReleases()
        {
            string json = """
                [
                  { "tag_name": "1", "draft": true, "assets": [ { "name": "ClassicAssist-win-x64.zip",
                    "size": 1, "browser_download_url": "https://example.invalid/a.zip" } ] },
                  { "tag_name": "2", "draft": false, "assets": [] },
                  { "tag_name": "3", "draft": false, "assets": [ { "name": "notes.txt", "size": 1,
                    "browser_download_url": "https://example.invalid/notes.txt" } ] }
                ]
                """;

            Assert.AreEqual( 0, ReleaseSource.Parse( json ).Count() );
        }

        /// <summary>
        ///     The WPF build's flat manifest is still accepted, so a fork can point the updater at a
        ///     static file instead of the GitHub API.
        /// </summary>
        [TestMethod]
        public void WillParseLegacyManifest()
        {
            string json = """
                [
                  { "Version": "0.4.424", "Description": "Old style", "Prerelease": false,
                    "DownloadSize": 99, "DownloadURL": "https://example.invalid/ClassicAssist.zip" }
                ]
                """;

            ChangelogEntry entry = ReleaseSource.Parse( json ).Single();

            Assert.AreEqual( "0.4.424", entry.Version );
            Assert.AreEqual( 99, entry.DownloadSize );
            Assert.AreEqual( "ClassicAssist.zip", entry.PackageName );
        }

        /// <summary>
        ///     The assistant lives in ui/, not the install root. Looking only in the root found
        ///     nothing, silently forced every update, and ticked Force Update on startup.
        /// </summary>
        [TestMethod]
        public void WillReadTheVersionFromTheUiSubfolder()
        {
            string root = Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString() );

            Directory.CreateDirectory( Path.Combine( root, "ui" ) );

            try
            {
                // A real managed assembly, so FileVersionInfo has something to report.
                string source = typeof( ClassicAssist.Shared.Engine ).Assembly.Location;

                File.Copy( source, Path.Combine( root, "ui", "ClassicAssist.Shared.dll" ) );

                Assert.IsFalse( string.IsNullOrEmpty( InstallVersion.Resolve( root ) ) );
            }
            finally
            {
                Directory.Delete( root, true );
            }
        }

        [TestMethod]
        public void WillReturnNoVersionForAnEmptyInstall()
        {
            string root = Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString() );

            Directory.CreateDirectory( root );

            try
            {
                Assert.IsNull( InstallVersion.Resolve( root ) );
            }
            finally
            {
                Directory.Delete( root, true );
            }
        }

        [TestMethod]
        public void WillLookInTheUiSubfolderFirst()
        {
            string[] candidates = InstallVersion.Candidates( "/install" );

            StringAssert.Contains( candidates[0], Path.Combine( "ui", "ClassicAssist.Shared.dll" ) );
        }

        /// <summary>
        ///     The traversal-rejection half of the entry-name table. Every archive entry goes
        ///     through this gate, and a crafted archive can name anything, so each of these must be
        ///     refused before a path is ever written to. Asserted on the resolver's return value
        ///     only: a test that actually wrote outside the root would be the bug being tested for.
        /// </summary>
        [TestMethod]
        public void WillRejectEntryNamesThatWouldEscapeTheRoot()
        {
            string root = Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString() );

            string[] rejected =
            [
                // ".." segments are refused outright rather than normalised away - including the
                // spelling that would resolve back inside the root; the segment walk stops it long
                // before normalisation could bless it.
                "../../evil.txt", "..\\..\\evil.txt", "sub/../../evil.txt", "evil/..", "a/../..", "..",
                "sub/../good.txt",

                // "." segments are skipped, which leaves the root itself - and the root is not
                // strictly under root + separator, so the final gate refuses it too.
                ".",

                // Empty and whitespace names say nothing about where anything should be written.
                "", "   ",

                // Rooted names are refused before anything is combined: a rooted segment would
                // make Path.Combine discard everything combined before it.
                "/", "/etc/evil.txt"
            ];

            foreach ( string entryName in rejected )
            {
                Assert.IsFalse( MainViewModel.TryResolveEntryPath( root, entryName, out string destination ),
                    $"'{entryName}' must be refused" );

                Assert.IsNull( destination, $"'{entryName}' must not report a destination" );
            }
        }

        /// <summary>
        ///     Names that are hostile on one platform and ordinary on another. Drive-qualified and
        ///     backslash-rooted names are rooted on Windows and refused at the very first check; the
        ///     Win32 normalisation spoofs are refused by the final gate, where GetFullPath strips
        ///     their trailing dots and spaces and lands them on the root itself. On Unix '\' is not
        ///     a separator, "C:" is just an odd folder name and ".. " is a legal file name, so the
        ///     same names resolve safely inside the root instead. Either way the invariant holds:
        ///     refused, or strictly under the root.
        /// </summary>
        [TestMethod]
        public void WillRefuseRootedAndWin32NormalisedEntryNames()
        {
            string root = Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString() );

            string[] names = ["C:/evil.txt", "C:evil.txt", "\\evil.txt", ".. ", "..."];

            foreach ( string entryName in names )
            {
                bool resolved = MainViewModel.TryResolveEntryPath( root, entryName, out string destination );

                if ( OperatingSystem.IsWindows() )
                {
                    Assert.IsFalse( resolved, $"'{entryName}' must be refused on Windows" );
                    Assert.IsNull( destination, $"'{entryName}' must not report a destination" );
                }
                else if ( resolved )
                {
                    AssertUnderTheRoot( root, destination );
                }
            }
        }

        /// <summary>
        ///     The accepted half of the table: ordinary archive spellings must resolve, and resolve
        ///     strictly under root + separator - the same prefix the production gate checks. These
        ///     are the variants a real archive actually carries: '\' separators from a
        ///     Windows-built archive, './' and '//' noise, and a trailing separator on a directory
        ///     entry.
        /// </summary>
        [TestMethod]
        public void WillResolveEntryNamesStrictlyUnderTheRoot()
        {
            string root = Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString() );

            ( string entryName, string expected )[] accepted =
            [
                ( "sub/win.py", "sub/win.py" ), // plain nested file
                ( "sub\\win.py", "sub/win.py" ), // '\' separators, whatever platform wrote the archive
                ( "./x.txt", "x.txt" ), // '.' segments are skipped
                ( "good//y.txt", "good/y.txt" ), // empty segments are skipped
                ( "dir/", "dir" ) // trailing separator on a directory entry
            ];

            foreach ( ( string entryName, string expected ) in accepted )
            {
                Assert.IsTrue( MainViewModel.TryResolveEntryPath( root, entryName, out string destination ),
                    $"'{entryName}' must resolve" );

                Assert.AreEqual( Path.GetFullPath( Path.Combine( root, expected ) ), destination,
                    $"'{entryName}' must resolve to '{expected}'" );

                AssertUnderTheRoot( root, destination );
            }
        }

        /// <summary>
        ///     The zip-slip scenario end to end against the real module extraction: a crafted
        ///     Modules.zip with one good entry, hostile names, and entries merely claiming (via the
        ///     Unix mode bits in their external attributes) to be a symlink and a fifo. The good
        ///     entry must land with its content, every hostile name must be skipped rather than
        ///     written anywhere, and nothing on disk may end up as a link - a recreated link is what
        ///     a later entry could write through to escape. A zip carries no nodes of its own, so a
        ///     claim can only ever land as plain bytes; asserted per file below.
        /// </summary>
        [TestMethod]
        public void WillSkipHostileZipEntriesWhenExtractingModules()
        {
            string guid = Guid.NewGuid().ToString();

            string scratch = Path.Combine( Path.GetTempPath(), guid );
            string updatePath = Path.Combine( scratch, "update" );
            string installPath = Path.Combine( scratch, "install" );
            string modulesPath = Path.Combine( installPath, "Modules" );

            Directory.CreateDirectory( updatePath );

            try
            {
                string modulesZip = Path.Combine( updatePath, "Modules.zip" );

                using ( ZipArchive zip = ZipFile.Open( modulesZip, ZipArchiveMode.Create ) )
                {
                    WriteZipEntry( zip, "good.txt", "package payload", 0 );

                    // Hostile names: parents spelled with both separators, an attempt only partway
                    // through the name, and a rooted one. Each is refused on every platform - the
                    // platform-dependent spellings live in the table tests above.
                    WriteZipEntry( zip, $"../../../evil-a-{guid}.txt", "escaped", 0 );
                    WriteZipEntry( zip, $"..\\..\\evil-b-{guid}.txt", "escaped", 0 );
                    WriteZipEntry( zip, $"sub/../../evil-c-{guid}.txt", "escaped", 0 );
                    WriteZipEntry( zip, $"/etc/evil-d-{guid}.txt", "escaped", 0 );

                    WriteZipEntry( zip, "link", $"../../outside-{guid}", UnixSymlinkMode );
                    WriteZipEntry( zip, "pipe", "", UnixFifoMode );
                }

                new MainViewModel( true ).ExtractModules( updatePath, installPath );

                Assert.AreEqual( "package payload", File.ReadAllText( Path.Combine( modulesPath, "good.txt" ) ),
                    "the good entry must extract with its content" );

                // Skipped entries write nothing anywhere: the only files under the scratch tree are
                // the archive itself and what the good and claimed entries produced. A hostile
                // entry that merely landed under Modules/ would fail this too - skipped means
                // skipped, not relocated.
                string[] created = Directory.GetFiles( scratch, "*", SearchOption.AllDirectories );

                string[] expected =
                [
                    modulesZip, Path.Combine( modulesPath, "good.txt" ), Path.Combine( modulesPath, "link" ),
                    Path.Combine( modulesPath, "pipe" )
                ];

                CollectionAssert.AreEquivalent( expected, created, "hostile entries must write nothing" );

                Assert.IsFalse( File.Exists( Path.Combine( Path.GetTempPath(), $"evil-a-{guid}.txt" ) ),
                    "an entry naming ../../../evil.txt must not reach the temp directory" );

                foreach ( string path in created )
                {
                    Assert.IsNull( new FileInfo( path ).LinkTarget, $"'{path}' must not be recreated as a link" );

                    Assert.IsFalse( File.GetAttributes( path ).HasFlag( FileAttributes.ReparsePoint ),
                        $"'{path}' must not be a reparse point" );
                }
            }
            finally
            {
                Directory.Delete( scratch, true );
            }
        }

        /// <summary>
        ///     The tar.gz round-trip: a package with one good file, hostile names, and a symlink and
        ///     a fifo - nodes a tar carries natively and a naive extractor would recreate, opening
        ///     the second escape vector a link provides. The good file must land with its content,
        ///     hostile names must be skipped rather than written anywhere, and the link and fifo
        ///     must not be recreated at all.
        /// </summary>
        [TestMethod]
        public void WillSkipHostileAndNonRegularEntriesWhenExtractingPackageTarGz()
        {
            string guid = Guid.NewGuid().ToString();

            string scratch = Path.Combine( Path.GetTempPath(), guid );
            string packagePath = Path.Combine( scratch, "package.tar.gz" );
            string extractPath = Path.Combine( scratch, "extract" );

            Directory.CreateDirectory( scratch );

            try
            {
                using ( FileStream stream = File.Create( packagePath ) )
                using ( GZipStream gzip = new( stream, CompressionMode.Compress ) )
                using ( TarWriter writer = new( gzip ) )
                {
                    writer.WriteEntry( NewTarEntry( TarEntryType.Directory, "sub", null ) );
                    writer.WriteEntry( NewTarEntry( TarEntryType.RegularFile, "sub/good.txt", "package payload" ) );

                    // The same hostile attempts as the zip above.
                    writer.WriteEntry( NewTarEntry( TarEntryType.RegularFile, $"../../evil-a-{guid}.txt",
                        "escaped" ) );
                    writer.WriteEntry( NewTarEntry( TarEntryType.RegularFile, $"..\\..\\evil-b-{guid}.txt",
                        "escaped" ) );
                    writer.WriteEntry( NewTarEntry( TarEntryType.RegularFile, $"sub/../../evil-c-{guid}.txt",
                        "escaped" ) );
                    writer.WriteEntry( NewTarEntry( TarEntryType.RegularFile, $"/etc/evil-d-{guid}.txt",
                        "escaped" ) );

                    UstarTarEntry link = new( TarEntryType.SymbolicLink, "link" ) { LinkName = $"../../outside-{guid}" };

                    writer.WriteEntry( link );
                    writer.WriteEntry( NewTarEntry( TarEntryType.Fifo, "pipe", null ) );
                }

                new MainViewModel( true ).ExtractTarGz( packagePath, extractPath );

                Assert.AreEqual( "package payload",
                    File.ReadAllText( Path.Combine( extractPath, "sub", "good.txt" ) ),
                    "the good file must extract with its content" );

                Assert.IsTrue( Directory.Exists( Path.Combine( extractPath, "sub" ) ),
                    "the directory entry must extract" );

                // Skipped entries leave nothing behind at all - not the node, not a placeholder.
                Assert.IsFalse( File.Exists( Path.Combine( extractPath, "link" ) ) ||
                                Directory.Exists( Path.Combine( extractPath, "link" ) ),
                    "a symlink entry must be skipped, not recreated" );

                Assert.IsFalse( File.Exists( Path.Combine( extractPath, "pipe" ) ) ||
                                Directory.Exists( Path.Combine( extractPath, "pipe" ) ),
                    "a fifo entry must be skipped, not recreated" );

                string[] expected = [packagePath, Path.Combine( extractPath, "sub", "good.txt" )];
                string[] created = Directory.GetFiles( scratch, "*", SearchOption.AllDirectories );

                CollectionAssert.AreEquivalent( expected, created, "hostile entries must write nothing" );

                // The parent entries walk as far as the temp directory itself.
                Assert.IsFalse( File.Exists( Path.Combine( Path.GetTempPath(), $"evil-a-{guid}.txt" ) ),
                    "an entry naming ../../evil.txt must not reach the temp directory" );

                Assert.IsFalse( File.Exists( Path.Combine( Path.GetTempPath(), $"evil-b-{guid}.txt" ) ),
                    "an entry naming ..\\..\\evil.txt must not reach the temp directory" );
            }
            finally
            {
                Directory.Delete( scratch, true );
            }
        }

        private static void AssertUnderTheRoot( string root, string destination )
        {
            StringComparison comparison = RuntimeInformation.IsOSPlatform( OSPlatform.Linux )
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            Assert.IsTrue(
                destination.StartsWith( Path.GetFullPath( root ) + Path.DirectorySeparatorChar, comparison ),
                $"'{destination}' must be strictly under '{root}'" );
        }

        private static void WriteZipEntry( ZipArchive zip, string entryName, string content, int unixMode )
        {
            ZipArchiveEntry entry = zip.CreateEntry( entryName );

            if ( unixMode != 0 )
            {
                // The high 16 bits of the external attributes are the Unix mode a real unzip would
                // recreate the entry with.
                entry.ExternalAttributes = unixMode << 16;
            }

            using ( StreamWriter writer = new( entry.Open() ) )
            {
                writer.Write( content );
            }
        }

        private static TarEntry NewTarEntry( TarEntryType entryType, string entryName, string content )
        {
            UstarTarEntry entry = new( entryType, entryName );

            if ( content != null )
            {
                entry.DataStream = new MemoryStream( Encoding.UTF8.GetBytes( content ) );
            }

            return entry;
        }

        private static string ExpectedToken()
        {
            if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
            {
                return "win-x64";
            }

            return RuntimeInformation.IsOSPlatform( OSPlatform.OSX )
                ? $"osx-{( RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64" )}"
                : "linux-x64";
        }
    }
}
