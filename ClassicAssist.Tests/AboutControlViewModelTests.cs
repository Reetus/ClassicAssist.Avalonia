using System;
using System.IO;
using System.Reflection;
using ClassicAssist.Shared;
using ClassicAssist.UI.ViewModels;
using Xunit;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     The Check for Updates button has now failed silently twice - once because the apphost has
    ///     no .exe outside Windows, once because the assistant runs from ui/ while the updater sits in
    ///     the install root above it. Neither is visible without launching the app, hence these.
    /// </summary>
    public class AboutControlViewModelTests : IDisposable
    {
        private string _originalStartupPath;
        private string _tempDir;

        /// <summary>The apphost has no extension outside Windows.</summary>
        private static string UpdaterFileName =>
            OperatingSystem.IsWindows() ? "ClassicAssist.Updater.exe" : "ClassicAssist.Updater";

        public AboutControlViewModelTests()
        {
            _originalStartupPath = Engine.StartupPath;
            _tempDir = Path.Combine( Path.GetTempPath(), "ClassicAssistTests", Guid.NewGuid().ToString( "N" ) );
            Directory.CreateDirectory( _tempDir );
        }

        public void Dispose()
        {
            Engine.StartupPath = _originalStartupPath;

            try
            {
                Directory.Delete( _tempDir, true );
            }
            catch ( IOException )
            {
                // best effort
            }
        }

        [Fact]
        public void FindsUpdaterOneLevelAboveTheUIFolder()
        {
            string installPath = Path.Combine( _tempDir, "ClassicAssist" );
            string uiPath = Path.Combine( installPath, "ui" );

            Directory.CreateDirectory( uiPath );

            string updater = Path.Combine( installPath, UpdaterFileName );
            File.WriteAllText( updater, "" );

            Engine.StartupPath = uiPath;

            string found = AboutControlViewModel.FindUpdater();

            Assert.Equal( updater, found );

            // The install root, not ui/ - handing the updater the latter would have it copy a release
            // into ui/.
            Assert.Equal( installPath, Path.GetDirectoryName( found ) );
        }

        [Fact]
        public void FindsUpdaterBesideItselfInAFlatFolder()
        {
            string updater = Path.Combine( _tempDir, UpdaterFileName );
            File.WriteAllText( updater, "" );

            Engine.StartupPath = _tempDir;

            Assert.Equal( updater, AboutControlViewModel.FindUpdater() );
        }

        [Fact]
        public void ReturnsNullWhenThereIsNoUpdater()
        {
            string uiPath = Path.Combine( _tempDir, "ClassicAssist", "ui" );

            Directory.CreateDirectory( uiPath );

            Engine.StartupPath = uiPath;

            Assert.Null( AboutControlViewModel.FindUpdater() );
        }

        /// <summary>
        ///     The path is handed to the updater, which shows it to the user and matches it against
        ///     the module paths of running clients - a ui/../ in the middle would match nothing.
        /// </summary>
        [Fact]
        public void ReturnsANormalisedPath()
        {
            string installPath = Path.Combine( _tempDir, "ClassicAssist" );

            Directory.CreateDirectory( Path.Combine( installPath, "ui" ) );
            File.WriteAllText( Path.Combine( installPath, UpdaterFileName ), "" );

            Engine.StartupPath = Path.Combine( installPath, "ui" );

            string found = AboutControlViewModel.FindUpdater();

            Assert.False( found.Contains( ".." ), $"{found} still contains a relative segment" );
        }

        /// <summary>
        ///     The About tab used to reconstruct the build date from the version's third component,
        ///     which only holds while nothing overrides Version - and the release workflow always
        ///     does, so every release displayed a date years out. Guards the AssemblyAttribute item
        ///     in ClassicAssist.Shared.csproj, which nothing else would notice going missing.
        /// </summary>
        [Fact]
        public void BuildDateComesFromTheStampedAttribute()
        {
            Assembly assembly = typeof( AboutControlViewModel ).Assembly;

            Assert.NotNull( assembly.GetCustomAttribute<BuildDateAttribute>() ); // "ClassicAssist.Shared has no BuildDateAttribute"

            DateTime buildDate = AboutControlViewModel.GetBuildDateTime( assembly );

            Assert.True( buildDate <= DateTime.Now.AddDays( 1 ), $"{buildDate} is in the future" );
            Assert.True( buildDate > new DateTime( 2020, 7, 3 ), $"{buildDate} is the epoch the old version maths started from" );
        }
    }
}
