using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClassicAssist.Annotations;

namespace ClassicAssist.Data.Vendors
{
    public class VendorSellAgentEntry : INotifyPropertyChanged
    {
        private int _amount;
        private bool _enabled;
        private int _graphic;
        private int _hue;
        private int _minPrice;
        private string _name;

        public int Amount
        {
            get => _amount;
            set => SetProperty( ref _amount, value );
        }

        public bool Enabled
        {
            get => _enabled;
            set => SetProperty( ref _enabled, value );
        }

        public int Graphic
        {
            get => _graphic;
            set => SetProperty( ref _graphic, value );
        }

        public int Hue
        {
            get => _hue;
            set => SetProperty( ref _hue, value );
        }

        public int MinPrice
        {
            get => _minPrice;
            set => SetProperty( ref _minPrice, value );
        }

        public string Name
        {
            get => _name;
            set => SetProperty( ref _name, value );
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
        {
            PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
        }

        // ReSharper disable once RedundantAssignment
        public virtual void SetProperty<T>( ref T obj, T value, [CallerMemberName] string propertyName = "" )
        {
            obj = value;
            OnPropertyChanged( propertyName );
        }
    }
}