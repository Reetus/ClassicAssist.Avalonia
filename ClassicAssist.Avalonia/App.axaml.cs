using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClassicAssist.Avalonia.Misc;
using ClassicAssist.Avalonia.Views;
using ClassicAssist.UI.ViewModels;
using SEngine = ClassicAssist.Shared.Engine;

namespace ClassicAssist.Avalonia
{
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

            SEngine.InstallRPC( Program.Host, new AvaloniaMessageBoxProvider() );
            UiHost.Initialize();

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            MainWindow mainWindow = new MainWindow { DataContext = new MainWindowViewModel() };

            desktop.MainWindow = mainWindow;
            UiHost.MainWindow = mainWindow;

            SEngine.Shutdown += () => Dispatcher.UIThread.Post( () => desktop.Shutdown( 0 ) );
        }
    }
}
