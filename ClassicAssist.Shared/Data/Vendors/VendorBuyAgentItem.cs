using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClassicAssist.Data.Vendors;

public class VendorBuyAgentItem : INotifyPropertyChanged
{
    public int Amount
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int BackpackGraphic
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool Enabled
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int Graphic
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int Hue
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int MaxPrice
    {
        get;
        set => SetProperty( ref field, value );
    }

    public string Name
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool Stackable
    {
        get;
        set => SetProperty( ref field, value );
    }

    public double Weight
    {
        get;
        set => SetProperty( ref field, value );
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