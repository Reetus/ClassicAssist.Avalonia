using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClassicAssist.UI.Misc.DraggableTreeView;

namespace ClassicAssist.Data.Autoloot;

public class AutolootEntry : INotifyPropertyChanged, IDraggableEntry
{
    public bool Autoloot
    {
        get;
        set => SetProperty( ref field, value );
    } = true;

    public ObservableCollection<AutolootConstraintEntry> Constraints
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public bool Enabled
    {
        get;
        set => SetProperty( ref field, value );
    } = true;

    public AutolootGroup Group
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int ID
    {
        get;
        set
        {
            SetProperty( ref field, value );
            OnPropertyChanged( nameof( DisplayName ) );
        }
    }

    public string Name
    {
        get;
        set
        {
            SetProperty( ref field, value );
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
        get;
        set => SetProperty( ref field, value );
    } = AutolootPriority.Normal;

    public bool Rehue
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int RehueHue
    {
        get;
        set => SetProperty( ref field, value );
    } = 1153;

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