using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            //TODO
            Assistant.Engine.MainWindow = (MainWindow)desktop.MainWindow;

            SEngine.Shutdown += () =>
            {
                desktop.Shutdown( 0 );
                Environment.Exit( 0 );
            };
        }
    }
}
