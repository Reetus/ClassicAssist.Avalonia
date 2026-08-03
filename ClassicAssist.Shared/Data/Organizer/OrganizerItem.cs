using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClassicAssist.Data.Organizer
{
    public class OrganizerItem : INotifyPropertyChanged
    {
        private int _amount;
        private int? _destinationContainer;
        private int _hue;
        private int _id;
        private string _item;
        private int? _sourceContainer;

        public int Amount
        {
            get => _amount;
            set => SetProperty( ref _amount, value );
        }

        /// <summary>Per-item destination container override. When set it wins over the entry-level
        /// <see cref="OrganizerEntry.DestinationContainer" /> for this item.</summary>
        public int? DestinationContainer
        {
            get => _destinationContainer;
            set => SetProperty( ref _destinationContainer, value );
        }

        public int Hue
        {
            get => _hue;
            set => SetProperty( ref _hue, value );
        }

        public int ID
        {
            get => _id;
            set => SetProperty( ref _id, value );
        }

        public string Item
        {
            get => _item;
            set => SetProperty( ref _item, value );
        }

        /// <summary>Per-item source container override. When set it wins over the entry-level
        /// <see cref="OrganizerEntry.SourceContainer" /> for this item.</summary>
        public int? SourceContainer
        {
            get => _sourceContainer;
            set => SetProperty( ref _sourceContainer, value );
        }

        public event PropertyChangedEventHandler PropertyChanged;

        // ReSharper disable once RedundantAssignment
        public void SetProperty<T>( ref T obj, T value, [CallerMemberName] string propertyName = "" )
        {
            obj = value;
            OnPropertyChanged( propertyName );
        }

        protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
        {
            PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
        }
    }
}