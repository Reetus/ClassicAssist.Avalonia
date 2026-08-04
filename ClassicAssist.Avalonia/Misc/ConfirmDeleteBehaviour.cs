using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

namespace ClassicAssist.Avalonia.Misc
{
    /// <summary>
    ///     Ports WPF's ImageButtonConfirmDeleteBehaviour: first click arms the delete (icon swaps to
    ///     <see cref="PendingIcon" /> for 2 seconds) and the second click within that window executes the
    ///     button's original Command. The Command is detached in OnAttached so a single click can't fire
    ///     it via the button's own click handling.
    /// </summary>
    public class ConfirmDeleteBehaviour : Behavior<Button>
    {
        public static readonly StyledProperty<IImage> PendingIconProperty =
            AvaloniaProperty.Register<ConfirmDeleteBehaviour, IImage>( nameof( PendingIcon ) );

        private CancellationTokenSource _cancellationTokenSource;
        private ICommand _deleteCommand;
        private bool _isPending;

        public IImage PendingIcon
        {
            get => GetValue( PendingIconProperty );
            set => SetValue( PendingIconProperty, value );
        }

        protected override void OnAttached()
        {
            base.OnAttached();

            AssociatedObject.Click += OnClick;

            if ( AssociatedObject.Command == null )
            {
                return;
            }

            _deleteCommand = AssociatedObject.Command;
            AssociatedObject.Command = null;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.Click -= OnClick;

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            base.OnDetaching();
        }

        private void OnClick( object sender, RoutedEventArgs e )
        {
            if ( !( AssociatedObject.Content is Image image ) )
            {
                return;
            }

            if ( !_isPending )
            {
                _isPending = true;
                IImage originalIcon = image.Source;
                image.Source = PendingIcon;

                _cancellationTokenSource = new CancellationTokenSource();
                CancellationToken token = _cancellationTokenSource.Token;

                // Captures image/originalIcon directly rather than re-reading AssociatedObject when the
                // timer fires: if the ComboBoxItem container gets recycled or the popup tears down its
                // content in the meantime, AssociatedObject can already be stale/null by then, which
                // silently no-ops the revert and leaves the icon stuck red.
                Task.Delay( 2000, token ).ContinueWith( t =>
                {
                    if ( t.IsCanceled )
                    {
                        return;
                    }

                    Dispatcher.UIThread.Post( () =>
                    {
                        image.Source = originalIcon;
                        _isPending = false;
                    } );
                }, TaskScheduler.Default );
            }
            else
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                if ( _deleteCommand?.CanExecute( AssociatedObject.CommandParameter ) == true )
                {
                    _deleteCommand.Execute( AssociatedObject.CommandParameter );
                }

                _isPending = false;
            }

            e.Handled = true;
        }
    }
}
