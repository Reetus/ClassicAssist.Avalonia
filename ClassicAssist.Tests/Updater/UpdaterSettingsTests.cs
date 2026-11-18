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
using System.Diagnostics;
using System.IO;
using ClassicAssist.Tests.UOData;
using ClassicAssist.Updater.Models;
using ClassicAssist.Updater.Services;
using ClassicAssist.Updater.ViewModels;
using Xunit;

namespace ClassicAssist.Tests.Updater
{
    /// <summary>
    ///     updater.settings.json decides where releases are fetched from and whether prereleases count. The
    ///     updater has to run even when the install is half broken, so a missing, corrupt or blanked file
    ///     must come back as working defaults rather than stop it, and a save into a read-only install must
    ///     not throw.
    /// </summary>
    public class UpdaterSettingsTests : IDisposable
    {
        private readonly TempDirectory _directory = new TempDirectory();

        public void Dispose()
        {
            _directory.Dispose();
        }

        [Fact]
        public void WillRoundTripTheSettings()
        {
            UpdaterSettings.Save( new UpdaterSettings { ReleasesURL = "https://fork.example/releases", InstallPrereleases = true },
                _directory.Path );

            UpdaterSettings loaded = UpdaterSettings.Load( _directory.Path );

            Assert.Equal( "https://fork.example/releases", loaded.ReleasesURL );
            Assert.True( loaded.InstallPrereleases );
        }

        [Theory]
        [InlineData( null )]
        [InlineData( "{ not json" )]
        [InlineData( "null" )]
        [InlineData( "{ \"ReleasesURL\": \"  \", \"InstallPrereleases\": true }" )]
        public void WillFallBackToTheDefaultReleasesUrl( string contents )
        {
            if ( contents != null )
            {
                _directory.Write( "updater.settings.json", System.Text.Encoding.UTF8.GetBytes( contents ) );
            }

            UpdaterSettings loaded = UpdaterSettings.Load( _directory.Path );

            Assert.Equal( UpdaterSettings.DEFAULT_RELEASES_URL, loaded.ReleasesURL );
        }

        [Fact]
        public void WillKeepOtherSettingsWhenOnlyTheUrlIsBlank()
        {
            _directory.Write( "updater.settings.json",
                System.Text.Encoding.UTF8.GetBytes( "{ \"ReleasesURL\": \"\", \"InstallPrereleases\": true }" ) );

            Assert.True( UpdaterSettings.Load( _directory.Path ).InstallPrereleases );
        }

        [Fact]
        public void WillNotThrowSavingToAMissingDirectory()
        {
            UpdaterSettings.Save( new UpdaterSettings(), _directory.File( "missing" ) );

            Assert.False( Directory.Exists( _directory.File( "missing" ) ) );
        }

        /// <summary>
        ///     The updater kills whatever has the install loaded, so it must never count itself, and a path that
        ///     does not exist has nothing loaded from it.
        /// </summary>
        [Fact]
        public void WillNeverFindItselfAmongRunningClients()
        {
            Assert.Empty( RunningClients.Find( _directory.File( "missing" ) ) );
            Assert.Empty( RunningClients.Find( null ) );

            Process[] found = RunningClients.Find( AppContext.BaseDirectory.TrimEnd( Path.DirectorySeparatorChar ) );

            Assert.DoesNotContain( found, p => p.Id == Environment.ProcessId );
        }

        /// <summary>
        ///     Closing the "clients are running" prompt any way other than OK must cancel the update.
        /// </summary>
        [Fact]
        public void WillOnlyAcceptTheProcessPromptOnOk()
        {
            using Process self = Process.GetCurrentProcess();

            ProcessesViewModel vm = new ProcessesViewModel( new[] { self } );

            Assert.False( vm.Accepted );
            Assert.Contains( $"({self.Id})", Assert.Single( vm.Processes ) );

            vm.OKCommand.Execute( null );

            Assert.True( vm.Accepted );
        }
    }
}
