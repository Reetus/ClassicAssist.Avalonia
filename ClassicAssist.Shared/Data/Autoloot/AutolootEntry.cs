using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClassicAssist.UI.Misc.DraggableTreeView;

namespace ClassicAssist.Data.Autoloot
{
    public class AutolootEntry : INotifyPropertyChanged, IDraggableEntry
    {
        private bool _autoloot = true;

        private ObservableCollection<AutolootConstraintEntry> _constraints =
            new ObservableCollection<AutolootConstraintEntry>();

        private bool _enabled = true;
        private AutolootGroup _group;
        private int _id;
        private string _name;
        private AutolootPriority _priority = AutolootPriority.Normal;
        private bool _rehue;
        private int _rehueHue = 1153;

        public bool Autoloot
        {
            get => _autoloot;
            set => SetProperty( ref _autoloot, value );
        }

        public ObservableCollection<AutolootConstraintEntry> Constraints
        {
            get => _constraints;
            set => SetProperty( ref _constraints, value );
        }

        public bool Enabled
        {
            get => _enabled;
            set => SetProperty( ref _enabled, value );
        }

        public AutolootGroup Group
        {
            get => _group;
            set => SetProperty( ref _group, value );
        }

        public int ID
        {
            get => _id;
            set
            {
                SetProperty( ref _id, value );
                OnPropertyChanged( nameof( DisplayName ) );
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                SetProperty( ref _name, value );
                OnPropertyChanged( nameof( DisplayName ) );
            }
        }

        /// <summary>
        ///     "Name - 0x{id:x}" shown in the autoloot tree (WPF parity). The Any entry (-1) renders as
        ///     0xffff, matching WPF's "Match Any ID".
        /// </summary>
        public string DisplayName => $"{Name} - 0x{( ID & 0xFFFF ).ToString( "x" )}";

        public AutolootPriority Priority
        {
            get => _priority;
            set => SetProperty( ref _priority, value );
        }

        public bool Rehue
        {
            get => _rehue;
            set => SetProperty( ref _rehue, value );
        }

        public int RehueHue
        {
            get => _rehueHue;
            set => SetProperty( ref _rehueHue, value );
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public override string ToString()
        {
            return $"{Name} - 0x{ID:x}";
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