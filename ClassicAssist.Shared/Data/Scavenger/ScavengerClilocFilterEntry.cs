using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClassicAssist.UO.Data;

namespace ClassicAssist.Data.Scavenger
{
    /// <summary>
    ///     A cliloc whose properties are excluded from scavenging when the scavenger's cliloc filter is
    ///     enabled (e.g. insured/first-chance items).
    /// </summary>
    public class ScavengerClilocFilterEntry : INotifyPropertyChanged
    {
        private int _cliloc;

        public int Cliloc
        {
            get => _cliloc;
            set
            {
                _cliloc = value;
                OnPropertyChanged( nameof( Property ) );
            }
        }

        public bool Enabled { get; set; }

        public string Property => ClassicAssist.UO.Data.Cliloc.GetProperty( Cliloc );

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
        {
            PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
        }
    }
}
