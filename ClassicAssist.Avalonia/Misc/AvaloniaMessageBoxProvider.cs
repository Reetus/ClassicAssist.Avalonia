using System;
using System.Threading.Tasks;
using Engine = ClassicAssist.Avalonia.UiHost;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.ViewModels;
using SEngine = ClassicAssist.Shared.Engine;

namespace ClassicAssist.Avalonia.Misc;

internal class AvaloniaMessageBoxProvider : IMessageBoxProvider
{
    public Task<MessageBoxResult> Show( string text, string caption = null,
        MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxImage? icon = null )
    {
        if ( string.IsNullOrEmpty( caption ) )
        {
            caption = Strings.Error;
        }

        TaskCompletionSource<MessageBoxResult> promise = new();

        SEngine.Dispatcher.Invoke( async () =>
        {
            DialogWindowViewModel model = null;

            try
            {
                DialogWindow window = new();
                model = new DialogWindowViewModel( new SharedWindow
                {
                    Window = window,
                    Type = WindowType.Avalonia,
                    Caption = caption,
                    Text = text,
                    Buttons = buttons,
                    Icon = icon
                } );
                window.DataContext = model;

                await window.ShowDialog( Engine.MainWindow );
            }
            catch ( Exception e )
            {
                // ShowDialog throws when Engine.MainWindow is null - it is assigned after this
                // provider is installed. The exception must not vanish into the async void
                // dispatcher delegate and leave the promise unresolved.
                Console.WriteLine( $"Failed to show message box: {e}" );
            }
            finally
            {
                // Always resolve - every awaiter of Show(...) hangs forever otherwise. Cancel is
                // the safe direction: callers asking for confirmation simply take no action.
                promise.TrySetResult( model?.Result ?? MessageBoxResult.Cancel );
            }
        } );

        return promise.Task;
    }
}