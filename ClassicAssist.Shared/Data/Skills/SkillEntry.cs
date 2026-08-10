using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClassicAssist.UO.Data;

namespace ClassicAssist.Data.Skills;

public class SkillEntry : INotifyPropertyChanged
{
    private Skill _skill;

    public float Base
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    public float Cap
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    public double Delta
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    public LockStatus LockStatus
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    public Skill Skill
    {
        get => _skill;
        set
        {
            _skill = value;
            OnPropertyChanged();
        }
    }

    public float Value
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
    {
        PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
    }
}