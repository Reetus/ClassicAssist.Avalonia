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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using ClassicAssist.Avalonia;
using ClassicAssist.HeadlessTests;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

[assembly: AvaloniaTestApplication( typeof( TestAppBuilder ) )]

// One Avalonia application and one dispatcher thread for the whole assembly, and the tests share
// process-wide state either way (Engine.StartupPath, Application.Current), so they run one at a time.
// xunit.v3 4.0 removed CollectionBehavior.DisableTestParallelization; ParallelMode.None is the
// equivalent of the old DisableTestParallelization = true.
[assembly: Parallelization( Mode = ParallelMode.None )]

namespace ClassicAssist.HeadlessTests;

/// <summary>
///     The application the headless session runs the tests against - the real <see cref="App" />, so
///     the windows under test get the real styles and icon resources. Its
///     <c>OnFrameworkInitializationCompleted</c> only builds the main window under a classic desktop
///     lifetime, which a headless session is not, so nothing of the plugin's startup path runs here.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            // Skia rather than the default no-op drawing: the views pull real bitmaps out of
            // Assets/Icons.xaml, and decoding those needs a render interface that can.
            .UseSkia()
            .UseHeadless( new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false } );
    }
}

/// <summary>
///     Runs a test body on the headless session's UI thread.
///     <para>
///         Avalonia ships both the session (<see cref="HeadlessUnitTestSession" />) and an
///         <c>[AvaloniaFact]</c> attribute, but the attribute (Avalonia.Headless.XUnit 12.1.6) is
///         compiled against xunit.v3 3.2.2 and this project is on 4.0.3, so it fails at discovery
///         (AvaloniaUI/Avalonia#22072). Tests are therefore plain <c>[Fact]</c>s returning
///         <see cref="Run" />; switch to <c>[AvaloniaFact]</c> when a compatible package ships.
///     </para>
/// </summary>
public static class Headless
{
    private static readonly HeadlessUnitTestSession _session =
        HeadlessUnitTestSession.GetOrStartForAssembly( typeof( Headless ).Assembly );

    public static Task Run( Action action )
    {
        return _session.Dispatch( () =>
        {
            action();

            return Task.CompletedTask;
        }, default );
    }

    /// <summary>
    ///     Lets queued dispatcher work (bindings, layout, the property changes a selection kicks off)
    ///     run to completion, the way it would between two user actions.
    /// </summary>
    public static void Settle()
    {
        for ( int i = 0; i < 5; i++ )
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
