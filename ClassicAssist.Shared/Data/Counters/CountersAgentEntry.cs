using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ClassicAssist.Shared;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.Data.Counters;

public class CountersAgentEntry : INotifyPropertyChanged
{
    public int Color
    {
        get;
        set
        {
            SetProperty( ref field, value );
            Recount();
        }
    }

    public int Count
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int Graphic { get; set; }
    public string Name { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;

    public void Recount()
    {
        if ( Engine.Player == null )
        {
            return;
        }

        if ( Engine.Player.Backpack?.Container == null )
        {
            Count = 0;
            return;
        }

        Item[] matches =
            Engine.Player.Backpack.Container.SelectEntities( i =>
                i.ID == Graphic && ( Color == -1 || Color == i.Hue ) );

        Count = matches?.Sum( i => i.Count ) ?? 0;
    }

    protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
    {
        PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
    }

    public void SetProperty<T>( ref T obj, T value, [CallerMemberName] string propertyName = "" )
    {
        obj = value;
        OnPropertyChanged( propertyName );
    }
}