using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
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
///     <para>
///         This file is also linked into ClassicAssist.Updater (see its csproj), so nothing in it
///         may reference launcher-only types - error reporting is limited to tracing.
///     </para>
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

/// <summary>
///     Same policy as the main app's CommandExceptionPolicy: ordinary command failures are logged
///     and swallowed so a bad shard address or a failing Process.Start can't take the process down,
///     while corrupted-state and resource-exhaustion faults are allowed to terminate it.
///     <see cref="StackOverflowException" /> is not listed because on .NET it cannot be caught at
///     all. Deliberately self-contained: this file is linked into ClassicAssist.Updater.
/// </summary>
internal static class CommandExceptionPolicy
{
    public static bool IsFatal( Exception exception )
    {
        return exception is OutOfMemoryException or AccessViolationException or AppDomainUnloadedException
            or CannotUnloadAppDomainException;
    }

    public static void Report( Exception exception )
    {
        // Trace rather than Debug so the report survives release builds. There is no dialog to
        // show from here - this file is shared with the updater - so command handlers that can
        // report a failure themselves (e.g. MainViewModel.Start) still show their own message.
        Trace.WriteLine( $"Unhandled exception in command: {exception}" );
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

    /// <summary>
    ///     Guarded like RelayCommandAsync.Execute: a throw here is invoked from UI event handlers
    ///     and button bindings that nothing up the chain catches, and neither this app nor the
    ///     updater installs a global dispatcher handler - it would terminate the process.
    /// </summary>
    public override void Execute( object parameter )
    {
        try
        {
            _execute( parameter );
        }
        catch ( Exception e ) when ( !CommandExceptionPolicy.IsFatal( e ) )
        {
            CommandExceptionPolicy.Report( e );
        }
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

    /// <summary>
    ///     ICommand.Execute is async void, so anything the command throws has no task to observe it
    ///     and terminated the process - MainViewModel.Start (Process.Start -> Win32Exception),
    ///     Utility.ResolveAddress (Dns.GetHostEntryAsync -> SocketException) and CheckPresets' JSON
    ///     parse were the concrete crash sources. Non-fatal failures are logged instead; fatal
    ///     faults (see CommandExceptionPolicy) are rethrown and still terminate.
    /// </summary>
    public override async void Execute( object parameter )
    {
        try
        {
            await _execute( parameter );
        }
        catch ( Exception e ) when ( !CommandExceptionPolicy.IsFatal( e ) )
        {
            CommandExceptionPolicy.Report( e );
        }
    }
}
