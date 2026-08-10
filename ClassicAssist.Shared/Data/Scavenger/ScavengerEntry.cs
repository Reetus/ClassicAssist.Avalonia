using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClassicAssist.Data.Scavenger;

public class ScavengerEntry : INotifyPropertyChanged
{
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

    public string Name
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ScavengerPriority Priority
    {
        get;
        set => SetProperty( ref field, value );
    } = ScavengerPriority.Normal;

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
    {
        PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
    }

    // ReSharper disable once RedundantAssignment
    public void SetProperty<T>( ref T field, T value, [CallerMemberName] string propertyName = null )
    {
        field = value;
        OnPropertyChanged( propertyName );
    }
}