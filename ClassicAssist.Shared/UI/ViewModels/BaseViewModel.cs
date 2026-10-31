#region License

// Copyright (C) 2020 Reetus
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.UI.Misc;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.UI.ViewModels;

public class BaseViewModel : ObservableObject
{
    /// <summary>
    ///     Every view model ever constructed, in construction order, held weakly. Nothing here
    ///     roots a view model: a closed dialog or window's view model is collected along with
    ///     everything it references. Enumerated by <see cref="Instances" /> for profile save/load;
    ///     mutated only under <see cref="_stateLock" /> because constructors run on the UI thread
    ///     while Options.Save/Load are reached from RPC threads (MCP tool handlers, shutdown).
    /// </summary>
    private static readonly List<WeakReference<BaseViewModel>> _viewModels = [];

    /// <summary>
    ///     The newest <see cref="ISettingProvider" /> instance per concrete view model type, held
    ///     strongly. This decouples settings persistence from view model liveness: a closed Debug
    ///     window's <c>DebugViewModel</c> still serialises into every later profile save (so the
    ///     config round-trips exactly as it did when <see cref="_viewModels" /> held everything
    ///     strongly), while every non-provider view model and every superseded provider is only
    ///     weakly referenced and can be collected.
    ///     <para>
    ///         Why one instance per type is sufficient: each provider type writes a fixed set of
    ///         top-level profile keys ("General", "Options", "Hotkeys", "Macros", "Packets", ...)
    ///         and instances serialise in construction order with last-writer-wins per key, so the
    ///         newest instance's values are what lands in the profile regardless of how many older
    ///         instances exist - identical output to the old pin-everything list. The retained set
    ///         is therefore bounded by the number of provider types (~15), not by how many windows
    ///         or dialogs have been opened.
    ///     </para>
    /// </summary>
    private static readonly Dictionary<Type, BaseViewModel> _retainedSettingProviders = [];

    private static readonly Lock _stateLock = new();

    private static Options _trackedOptions;

    /// <summary>
    ///     Command-typed properties per view model type, cached so that the CanExecute sweep on every
    ///     property change doesn't re-reflect.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _commandProperties =
        new();

    protected IDispatcher _dispatcher;

    static BaseViewModel()
    {
        // Options.CurrentOptions is REPLACED on every profile change (AssistantOptions.PlayerInitialized,
        // GeneralControlViewModel, NewProfileViewModel) and each of those sites loads the new profile
        // immediately afterwards, so Options.Load is the one hook that fires after every swap.
        // TrackCurrentOptions then re-points the shared PropertyChanged subscription at the new
        // instance; without this, [OptionsBinding] properties (e.g. MainWindowViewModel.AlwaysOnTop,
        // bound as Topmost) stopped tracking after a linked-profile switch and every replaced Options
        // instance stayed rooted by its accumulated handlers.
        Options.LoadEvent += OnOptionsLoaded;

        TrackCurrentOptions();
    }

    public BaseViewModel()
    {
        lock ( _stateLock )
        {
            _viewModels.Add( new WeakReference<BaseViewModel>( this ) );

            if ( this is ISettingProvider )
            {
                _retainedSettingProviders[GetType()] = this;
            }
        }

        _dispatcher = Engine.Dispatcher;

        PropertyChanged += OnPropertyChangedRefreshCommands;
    }

    /// <summary>
    ///     Live view models in construction order, then any retained setting provider whose view
    ///     model has already been collected (appended last so that, for a given settings key, the
    ///     newest instance still writes last). Dead entries are pruned as a side effect, and the
    ///     snapshot is taken under the lock so concurrent registration can't be observed mid-mutation
    ///     by save/load running on another thread.
    /// </summary>
    public static BaseViewModel[] Instances
    {
        get
        {
            TrackCurrentOptions();

            lock ( _stateLock )
            {
                List<BaseViewModel> instances = [];
                HashSet<BaseViewModel> seen = new( ReferenceEqualityComparer.Instance );

                foreach ( WeakReference<BaseViewModel> reference in _viewModels )
                {
                    if ( reference.TryGetTarget( out BaseViewModel instance ) && seen.Add( instance ) )
                    {
                        instances.Add( instance );
                    }
                }

                foreach ( BaseViewModel retained in _retainedSettingProviders.Values )
                {
                    if ( seen.Add( retained ) )
                    {
                        instances.Add( retained );
                    }
                }

                _viewModels.RemoveAll( reference => !reference.TryGetTarget( out _ ) );

                return [.. instances];
            }
        }
    }

    public new virtual void SetProperty<T>( ref T obj, T value, [CallerMemberName] string propertyName = "" )
    {
        base.SetProperty( ref obj, value, propertyName );
    }

