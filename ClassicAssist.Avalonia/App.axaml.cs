using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClassicAssist.Avalonia.Misc;
using ClassicAssist.Avalonia.Views;
using ClassicAssist.UI.ViewModels;
using SEngine = ClassicAssist.Shared.Engine;

namespace ClassicAssist.Avalonia;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load( this );
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if ( ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop )
        {
            return;
        }

        // Must precede InstallRPC: constructing the managers and view models marshals onto these.
        SEngine.Dispatcher = new AvaloniaDispatcher( Dispatcher.UIThread );
        SEngine.UIInvoker = new AvaloniaUIInvoker( Dispatcher.UIThread );

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        SplashWindow splash = new();
        splash.Show();

        // The rest is deliberately deferred rather than run inline. InstallRPC blocks on RPC round
        // trips and reads the client's Art/Cliloc/TileData/Statics files, and building the main
        // window constructs every tab's view model, all of it on this thread - run here it would
        // finish before the splash ever got a layout pass, so nothing would be on screen for the
        // one period the splash exists to cover. Posting below Render/Layout priority lets the
        // splash's first frame reach the compositor first; the render thread keeps it painted
        // while the UI thread is busy.
        Dispatcher.UIThread.Post( () =>
        {
            try
            {
                SEngine.InstallRPC( Program.Host, new AvaloniaMessageBoxProvider() );
                UiHost.Initialize();

                MainWindow mainWindow = new() { DataContext = new MainWindowViewModel() };

                desktop.MainWindow = mainWindow;
                UiHost.MainWindow = mainWindow;

                // Shown explicitly: the lifetime only auto-shows a MainWindow that was already set
                // when OnFrameworkInitializationCompleted returned, which by now it has.
                mainWindow.Show();
            }
            finally
            {
                // In a finally so a failure during load cannot strand an undismissable topmost
                // window over the client.
                splash.Close();
            }
        }, DispatcherPriority.Background );

        SEngine.Shutdown += () => Dispatcher.UIThread.Post( () => desktop.Shutdown( 0 ) );
    }
}
