using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClassicAssist.Updater.Models;
using ClassicAssist.Updater.Views;

namespace ClassicAssist.Updater;

public class App : Application
{
    private static IClassicDesktopStyleApplicationLifetime _desktop;

    public static CommandLineOptions CurrentOptions { get; set; } = new();

    public static UpdaterSettings UpdaterSettings { get; set; }

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

        _desktop = desktop;

        CurrentOptions = CommandLineOptions.Parse( desktop.Args );

        if ( string.IsNullOrEmpty( CurrentOptions.Path ) )
        {
            // Stage Initial runs from the install; stage Install runs from the extracted package and
            // is always given --path explicitly.
            CurrentOptions.Path = AppContext.BaseDirectory.TrimEnd( Path.DirectorySeparatorChar );
        }

        UpdaterSettings = UpdaterSettings.Load( CurrentOptions.Path );

        desktop.Exit += ( _, _ ) => UpdaterSettings.Save( UpdaterSettings, CurrentOptions.Path );

        ResolveCurrentVersion();

        desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    ///     The caller normally passes --version. Without it, read it off the installed library; a
    ///     version that cannot be read at all means the install is broken or absent, which WPF treated
    ///     as grounds to force the update rather than to give up.
    /// </summary>
    private static void ResolveCurrentVersion()
    {
        if ( !string.IsNullOrEmpty( CurrentOptions.CurrentVersion ) )
        {
            return;
        }

        CurrentOptions.CurrentVersion = InstallVersion.Resolve( CurrentOptions.Path );

        if ( string.IsNullOrEmpty( CurrentOptions.CurrentVersion ) )
        {
            CurrentOptions.Force = true;
        }
    }

    public static void Shutdown( int exitCode )
    {
        _desktop?.Shutdown( exitCode );
    }
}
