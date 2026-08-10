using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using ClassicAssist.UI.Misc;
using Newtonsoft.Json;

namespace ClassicAssist.Data.Hotkeys;

public abstract class HotkeyEntry : INotifyPropertyChanged, IComparable<HotkeyEntry>
{
    public delegate void HotkeyChangedEventHandler( object sender, HotkeyChangedEventArgs e );

    private readonly Lock _childrenLock = new();
    private string _name;

    [JsonIgnore]
    public Action<HotkeyEntry, object[]> Action { get; set; }

    public bool CanGlobal
    {
        get;
        set => SetProperty( ref field, value );
    } = true;

    [JsonIgnore]
    public ObservableCollectionEx<HotkeyEntry> Children
    {
        get
        {
            lock ( _childrenLock )
            {
                return field;
            }
        }
        set
        {
            lock ( _childrenLock )
            {
                SetProperty( ref field, value );
            }
        }
    } = [];

    /// <summary>
    ///     Whether this entry has [HotkeyConfiguration] properties worth showing an Options dialog for.
    ///     Overridden to true by the commands that have them; gates the Options button on the tab.
    /// </summary>
    public virtual bool Configurable { get; set; } = false;

    public virtual bool Disableable { get; set; } = true;

    public ShortcutKeys Hotkey
    {
        get;
        set
        {
            if ( !Equals( value, ShortcutKeys.Default ) )
            {
                HotkeyManager manager = HotkeyManager.GetInstance();
                manager.ClearPreviousHotkey( value );
            }

            SetProperty( ref field, value );
            OnPropertyChanged( nameof( Image ) );
            HotkeyChanged?.Invoke( this, new HotkeyChangedEventArgs( field, value ) );
        }
    } = new ShortcutKeys();

    [JsonIgnore]
    public string Image => Equals( Hotkey, ShortcutKeys.Default ) ? "red-circle.png" : "green-circle.png";

    public bool IsCategory
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool IsGlobal
    {
        get;
        set => SetProperty( ref field, value );
    }

    public virtual string Name
    {
        get => _name;
        set => SetProperty( ref _name, value );
    }

    public bool PassToUO
    {
        get;
        set => SetProperty( ref field, value );
    } = true;

    public int CompareTo( HotkeyEntry other )
    {
        if ( ReferenceEquals( this, other ) )
        {
            return 0;
        }

        if ( other is null )
        {
            return 1;
        }

        int isCategoryComparison = IsCategory.CompareTo( other.IsCategory );

        if ( isCategoryComparison != 0 )
        {
            return isCategoryComparison;
        }

        return string.Compare( _name, other._name, StringComparison.Ordinal );
    }

    public event HotkeyChangedEventHandler HotkeyChanged;

    public override string ToString()
    {
        return Name;
    }

    #region INotifyPropertyChanged

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

    #endregion
}