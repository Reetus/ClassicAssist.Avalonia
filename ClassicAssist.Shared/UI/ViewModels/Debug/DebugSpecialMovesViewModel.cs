using System;
using System.Collections.ObjectModel;
using ClassicAssist.Data.SpecialMoves;
using ClassicAssist.UI.ViewModels;
using Microsoft.Scripting.Utils;

namespace ClassicAssist.Shared.UI.ViewModels.Debug;

public class DebugSpecialMovesViewModel : BaseViewModel, IDisposable
{
    private readonly SpecialMovesManager _manager;

    /// <summary>Guards <see cref="Dispose" /> so the teardown below can only ever run once.</summary>
    private bool _disposed;

    public DebugSpecialMovesViewModel()
    {
        _manager = SpecialMovesManager.GetInstance();

        Items.AddRange( _manager.GetEnabledNames() );

        _manager.SpecialMovesChanged += OnSpecialMovesChanged;
    }

    /// <summary>
    ///     Unsubscribes the special-moves notifications for this window and drops the collected
    ///     state. Called once from <c>DebugWindow.OnClosed</c>.
    /// </summary>
    public void Dispose()
    {
        if ( _disposed )
        {
            return;
        }

        _disposed = true;

        _manager.SpecialMovesChanged -= OnSpecialMovesChanged;
        Items.Clear();
        Messages.Clear();
    }

    public ObservableCollection<string> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ObservableCollection<string> Messages
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public string SelectedItem
    {
        get;
        set => SetProperty( ref field, value );
    }

    private void OnSpecialMovesChanged( string name, bool enabled )
    {
        _dispatcher.Invoke( () =>
        {
            Messages.Add( enabled ? $"Enabled: {name}" : $"Disabled: {name}" );

            Items.Clear();
            Items.AddRange( _manager.GetEnabledNames() );
        } );
    }
}