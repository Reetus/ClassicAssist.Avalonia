using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ClassicAssist.Launcher.ViewModels;

/// <summary>
///     Deliberately not the ClassicAssist.Shared BaseViewModel used by the main Avalonia app: that
///     one is wired into Engine.Dispatcher/ISettingProvider machinery this standalone launcher has
///     no need of. WPF's CommandManager.RequerySuggested has no equivalent here either, so every
///     property change explicitly re-queries every ICommand-typed property's CanExecute instead -
///     otherwise bound buttons would never re-enable themselves.
/// </summary>
public class BaseViewModel : INotifyPropertyChanged
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _commandProperties = new();

    public event PropertyChangedEventHandler PropertyChanged;

    public virtual void SetProperty<T>( ref T obj, T value, [CallerMemberName] string propertyName = "" )
    {
        obj = value;
        NotifyPropertyChanged( propertyName );
    }

    protected void NotifyPropertyChanged( [CallerMemberName] string propertyName = "" )
    {
        PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );

        foreach ( PropertyInfo property in _commandProperties.GetOrAdd( GetType(), FindCommandProperties ) )
        {
            if ( property.GetValue( this ) is RelayCommandBase command )
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    private static PropertyInfo[] FindCommandProperties( Type type )
    {
        return [.. type.GetProperties( BindingFlags.Public | BindingFlags.Instance ).Where( p => typeof( ICommand ).IsAssignableFrom( p.PropertyType ) && p.GetIndexParameters().Length == 0 )];
    }
}

public abstract class RelayCommandBase : ICommand
{
    public event EventHandler CanExecuteChanged;

    public abstract bool CanExecute( object parameter );
    public abstract void Execute( object parameter );

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke( this, EventArgs.Empty );
    }
}

public class RelayCommand : RelayCommandBase
{
    private readonly Func<object, bool> _canExecute;
    private readonly Action<object> _execute;

    public RelayCommand( Action<object> execute, Func<object, bool> canExecute = null )
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute( object parameter )
    {
        return _canExecute == null || _canExecute( parameter );
    }

    public override void Execute( object parameter )
    {
        _execute( parameter );
    }
}

public class RelayCommandAsync : RelayCommandBase
{
    private readonly Func<object, bool> _canExecute;
    private readonly Func<object, Task> _execute;

    public RelayCommandAsync( Func<object, Task> execute, Func<object, bool> canExecute )
    {
        _execute = execute ?? throw new ArgumentNullException( nameof( execute ) );
        _canExecute = canExecute;
    }

    public override bool CanExecute( object parameter )
    {
        return _canExecute == null || _canExecute( parameter );
    }

    public override async void Execute( object parameter )
    {
        await _execute( parameter );
    }
}
