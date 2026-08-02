using System.Collections.ObjectModel;
using ClassicAssist.Shared.UI;

namespace ClassicAssist.UI.Models
{
    public class ContainerSet : SetPropertyNotifyChanged
    {
        private ObservableCollection<int> _items = new ObservableCollection<int>();
        private string _name;

        public ObservableCollection<int> Items
        {
            get => _items;
            set => SetProperty( ref _items, value );
        }

        public string Name
        {
            get => _name;
            set => SetProperty( ref _name, value );
        }
    }
}
