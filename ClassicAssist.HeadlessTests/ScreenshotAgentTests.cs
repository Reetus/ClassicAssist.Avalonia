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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using ClassicAssist.Avalonia.Misc;
using ClassicAssist.Avalonia.Views.Agents;
using ClassicAssist.Data;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UI.ViewModels.Agents;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.HeadlessTests;

/// <summary>
///     The Screenshot agent, driven the way the app drives it: a frame handed over by the (stubbed)
///     plugin, composed by the real Avalonia composer, written to the real profile's folder.
///     <para>
///         What these are really guarding is the seam this port introduces. Upstream took the pixels
///         with GDI inside the UI process; here they arrive as a temp file of raw RGBA from another
///         process, and everything downstream - the stride the frame is copied with, the info bar drawn
///         over it, the PNG encode - is new code with no upstream equivalent to compare against.
///     </para>
/// </summary>
public class ScreenshotAgentTests
{
    private const int FRAME_WIDTH = 320;
    private const int FRAME_HEIGHT = 200;

    /// <summary>Opaque green, as RGBA bytes - what a captured frame arrives as.</summary>
    private static readonly byte[] _framePixel = [0x00, 0xC0, 0x40, 0xFF];

    [Fact]
    public Task ComposesTheCapturedFrameIntoAPng()
    {
        return Headless.Run( () =>
        {
            using ScreenshotHarness harness = new();

            string savedTo = harness.TakeScreenshot();

            Assert.NotNull( savedTo );
            Assert.True( File.Exists( savedTo ) );

            using Bitmap saved = new( savedTo );

            Assert.Equal( new PixelSize( FRAME_WIDTH, FRAME_HEIGHT ), saved.PixelSize );

            // Bottom-left is clear of both overlays, so the frame's own colour has to have survived the
            // copy at full fidelity - a stride or channel-order mistake shows up here.
            Assert.Equal( ( 0x00, 0xC0, 0x40 ), ReadPixel( saved, 4, FRAME_HEIGHT - 4 ) );

            // The info bar is drawn over the top-left corner, so that pixel must no longer be the
            // frame's colour.
            Assert.NotEqual( ( 0x00, 0xC0, 0x40 ), ReadPixel( saved, 2, 2 ) );
        } );
    }

    [Fact]
    public Task LeavesNoFrameFileBehind()
    {
        return Headless.Run( () =>
        {
            using ScreenshotHarness harness = new();

            harness.TakeScreenshot();

            // The frame is the UI's to delete once read - the plugin only sweeps the ones a dead UI
            // abandoned, so a leak here would be several megabytes per screenshot.
            Assert.False( File.Exists( harness.FramePath ) );
        } );
    }

    [Fact]
    public Task WritesTheFilenameFormatWithInvalidCharactersReplaced()
    {
        return Headless.Run( () =>
        {
            using ScreenshotHarness harness = new();

            harness.ViewModel.FilenameFormat = "shot-{mobile}";

            string savedTo = harness.TakeScreenshot( "Orc/Brute" );

            Assert.NotNull( savedTo );
            Assert.Equal( "shot-Orc-Brute.png", Path.GetFileName( savedTo ) );
        } );
    }

    [Fact]
    public Task ReportsNoFileWhenTheClientHandsBackNothing()
    {
        return Headless.Run( () =>
        {
            using ScreenshotHarness harness = new();

            // What a client that stopped ticking looks like from here.
            harness.Host.Frame = () => null;

            Assert.Null( harness.TakeScreenshot() );
        } );
    }