    protected void NotifyPropertyChanged( [CallerMemberName] string propertyName = "" )
    {
        OnPropertyChanged( propertyName );
    }

    /// <summary>
    ///     Raises change notifications on the UI thread.
    ///     <para>
    ///         These view models were written against WPF, which quietly marshals a property change
    ///         raised on a background thread over to the dispatcher. Avalonia does not - it throws
    ///         "Call from invalid thread". That matters because nearly everything here is driven by
    ///         inbound RPC from the plugin, which StreamJsonRpc dispatches on thread pool threads, so
    ///         without this every packet and connection notification dies in a swallowed exception.
    ///     </para>
    /// </summary>
    protected override void OnPropertyChanged( PropertyChangedEventArgs e )
    {
        IDispatcher dispatcher = _dispatcher ?? Engine.Dispatcher;

        if ( dispatcher != null && !dispatcher.CheckAccess() )
        {
            // Post rather than block: the caller may be a packet handler the game thread is waiting on.
            dispatcher.Invoke( () => base.OnPropertyChanged( e ) );

            return;
        }

        base.OnPropertyChanged( e );
    }

    /// <summary>
    ///     WPF re-queried CanExecute automatically via CommandManager.RequerySuggested; Avalonia has no
    ///     equivalent, so commands are invalidated here whenever the view model changes.
    /// </summary>
    private void OnPropertyChangedRefreshCommands( object sender, PropertyChangedEventArgs e )
    {
        PropertyInfo[] properties = _commandProperties.GetOrAdd( GetType(),
            type => Array.FindAll( type.GetProperties( BindingFlags.Public | BindingFlags.Instance ),
                p => typeof( ICommand ).IsAssignableFrom( p.PropertyType ) && p.GetIndexParameters().Length == 0 ) );

        foreach ( PropertyInfo property in properties )
        {
            switch ( property.GetValue( this ) )
            {
                case RelayCommand command:
                    command.RaiseCanExecuteChanged();

                    break;
                case RelayCommandAsync command:
                    command.RaiseCanExecuteChanged();

                    break;
            }
        }
    }

    protected void OnOptionChanged( object sender, PropertyChangedEventArgs e )
    {
        PropertyInfo[] properties = GetType().GetProperties( BindingFlags.Public | BindingFlags.Instance );

        foreach ( PropertyInfo property in properties )
        {
            OptionsBindingAttribute attr = property.GetCustomAttribute<OptionsBindingAttribute>();

            if ( attr == null || attr.Property != e.PropertyName )
            {
                continue;
            }

            if ( sender is not Options options )
            {
                continue;
            }

            PropertyInfo optionsProperty = options.GetType().GetProperty( e.PropertyName );

            if ( optionsProperty == null )
            {
                continue;
            }

            object propertyValue = optionsProperty.GetValue( Options.CurrentOptions );

            property.SetValue( this, propertyValue );
        }
    }

    /// <summary>
    ///     Re-points the shared <see cref="Options.CurrentOptions" /> subscription and refreshes
    ///     every [OptionsBinding] property on live view models. The refresh matters because the
    ///     new Options instance is populated while the load event is being raised - providers'
    ///     Deserialize assigns Options properties such as AlwaysOnTop - and those changes can be
    ///     raised before this handler re-subscribes (event handlers run in subscription order).
    ///     Pushing the final values afterwards covers both orders.
    /// </summary>
    private static void OnOptionsLoaded( JObject json, Options options )
    {
        TrackCurrentOptions();

        foreach ( BaseViewModel instance in Instances )
        {
            instance.SyncOptionsBindings();
        }
    }

    private static void OnTrackedOptionChanged( object sender, PropertyChangedEventArgs e )
    {
        // Cheap guard for swaps that don't go through Options.Load (test fixtures assign
        // Options.CurrentOptions directly); a no-op when nothing changed.
        TrackCurrentOptions();

        foreach ( BaseViewModel instance in Instances )
        {
            instance.OnOptionChanged( sender, e );
        }
    }

    private static void TrackCurrentOptions()
    {
        lock ( _stateLock )
        {
            Options current = Options.CurrentOptions;

            if ( ReferenceEquals( current, _trackedOptions ) )
            {
                return;
            }

            _trackedOptions?.PropertyChanged -= OnTrackedOptionChanged;

            _trackedOptions = current;

            current?.PropertyChanged += OnTrackedOptionChanged;
        }
    }

    private void SyncOptionsBindings()
    {
        PropertyInfo[] properties = GetType().GetProperties( BindingFlags.Public | BindingFlags.Instance );

        foreach ( PropertyInfo property in properties )
        {
            OptionsBindingAttribute attr = property.GetCustomAttribute<OptionsBindingAttribute>();

            if ( attr == null )
            {
                continue;
            }

            PropertyInfo optionsProperty = typeof( Options ).GetProperty( attr.Property );

            if ( optionsProperty == null )
            {
                continue;
            }

            property.SetValue( this, optionsProperty.GetValue( Options.CurrentOptions ) );
        }
    }
}

