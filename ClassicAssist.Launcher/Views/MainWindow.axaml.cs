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
            vm.ClosingCommand.Execute( null );
        }
    }
}
