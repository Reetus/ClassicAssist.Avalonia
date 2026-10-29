using System;
using System.Collections.ObjectModel;
using ClassicAssist.Data.BuffIcons;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Network;
using Microsoft.Scripting.Utils;

namespace ClassicAssist.Shared.UI.ViewModels.Debug;

public class DebugBuffIconsViewModel : BaseViewModel, IDisposable
{
    private readonly BuffIconManager _manager;

    /// <summary>Guards <see cref="Dispose" /> so the teardown below can only ever run once.</summary>
    private bool _disposed;

    public DebugBuffIconsViewModel()
    {
        _manager = BuffIconManager.GetInstance();

        Items.AddRange( _manager.GetEnabledNames() );

        IncomingPacketHandlers.BufficonEnabledDisabledEvent += OnBufficonEnabledDisabledEvent;
    }

    /// <summary>
    ///     Unsubscribes the buff-icon notifications for this window and drops the collected state.
    ///     Called once from <c>DebugWindow.OnClosed</c>.
    /// </summary>
    public void Dispose()
    {
        if ( _disposed )
        {
            return;
        }

        _disposed = true;

        IncomingPacketHandlers.BufficonEnabledDisabledEvent -= OnBufficonEnabledDisabledEvent;
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

    private void OnBufficonEnabledDisabledEvent( int type, bool enabled, int duration )
    {
        BuffIconData data = _manager.GetDataByID( type );
        string name = data?.Name ?? $"Unknown (0x{type:x})";

        _dispatcher.Invoke( () =>
        {
            Messages.Add( enabled ? $"Enabled: {name}" : $"Disabled: {name}" );

            Items.Clear();
            Items.AddRange( _manager.GetEnabledNames() );
        } );
    }
}