/// <summary>
///     Shared policy for exceptions escaping an <see cref="ICommand" /> implementation or the UI
///     dispatcher: which ones are logged and swallowed (the process keeps running) and which ones
///     are allowed to terminate it.
///     <para>
///         Swallowed: ordinary command failures - a shard that won't resolve, a client that won't
///         start, a malformed JSON preset - where the state left behind is just a half-finished user
///         action. Let through: <see cref="OutOfMemoryException" />, <see cref="AccessViolationException" />,
///         <see cref="AppDomainUnloadedException" /> and <see cref="CannotUnloadAppDomainException" /> -
///         corrupted-state and resource-exhaustion faults where continuing would run the process on
///         state that can no longer be trusted. <see cref="StackOverflowException" /> is not listed
///         because on .NET it cannot be caught at all; the runtime terminates immediately.
///     </para>
/// </summary>
public static class CommandExceptionPolicy
{
    public static bool IsFatal( Exception exception )
    {
        return exception is OutOfMemoryException or AccessViolationException or AppDomainUnloadedException
            or CannotUnloadAppDomainException;
    }

    /// <summary>
    ///     Logs a failed command and shows a best-effort user-visible error. The dialog is never
    ///     awaited - ICommand.Execute is async void and must not block on a message pump - and is
    ///     never allowed to throw, so a failure to display it can't become a second crash.
    /// </summary>
    public static void Report( Exception exception )
    {
        Console.WriteLine( $"Unhandled exception in command: {exception}" );

        try
        {
            _ = Engine.MessageBoxProvider?.Show( exception.Message, "Error", MessageBoxButtons.OK,
                MessageBoxImage.Error );
        }
        catch ( Exception )
        {
            // Already logged above.
        }
    }
}

public class RelayCommandAsync : ICommand
{
    private readonly Func<object, bool> canExecuteMethod;
    private readonly Func<object, Task> executedMethod;

    public RelayCommandAsync( Func<object, Task> execute, Func<object, bool> canExecute )
    {
        executedMethod = execute ?? throw new ArgumentNullException( nameof( execute ) );
        canExecuteMethod = canExecute;
    }

    public event EventHandler CanExecuteChanged
    {
        add => CanExecuteChangedInternal += value;
        remove => CanExecuteChangedInternal -= value;
    }

    public bool CanExecute( object parameter )
    {
        return canExecuteMethod == null || canExecuteMethod( parameter );
    }

    /// <summary>
    ///     ICommand.Execute is async void, so anything the command throws escapes to the caller's
    ///     context with no task to observe it - on a UI thread that means the dispatcher, off it a
    ///     thread-pool thread - and until the global handlers in App.axaml.cs were added either one
    ///     terminated the process (Process.Start -> Win32Exception, Dns.GetHostEntryAsync ->
    ///     SocketException, a JSON parse failure in CheckPresets). Non-fatal failures are logged and
    ///     reported to the user instead; fatal faults (see CommandExceptionPolicy) are rethrown and
    ///     still take the process down.
    /// </summary>
    public async void Execute( object parameter )
    {
        try
        {
            await executedMethod( parameter );
        }
        catch ( Exception e ) when ( !CommandExceptionPolicy.IsFatal( e ) )
        {
            CommandExceptionPolicy.Report( e );
        }
    }

    private event EventHandler CanExecuteChangedInternal;

    // ReSharper disable once UnusedMember.Global
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChangedInternal?.Invoke( this, EventArgs.Empty );
    }
}

public class RelayCommand : ICommand
{
    private readonly Func<object, bool> _canExecute;
    private readonly Action<object> _execute;

    public RelayCommand( Action<object> execute, Func<object, bool> canExecute = null )
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler CanExecuteChanged
    {
        add => CanExecuteChangedInternal += value;
        remove => CanExecuteChangedInternal -= value;
    }

    public bool CanExecute( object parameter )
    {
        return _canExecute == null || _canExecute( parameter );
    }

    /// <summary>
    ///     Guarded like <see cref="RelayCommandAsync.Execute" />: a synchronous throw on the UI
    ///     thread would otherwise reach the dispatcher unhandled, and one on any other thread
    ///     (command handlers run wherever they are invoked) terminated the process outright.
    /// </summary>
    public void Execute( object parameter )
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

    private event EventHandler CanExecuteChangedInternal;

    // ReSharper disable once UnusedMember.Global
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChangedInternal?.Invoke( this, EventArgs.Empty );
    }
}
