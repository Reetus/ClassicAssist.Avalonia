using System.Windows.Input;
using ClassicAssist.Shared;
using ClassicAssist.Data;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.Misc;

namespace ClassicAssist.UI.ViewModels;

public class MainWindowViewModel : BaseViewModel
{

    //TODO UI
    //private DebugWindow _debugWindow;

    public MainWindowViewModel()
    {
        Engine.UpdateWindowTitleEvent += OnUpdateWindowTitleEvent;
        AssistantOptions.ProfileChangedEvent += _ => OnPropertyChanged( nameof( CurrentOptions ) );
    }

    public Options CurrentOptions => Options.CurrentOptions;

    [OptionsBinding( Property = "AlwaysOnTop" )]
    public bool AlwaysOnTop
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand DebugCommand => field ??= new RelayCommand( ShowDebugWindow, o => true );

    public string Status
    {
        get;
        set => SetProperty( ref field, value );
    } = Strings.Ready___;

    public string Title
    {
        get;
        set => SetProperty( ref field, value );
    } = Strings.ProductName;

    private void OnUpdateWindowTitleEvent()
    {
        Title = string.IsNullOrEmpty( Engine.Player?.Name )
            ? Strings.ProductName
            : $"{Engine.Player?.Name} - {( Options.CurrentOptions.ShowProfileNameWindowTitle ? $"({Options.CurrentOptions.Name}) - " : "" )}{Strings.ProductName}";
    }

    private static void ShowDebugWindow( object obj )
    {
        Engine.UIInvoker.Invoke( "DebugWindow" );
    }
}