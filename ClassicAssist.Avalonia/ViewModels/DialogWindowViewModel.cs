using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using ClassicAssist.Misc;

namespace ClassicAssist.UI.ViewModels;

public class DialogWindowViewModel : BaseViewModel
{
    private readonly SharedWindow _window;

    public DialogWindowViewModel( SharedWindow window )
    {
        _window = window;
    }

    public ICommand ButtonClick => field ??= new RelayCommand( OnButtonClick, o => true );

    public string Caption => _window.Caption;

    public bool Icon => _window.Icon != null;

    public string ImagePath =>
        _window.Icon == MessageBoxImage.Error ? "dialog-error.png" :
        _window.Icon == MessageBoxImage.Warning ? "dialog-warn.png" :
        _window.Icon == MessageBoxImage.Information ? "dialog-info.png" : null;

    public bool IsNoShowed => _window.Buttons == MessageBoxButtons.YesNo;
    public bool IsOkShowed => _window.Buttons == MessageBoxButtons.OK;
    public bool IsYesShowed => _window.Buttons == MessageBoxButtons.YesNo;
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.No;
    public string Text => _window.Text;
    public int TextWidth => Icon ? 250 : 330;

    private void OnButtonClick( object obj )
    {
        Result = (MessageBoxResult) obj;

        // SharedWindow.Window is a view-agnostic object; guard the cast so a mis-wired host cannot
        // throw out of a button handler.
        if ( _window.Window is Window window )
        {
            window.Close();
        }
    }

    public async Task Copy()
    {
        if ( _window.Window is not Window window )
        {
            return;
        }

        // A null task means the platform has no clipboard - the old ?. chain awaited null and threw
        // NullReferenceException. Failures are observed here for the same reason as in
        // AvaloniaUIInvoker.SetClipboardText: Copy is bound straight to Ctrl+C, so a locked or
        // missing clipboard has no other handler to catch the faulted task.
        Task setText = window.Clipboard?.SetTextAsync( _window.Text );

        if ( setText == null )
        {
            return;
        }

        try
        {
            await setText;
        }
        catch ( Exception e )
        {
            Console.WriteLine( $"Failed to set clipboard text: {e}" );
        }
    }
}