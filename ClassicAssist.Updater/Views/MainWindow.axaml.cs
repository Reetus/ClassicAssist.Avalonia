using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassicAssist.Updater.ViewModels;

namespace ClassicAssist.Updater.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        MainViewModel viewModel = new();

        // Assigned here rather than instantiated in XAML so the same instance can be handed its
        // owner - the "clients are running" dialog needs one to centre on.
        viewModel.OwnerWindow = this;
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }

    private void OnCloseClick( object sender, RoutedEventArgs e )
    {
        Close();
    }

    private void OnReleasesClick( object sender, PointerPressedEventArgs e )
    {
        string url = ( DataContext as MainViewModel )?.UpdaterSettings?.ReleasesURL;

        if ( string.IsNullOrEmpty( url ) )
        {
            return;
        }

        try
        {
            // The releases setting points at the API; the page a human wants is the html one.
            Process.Start( new ProcessStartInfo( ReleasesPageUrl( url ) ) { UseShellExecute = true } );
        }
        catch ( Exception )
        {
            // No browser, or a url the shell will not open.
        }
    }

    internal static string ReleasesPageUrl( string apiUrl )
    {
        const string prefix = "https://api.github.com/repos/";

        if ( !apiUrl.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
        {
            return apiUrl;
        }

        return $"https://github.com/{apiUrl[prefix.Length..].TrimEnd( '/' )}";
    }
}
