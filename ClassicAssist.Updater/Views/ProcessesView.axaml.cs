using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ClassicAssist.Updater.Views;

public partial class ProcessesView : Window
{
    public ProcessesView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }

    /// <summary>
    ///     Both buttons close; which one was pressed is carried by the view model's Accepted flag,
    ///     set by OKCommand. The launcher's CloseOnClickBehaviour would do the same job, but it lives
    ///     in ClassicAssist.Avalonia and is not one of the files this project links.
    /// </summary>
    private void OnCloseClick( object sender, RoutedEventArgs e )
    {
        Close();
    }
}
