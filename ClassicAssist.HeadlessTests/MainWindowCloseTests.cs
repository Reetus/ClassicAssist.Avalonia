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
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using ClassicAssist.Avalonia.Views;
using ClassicAssist.Shared;
using Xunit;

namespace ClassicAssist.HeadlessTests;

/// <summary>
///     The assistant is a companion to the game client and must not be dismissable on its own - see
///     <c>MainWindow.OnClosing</c>.
/// </summary>
public class MainWindowCloseTests
{
    /// <summary>
    ///     <c>IWindowImpl.Closing</c> is not reachable from source - Avalonia keeps it out of
    ///     the reference assembly the compiler sees - but the implementation exposes it, so reflection
    ///     is the only way to deliver a platform close notification in a test.
    /// </summary>
    private static Func<WindowCloseReason, bool> GetPlatformClosing( MainWindow window )
    {
        IWindowImpl impl = window.PlatformImpl;

        PropertyInfo property = typeof( IWindowImpl ).GetProperty( "Closing",
            BindingFlags.Public | BindingFlags.Instance );

        Assert.NotNull( property );

        Func<WindowCloseReason, bool> closing = (Func<WindowCloseReason, bool>) property.GetValue( impl );

        Assert.NotNull( closing );

        return closing;
    }

    /// <summary>
    ///     Delivers the close notification the platform sends for a title bar close button, Alt+F4 or
    ///     the taskbar's Close, which is the non-programmatic <see cref="WindowCloseReason.WindowClosing" />
    ///     path <see cref="Window.Close" /> does not exercise.
    /// </summary>
    private static bool TryPlatformClose( MainWindow window, WindowCloseReason reason )
    {
        bool closed = false;

        void OnClosed( object sender, EventArgs e )
        {
            closed = true;
        }

        Func<WindowCloseReason, bool> closing = GetPlatformClosing( window );

        window.Closed += OnClosed;

        // Deliver the notification the platform would raise for a user close; whether the window
        // actually closed shows up through the Closed event above.
        closing.Invoke( reason );

        window.Closed -= OnClosed;

        Headless.Settle();

        return closed;
    }

    [Fact]
    public Task UserInitiatedCloseIsRefusedButDeliberateShutdownIsAllowed()
    {
        return Headless.Run( () =>
        {
            string originalStartupPath = Engine.StartupPath;
            string tempDirectory = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );

            Directory.CreateDirectory( tempDirectory );
            Engine.StartupPath = tempDirectory;

            try
            {
                MainWindow window = new();

                window.Show();
                Headless.Settle();

                Assert.False( TryPlatformClose( window, WindowCloseReason.WindowClosing ) );

                // The window survived, so the deliberate reasons still have to get through.
                Assert.True( TryPlatformClose( window, WindowCloseReason.ApplicationShutdown ) );
            }
            finally
            {
                Engine.StartupPath = originalStartupPath;

                try
                {
                    Directory.Delete( tempDirectory, true );
                }
                catch ( IOException )
                {
                    // best effort
                }
            }
        } );
    }

    [Fact]
    public Task ProgrammaticCloseIsStillAllowed()
    {
        return Headless.Run( () =>
        {
            string originalStartupPath = Engine.StartupPath;
            string tempDirectory = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );

            Directory.CreateDirectory( tempDirectory );
            Engine.StartupPath = tempDirectory;

            try
            {
                MainWindow window = new();

                window.Show();
                Headless.Settle();

                bool closed = false;

                window.Closed += ( _, _ ) => closed = true;

                // What the desktop lifetime's shutdown (the client going away) calls.
                window.Close();
                Headless.Settle();

                Assert.True( closed );
            }
            finally
            {
                Engine.StartupPath = originalStartupPath;

                try
                {
                    Directory.Delete( tempDirectory, true );
                }
                catch ( IOException )
                {
                    // best effort
                }
            }
        } );
    }
}