    [Fact]
    public Task RoundTripsItsSettingsThroughTheProfile()
    {
        return Headless.Run( () =>
        {
            using ScreenshotHarness harness = new();

            harness.ViewModel.FilenameFormat = "{player}-{ticks}";
            harness.ViewModel.Format = "{player} {region}";
            harness.ViewModel.FontSize = 22;
            harness.ViewModel.FontColor = "#FF112233";
            harness.ViewModel.BackgroundColor = "#80000000";
            harness.ViewModel.AutoScreenshot = true;
            harness.ViewModel.MobileDeath = true;
            harness.ViewModel.MobileDeathDelay = 750;
            harness.ViewModel.Distance = 7;
            harness.ViewModel.OnlyIfEnemy = true;
            harness.ViewModel.MobileDeathFilter =
                [new ClassicAssist.Data.Screenshot.ScreenshotMobileFilterEntry { ID = 0x190, Note = "Human Male", Enabled = true }];

            JObject json = [];

            harness.ViewModel.Serialize( json );

            ScreenshotTabViewModel reloaded = new();

            reloaded.Deserialize( json, Options.CurrentOptions );

            Assert.Equal( "{player}-{ticks}", reloaded.FilenameFormat );
            Assert.Equal( "{player} {region}", reloaded.Format );
            Assert.Equal( 22, reloaded.FontSize );
            Assert.Equal( "#FF112233", reloaded.FontColor );
            Assert.Equal( "#80000000", reloaded.BackgroundColor );
            Assert.True( reloaded.AutoScreenshot );
            Assert.True( reloaded.MobileDeath );
            Assert.Equal( 750, reloaded.MobileDeathDelay );
            Assert.Equal( 7, reloaded.Distance );
            Assert.True( reloaded.OnlyIfEnemy );
            Assert.Equal( 0x190, Assert.Single( reloaded.MobileDeathFilter ).ID );
        } );
    }

    [Fact]
    public Task SaysSoWhenTheClientCannotBeCaptured()
    {
        return Headless.Run( () =>
        {
            using ScreenshotHarness harness = new( canCapture: false );

            ScreenshotTabControl control = new();
            Window window = new() { Content = control, Width = 900, Height = 600 };

            window.Show();
            Headless.Settle();

            try
            {
                ScreenshotTabViewModel viewModel = Assert.IsType<ScreenshotTabViewModel>( control.DataContext );

                Assert.False( viewModel.CaptureSupported );

                // A NativeAOT ClassicUO reports reflection as available and even answers Client.Game
                // with a stub, so the tab has to be able to say "not with this client" rather than
                // offering a button that fails every time.
                Assert.True( VisibleBanner( control ) );
                Assert.False( TakeSnapshotButton( control ).IsEffectivelyEnabled );
            }
            finally
            {
                window.Close();
            }
        } );
    }

    [Fact]
    public Task OffersTheButtonOnAClientThatCanBeCaptured()
    {
        return Headless.Run( () =>
        {
            using ScreenshotHarness harness = new();

            ScreenshotTabControl control = new();
            Window window = new() { Content = control, Width = 900, Height = 600 };

            window.Show();
            Headless.Settle();

            try
            {
                Assert.True( Assert.IsType<ScreenshotTabViewModel>( control.DataContext ).CaptureSupported );
                Assert.False( VisibleBanner( control ) );
                Assert.True( TakeSnapshotButton( control ).IsEffectivelyEnabled );
            }
            finally
            {
                window.Close();
            }
        } );
    }

    private static bool VisibleBanner( Control control )
    {
        return control.GetVisualDescendants().OfType<Border>().Any( b =>
            b.IsVisible && b.GetVisualDescendants().OfType<TextBlock>()
                .Any( t => t.Text == ClassicAssist.Shared.Resources.Strings.Screenshots_not_supported ) );
    }

    private static Button TakeSnapshotButton( Control control )
    {
        return control.GetVisualDescendants().OfType<Button>()
            .First( b => Equals( b.Content, ClassicAssist.Shared.Resources.Strings.Take_Snapshot ) );
    }

