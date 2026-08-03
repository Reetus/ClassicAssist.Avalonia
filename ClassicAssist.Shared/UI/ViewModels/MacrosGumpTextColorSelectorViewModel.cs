using System.Windows.Input;

namespace ClassicAssist.UI.ViewModels
{
    /// <summary>
    ///     View model for the in-game macros gump text color picker. The color is stored as an
    ///     #AARRGGBB string (the format the profile and the gump's BASEFONT use).
    /// </summary>
    public class MacrosGumpTextColorSelectorViewModel
    {
        private ICommand _okCommand;
        private string _selectedColor = "#FFFFFFFF";

        public ICommand OKCommand => _okCommand ?? ( _okCommand = new RelayCommand( OK ) );

        public bool Result { get; set; }

        public string SelectedColor
        {
            get => _selectedColor;
            set => _selectedColor = value;
        }

        private void OK( object obj )
        {
            Result = true;
        }
    }
}
