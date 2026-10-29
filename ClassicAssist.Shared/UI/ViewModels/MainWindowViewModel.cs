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

        // Named rather than a lambda so it can be unsubscribed; the lambda it replaces could never
        // be removed from the static event.
        AssistantOptions.ProfileChangedEvent += OnProfileChangedEvent;
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

    private void OnProfileChangedEvent( string profile )
    {
        OnPropertyChanged( nameof( CurrentOptions ) );
    }

    private static void ShowDebugWindow( object obj )
    {
        Engine.UIInvoker.Invoke( "DebugWindow" );
    }
}