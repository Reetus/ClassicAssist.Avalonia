using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UO.Data;

namespace ClassicAssist.Data.Dress;

public class DressAgentItem : INotifyPropertyChanged
{
    public DressAgentItem()
    {
        PropertyChanged += ( sender, args ) =>
        {
            // Set the name when the Type changes...
            if ( args.PropertyName == nameof( Type ) )
            {
                Name = Type == DressAgentItemType.ID
                    ? $"{Layer}: {Strings.Type}: 0x{ID:x4}"
                    : $"{Layer}: 0x{Serial:x8}";
            }
        };
    }

    public int ID
    {
        get;
        set => SetProperty( ref field, value );
    } = -1;

    public Layer Layer
    {
        get;
        set => SetProperty( ref field, value );
    }

    public string Name
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int Serial
    {
        get;
        set => SetProperty( ref field, value );
    }

    public DressAgentItemType Type
    {
        get;
        set => SetProperty( ref field, value );
    } = DressAgentItemType.Serial;

    public event PropertyChangedEventHandler PropertyChanged;

    // ReSharper disable once RedundantAssignment
    public void SetProperty<T>( ref T field, T value, [CallerMemberName] string propertyName = null )
    {
        field = value;
        OnPropertyChanged( propertyName );
    }

    public override string ToString()
    {
        return Name;
    }

    protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
    {
        PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
    }
}