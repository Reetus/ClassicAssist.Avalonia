using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ClassicAssist.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    ///     Refuses a close the user asked for. The assistant is a companion to the game client and
    ///     ends only when the client does: <see cref="App" /> wires the engine's shutdown to the
    ///     desktop lifetime, and the plugin's dropped RPC connection calls <see cref="Environment.Exit" />.
    ///     A close from either of those, or from the OS going down, is left alone - cancelling those
    ///     would strand the window over the client with no way to end the process.
    /// </summary>
    protected override void OnClosing( WindowClosingEventArgs e )
    {
        base.OnClosing( e );

        if ( e.IsProgrammatic )
        {
            // Close() called by this process - the lifetime's shutdown, a test, a future exit path.
            return;
        }

        if ( e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown )
        {
            return;
        }

        // Anything else is the user: the title bar's close button, Alt+F4, the taskbar's Close. They
        // are ignored so the window cannot be dismissed out from under the client. There is no
        // Avalonia API to disable the button itself - only to intercept the close.
        e.Cancel = true;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}