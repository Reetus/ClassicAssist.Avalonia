using System;
using System.Diagnostics;
using Avalonia.Controls;
using ClassicAssist.Launcher.ViewModels;

namespace ClassicAssist.Launcher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        if ( DataContext is MainViewModel vm )
        {
            vm.OwnerWindow = this;
            vm.RequestShutdown += Close;
        }
    }

    protected override void OnClosing( WindowClosingEventArgs e )
    {
        base.OnClosing( e );

        if ( DataContext is MainViewModel vm )
        {
            try
            {
                vm.ClosingCommand.Execute( null );
            }
            catch ( Exception ex )
            {
                // MainViewModel.Closing already fails soft on its own, but a save failure must
                // never escape the window-close path however it is thrown - log it and let the
                // close complete (the app is shutting down by this point anyway).
                Debug.WriteLine( $"Error saving launcher settings on close: {ex}" );
            }
        }
    }
}
