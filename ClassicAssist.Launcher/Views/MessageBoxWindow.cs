using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassicAssist.Launcher.Views;

/// <summary>
///     A minimal, self-contained message dialog. Kept out of XAML and dependency-free (no
///     ClassicAssist.Shared IMessageBoxProvider) since this project's only need is a single
///     "something went wrong" prompt.
/// </summary>
internal static class MessageBoxWindow
{
    public static async Task ShowAsync( Window owner, string message, string title = "Error" )
    {
        Window window = new()
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };

        Button okButton = new() { Content = "OK", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness( 0, 10, 0, 0 ) };
        okButton.Click += ( _, _ ) => window.Close();

        window.Content = new StackPanel
        {
            Margin = new Thickness( 15 ),
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, okButton }
        };

        if ( owner != null )
        {
            await window.ShowDialog( owner );
        }
        else
        {
            window.Show();
        }
    }
}
