using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ClassicAssist.Shared.UI.ViewModels.Agents;

namespace ClassicAssist.Avalonia.Views.Agents;

public partial class ScreenshotTabControl : UserControl
{
    public ScreenshotTabControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }

    /// <summary>
    ///     Opens the double-clicked screenshot in the platform's image viewer. Done in code-behind
    ///     because a gesture inside an item template has no clean way to reach the tab's own command
    ///     with the row as its parameter.
    /// </summary>
    private void OnScreenshotDoubleTapped( object sender, TappedEventArgs e )
    {
        if ( sender is Control { DataContext: ScreenshotTabViewModel.ScreenshotEntry entry } &&
             DataContext is ScreenshotTabViewModel viewModel &&
             viewModel.OpenScreenshotCommand.CanExecute( entry ) )
        {
            viewModel.OpenScreenshotCommand.Execute( entry );
        }
    }
}
