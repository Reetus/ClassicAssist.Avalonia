using System.Collections.ObjectModel;
using ClassicAssist.Data.BuffIcons;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Network;
using Microsoft.Scripting.Utils;

namespace ClassicAssist.Shared.UI.ViewModels.Debug;

public class DebugBuffIconsViewModel : BaseViewModel
{
    private readonly BuffIconManager _manager;

    public DebugBuffIconsViewModel()
    {
        _manager = BuffIconManager.GetInstance();

        Items.AddRange( _manager.GetEnabledNames() );

        IncomingPacketHandlers.BufficonEnabledDisabledEvent += OnBufficonEnabledDisabledEvent;
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

        _dispatcher.Invoke( () =>
        {
            Messages.Add( enabled ? $"Enabled: {data.Name}" : $"Disabled: {data?.Name}" );

            Items.Clear();
            Items.AddRange( _manager.GetEnabledNames() );
        } );
    }
}