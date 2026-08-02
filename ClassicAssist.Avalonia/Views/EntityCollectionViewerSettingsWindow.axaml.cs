using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ClassicAssist.Avalonia.Views
{
    public partial class EntityCollectionViewerSettingsWindow : Window
    {
        public EntityCollectionViewerSettingsWindow()
        {
            InitializeComponent();
        }

        private void OnCloseClick( object sender, RoutedEventArgs e )
        {
            Close();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load( this );
        }
    }
}
