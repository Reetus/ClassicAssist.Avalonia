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
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Shared;
using ClassicAssist.UI.Misc;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassicAssist.UI.ViewModels;

public class BaseViewModel : ObservableObject
{
    private static readonly List<BaseViewModel> _viewModels = [];

    /// <summary>
    ///     Command-typed properties per view model type, cached so that the CanExecute sweep on every
    ///     property change doesn't re-reflect.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _commandProperties =
        new();

    protected IDispatcher _dispatcher;

    public BaseViewModel()
    {
        _viewModels.Add( this );
        _dispatcher = Engine.Dispatcher;

        Options.CurrentOptions.PropertyChanged += OnOptionChanged;
        PropertyChanged += OnPropertyChangedRefreshCommands;
    }

    public static BaseViewModel[] Instances => [.. _viewModels];

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

    public async void Execute( object parameter )
    {
        await executedMethod( parameter );
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

    public void Execute( object parameter )
    {
        _execute( parameter );
    }

    private event EventHandler CanExecuteChangedInternal;

    // ReSharper disable once UnusedMember.Global
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChangedInternal?.Invoke( this, EventArgs.Empty );
    }
}