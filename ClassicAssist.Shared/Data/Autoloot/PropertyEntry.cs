using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json;

namespace ClassicAssist.Data.Autoloot
{
    public class PropertyEntry : INotifyPropertyChanged, IComparable<PropertyEntry>
    {
        private AutolootAllowedOperators _allowedOperators;
        private Type _allowedValuesEnum;
        private int _clilocIndex;
        private int[] _clilocs;
        private PropertyType _constraintType;
        private string _name;
        private ObservableCollection<string> _options;
        private Func<Entity, AutolootConstraintEntry, bool> _predicate;
        private string _shortName;
        private bool _useMultipleValues;

        public AutolootAllowedOperators AllowedOperators
        {
            get => _allowedOperators;
            set => SetProperty( ref _allowedOperators, value );
        }

        /// <summary>
        ///     When set to an enum whose values are stored in <see cref="AutolootConstraintEntry.Value" />
        ///     (<see cref="Layer" />, <see cref="TileFlags" />), the Value editor is a ComboBox over it.
        ///     <see cref="SkillBonusSkills" /> is the exception: it names the skill, which lives in
        ///     Additional, so Value stays the numeric bonus and the skill gets its own selector.
        /// </summary>
        [JsonIgnore]
        public Type AllowedValuesEnum
        {
            get => _allowedValuesEnum;
            set => SetProperty( ref _allowedValuesEnum, value );
        }

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
        ///     Fixed set of values the <see cref="AutolootConstraintEntry.Additional" /> editor offers as a
        ///     ComboBox (e.g. organizer profile names). Null means Additional is edited as free text, and a
        ///     constraint that ignores Additional entirely shows the numeric Value editor instead.
        /// </summary>
        public ObservableCollection<string> Options
        {
            get => _options;
            set => SetProperty( ref _options, value );
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
        ///     When true, the Value editor is a multi-value selector (a set of item IDs or clilocs) backed
        ///     by <see cref="AutolootConstraintEntry.Values" /> rather than a single int.
        /// </summary>
        public bool UseMultipleValues
        {
            get => _useMultipleValues;
            set => SetProperty( ref _useMultipleValues, value );
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