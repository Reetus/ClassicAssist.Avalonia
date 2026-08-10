using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using ClassicAssist.UI.Misc;

namespace ClassicAssist.Data.Skills;

public class SkillManager : INotifyPropertyChanged
{
    private static readonly Lock _lock = new();
    private static SkillManager _instance;

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
        // ReSharper disable once InvertIf
        if ( _instance == null )
        {
            lock ( _lock )
            {
                if ( _instance != null )
                {
                    return _instance;
                }

                _instance = new SkillManager();
                return _instance;
            }
        }

        return _instance;
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