    /// <summary>
    ///     Reads one pixel as RGBA, whatever the platform stores it as.
    ///     <para>
    ///         Reading the saved PNG's bytes directly would be reading them in Skia's native order, and
    ///         that order is not the same everywhere: <c>kN32</c> is BGRA on little-endian x86 and RGBA
    ///         on Apple/ARM, so a hardcoded channel mapping passes on Linux and Windows and fails on
    ///         macOS arm64 - which is exactly what CI caught. Copying through a buffer that is declared
    ///         <see cref="PixelFormat.Rgba8888" /> makes Avalonia transcode into that order instead, so
    ///         the assertions stay about the composer's channel order rather than the host's.
    ///     </para>
    /// </summary>
    private static (byte r, byte g, byte b) ReadPixel( Bitmap bitmap, int x, int y )
    {
        using WriteableBitmap rgba =
            new( bitmap.PixelSize, new Vector( 96, 96 ), PixelFormat.Rgba8888, AlphaFormat.Unpremul );

        using ILockedFramebuffer buffer = rgba.Lock();

        bitmap.CopyPixels( buffer, AlphaFormat.Unpremul );

        byte[] pixel = new byte[4];

        Marshal.Copy( buffer.Address + y * buffer.RowBytes + x * 4, pixel, 0, 4 );

        return ( pixel[0], pixel[1], pixel[2] );
    }

    /// <summary>
    ///     A screenshot tab pointed at a temporary profile folder, with a stub plugin handing it a
    ///     synthetic frame.
    /// </summary>
    private sealed class ScreenshotHarness : IDisposable
    {
        private readonly IHostMethods _originalHost;
        private readonly IScreenshotComposerHolder _originalComposer;
        private readonly string _originalStartupPath;
        private readonly string _tempDirectory;

        public ScreenshotHarness( bool canCapture = true )
        {
            _tempDirectory = Path.Combine( Path.GetTempPath(), $"ca-screenshot-{Guid.NewGuid():N}" );
            Directory.CreateDirectory( _tempDirectory );

            FramePath = Path.Combine( _tempDirectory, "frame.rgba" );
            File.WriteAllBytes( FramePath, BuildFrame() );

            _originalStartupPath = Engine.StartupPath;
            _originalHost = Engine.Host;
            _originalComposer = new IScreenshotComposerHolder( Engine.ScreenshotComposer );

            Engine.StartupPath = _tempDirectory;
            Engine.ScreenshotComposer = new AvaloniaScreenshotComposer();

            Host = new StubHostMethods
            {
                CanCapture = canCapture,
                Frame = () => new ScreenshotFrame { Path = FramePath, Width = FRAME_WIDTH, Height = FRAME_HEIGHT }
            };

            Engine.Host = Host;

            ViewModel = new ScreenshotTabViewModel();
        }

        public string FramePath { get; }
        public StubHostMethods Host { get; }
        public ScreenshotTabViewModel ViewModel { get; }

        public void Dispose()
        {
            Engine.Host = _originalHost;
            Engine.ScreenshotComposer = _originalComposer.Composer;
            Engine.StartupPath = _originalStartupPath;

            try
            {
                Directory.Delete( _tempDirectory, true );
            }
            catch ( IOException )
            {
                // A file still held open somewhere - the temp folder is the OS's problem then.
            }
        }

        /// <summary>
        ///     Runs the capture to completion on the headless dispatcher. The composer hops to the UI
        ///     thread, which is the thread the test body itself is on, so the task has to be pumped
        ///     rather than waited on.
        /// </summary>
        public string TakeScreenshot( string mobileName = null )
        {
            Task<string> capture = ViewModel.TakeScreenshot( mobileName );

            for ( int i = 0; i < 100 && !capture.IsCompleted; i++ )
            {
                Headless.Settle();
            }

            Assert.True( capture.IsCompleted, "The capture never completed." );

            return capture.GetAwaiter().GetResult();
        }

        private static byte[] BuildFrame()
        {
            byte[] pixels = new byte[FRAME_WIDTH * FRAME_HEIGHT * 4];

            for ( int i = 0; i < pixels.Length; i += 4 )
            {
                Array.Copy( _framePixel, 0, pixels, i, 4 );
            }

            return pixels;
        }

        /// <summary>Boxes the previous composer so a null one can be told from "not captured".</summary>
        private sealed record IScreenshotComposerHolder( ClassicAssist.Data.Screenshot.IScreenshotComposer Composer );
    }
}
