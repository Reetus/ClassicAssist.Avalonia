using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ClassicAssist.Data.Scavenger;
using ClassicAssist.UI.ViewModels;

namespace ClassicAssist.UI.ViewModels
{
    public class ScavengerClilocFilterViewModel : BaseViewModel
    {
        private ICommand _addCommand;
        private bool _enabled;
        private ObservableCollection<ScavengerClilocFilterEntry> _items =
            new ObservableCollection<ScavengerClilocFilterEntry>();
        private ICommand _okCommand;
        private ICommand _removeCommand;

        public ScavengerClilocFilterViewModel()
        {
        }

        public ScavengerClilocFilterViewModel( bool enabled, IEnumerable<ScavengerClilocFilterEntry> items )
        {
            Enabled = enabled;

            foreach ( ScavengerClilocFilterEntry item in items )
            {
                Items.Add( item );
            }

            if ( Items.Count == 0 )
            {
                Items.Add( new ScavengerClilocFilterEntry { Cliloc = 501643 } );
                Items.Add( new ScavengerClilocFilterEntry { Cliloc = 501644 } );
            }
        }

        public ICommand AddCommand => _addCommand ?? ( _addCommand = new RelayCommand( Add, o => true ) );

        public bool Enabled
        {
            get => _enabled;
            set => SetProperty( ref _enabled, value );
        }

        public ObservableCollection<ScavengerClilocFilterEntry> Items
        {
            get => _items;
            set => SetProperty( ref _items, value );
        }

        public ICommand OkCommand => _okCommand ?? ( _okCommand = new RelayCommand( Ok, o => true ) );

        public ICommand RemoveCommand =>
            _removeCommand ?? ( _removeCommand = new RelayCommand( Remove, o => o != null ) );

        public bool Result { get; set; }

        private ScavengerClilocFilterEntry _selectedItem;

        public ScavengerClilocFilterEntry SelectedItem
        {
            get => _selectedItem;
            set => SetProperty( ref _selectedItem, value );
        }

        private void Add( object obj )
        {
            Items.Add( new ScavengerClilocFilterEntry { Cliloc = 501643 } );
        }

        private void Ok( object obj )
        {
            Result = true;
        }

        private void Remove( object obj )
        {
            if ( obj is ScavengerClilocFilterEntry filterEntry )
            {
                Items.Remove( filterEntry );
            }
        }
    }
}
