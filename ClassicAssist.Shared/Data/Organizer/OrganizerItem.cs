using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClassicAssist.Data.Organizer;

public class OrganizerItem : INotifyPropertyChanged
{
    public int Amount
    {
        get;
        set => SetProperty( ref field, value );
    }

    /// <summary>Per-item destination container override. When set it wins over the entry-level
    /// <see cref="OrganizerEntry.DestinationContainer" /> for this item.</summary>
    public int? DestinationContainer
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int Hue
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int ID
    {
        get;
        set => SetProperty( ref field, value );
    }

    public string Item
    {
        get;
        set => SetProperty( ref field, value );
    }

    /// <summary>Per-item source container override. When set it wins over the entry-level
    /// <see cref="OrganizerEntry.SourceContainer" /> for this item.</summary>
    public int? SourceContainer
    {
        get;
        set => SetProperty( ref field, value );
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