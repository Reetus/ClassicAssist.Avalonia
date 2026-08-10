using System.Collections.ObjectModel;
using ClassicAssist.Data.SpecialMoves;
using ClassicAssist.UI.ViewModels;
using Microsoft.Scripting.Utils;

namespace ClassicAssist.Shared.UI.ViewModels.Debug;

public class DebugSpecialMovesViewModel : BaseViewModel
{
    private readonly SpecialMovesManager _manager;

    public DebugSpecialMovesViewModel()
    {
        _manager = SpecialMovesManager.GetInstance();

        Items.AddRange( _manager.GetEnabledNames() );

        _manager.SpecialMovesChanged += OnSpecialMovesChanged;
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