using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ClassicAssist.Data.Autoloot
{
    public class AutolootConstraintEntry : INotifyPropertyChanged
    {
        private string _additional;
        private bool _enabled = true;
        private AutolootOperator _operator = AutolootOperator.Equal;
        private PropertyEntry _property;
        private int _value;
        private ObservableCollection<int> _values;

        /// <summary>Free-text argument for Predicate constraints that need more than an int (e.g. a
        /// substring or an Organizer profile name).</summary>
        public string Additional
        {
            get => _additional;
            set => SetProperty( ref _additional, value );
        }

        /// <summary>
        ///     Lets a row be excluded from evaluation without removing it - only meaningful where a caller
        ///     checks it (currently just the ECV filter's <c>ApplyFilter</c>/<c>ApplyCollectionChange</c>).
        ///     Autoloot's own conditions don't currently expose a way to toggle this, so it has no effect
        ///     there. Ported from WPF's ECV-only <c>EntityCollectionFilterItem.Enabled</c>.
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set => SetProperty( ref _enabled, value );
        }

        public AutolootOperator Operator
        {
            get => _operator;
            set => SetProperty( ref _operator, value );
        }

        public PropertyEntry Property
        {
            get => _property;
            set => SetProperty( ref _property, value );
        }

        public int Value
        {
            get => _value;
            set => SetProperty( ref _value, value );
        }

        /// <summary>Multi-value set for <see cref="PropertyEntry.UseMultipleValues" /> constraints
        /// (e.g. ID (Multiple) / Cliloc (Multiple)).</summary>
        public ObservableCollection<int> Values
        {
            get => _values;
            set => SetProperty( ref _values, value );
        }

        public event PropertyChangedEventHandler PropertyChanged;

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