using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClassicAssist.UI.Misc;

namespace ClassicAssist.Data.Skills;

public class SkillManager : INotifyPropertyChanged
{
    private static readonly Lazy<SkillManager> _instance = new( () => new SkillManager() );

    private SkillManager()
    {
    }

    public ObservableCollectionEx<SkillEntry> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public event PropertyChangedEventHandler PropertyChanged;

    public static SkillManager GetInstance()
    {
        return _instance.Value;
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