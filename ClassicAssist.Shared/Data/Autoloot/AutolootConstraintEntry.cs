using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClassicAssist.Data.Autoloot;

public class AutolootConstraintEntry : INotifyPropertyChanged
{
    /// <summary>Free-text argument for Predicate constraints that need more than an int (e.g. a
    /// substring or an Organizer profile name).</summary>
    public string Additional
    {
        get;
        set => SetProperty( ref field, value );
    }

    /// <summary>
    ///     Lets a row be excluded from evaluation without removing it - only meaningful where a caller
    ///     checks it (currently just the ECV filter's <c>ApplyFilter</c>/<c>ApplyCollectionChange</c>).
    ///     Autoloot's own conditions don't currently expose a way to toggle this, so it has no effect
    ///     there. Ported from WPF's ECV-only <c>EntityCollectionFilterItem.Enabled</c>.
    /// </summary>
    public bool Enabled
    {
        get;
        set => SetProperty( ref field, value );
    } = true;

    public AutolootOperator Operator
    {
        get;
        set => SetProperty( ref field, value );
    } = AutolootOperator.Equal;

    public PropertyEntry Property
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int Value
    {
        get;
        set => SetProperty( ref field, value );
    }

    /// <summary>Multi-value set for <see cref="PropertyEntry.UseMultipleValues" /> constraints
    /// (e.g. ID (Multiple) / Cliloc (Multiple)).</summary>
    public ObservableCollection<int> Values
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