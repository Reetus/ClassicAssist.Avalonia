using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json;

namespace ClassicAssist.Data.Autoloot
{
    public class PropertyEntry : INotifyPropertyChanged, IComparable<PropertyEntry>
    {
        private int _clilocIndex;
        private int[] _clilocs;
        private PropertyType _constraintType;
        private string _name;
        private Func<Entity, AutolootConstraintEntry, bool> _predicate;
        private string _shortName;

        public int ClilocIndex
        {
            get => _clilocIndex;
            set => SetProperty( ref _clilocIndex, value );
        }

        public int[] Clilocs
        {
            get => _clilocs;
            set => SetProperty( ref _clilocs, value );
        }

        public PropertyType ConstraintType
        {
            get => _constraintType;
            set => SetProperty( ref _constraintType, value );
        }

        public string Name
        {
            get => _name;
            set => SetProperty( ref _name, value );
        }

        /// <summary>
        ///     Abbreviated name used to match CSV-import columns against the property (e.g. "LMC" for
        ///     "Lower Mana Cost").
        /// </summary>
        public string ShortName
        {
            get => _shortName;
            set => SetProperty( ref _shortName, value );
        }

        /// <summary>
        ///     Evaluates a <see cref="PropertyType.Predicate" />/<see cref="PropertyType.PredicateWithValue" />
        ///     constraint - arbitrary item logic that doesn't fit the cliloc-property or reflected-object-
        ///     property shapes the other constraint types cover (e.g. distance, tile flags).
        /// </summary>
        [JsonIgnore]
        public Func<Entity, AutolootConstraintEntry, bool> Predicate
        {
            get => _predicate;
            set => SetProperty( ref _predicate, value );
        }

        public int CompareTo( PropertyEntry other )
        {
            if ( ReferenceEquals( this, other ) )
            {
                return 0;
            }

            if ( ReferenceEquals( null, other ) )
            {
                return 1;
            }

            int nameComparison = string.Compare( _name, other._name, StringComparison.InvariantCultureIgnoreCase );

            return nameComparison;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public override string ToString()
        {
            return Name;
        }

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