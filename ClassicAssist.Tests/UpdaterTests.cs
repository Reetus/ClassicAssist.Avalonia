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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ClassicAssist.Updater;
using ClassicAssist.Updater.Models;
using ClassicAssist.Updater.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class UpdaterTests
    {
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
