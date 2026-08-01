using Avalonia.Controls;
using ClassicAssist.Launcher.ViewModels;

namespace ClassicAssist.Launcher.Views
{
    public partial class OptionsWindow : Window
    {
        public OptionsWindow()
        {
            InitializeComponent();

            if ( DataContext is OptionsViewModel vm )
            {
                vm.OwnerWindow = this;
                vm.CloseRequested += Close;
            }
        }
    }
}
