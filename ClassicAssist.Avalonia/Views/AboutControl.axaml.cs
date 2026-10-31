using System.Timers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ClassicAssist.Misc;
using ClassicAssist.Shared;

namespace ClassicAssist.Avalonia.Views;

public partial class AboutControl : UserControl
{
    private readonly TextBlock _control;
    private Timer _timer;

    public AboutControl()
    {
        InitializeComponent();

        _control = this.FindControl<TextBlock>( "CreditText" );

        // _control.PointerEnter += OnPointerEnter;
        // _control.PointerLeave += OnPointerLeave;
    }

    private void OnPointerLeave( object sender, PointerEventArgs e )
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    private void OnPointerEnter( object sender, PointerEventArgs e )
    {
        // Dispose any previous timer first - a new one per enter used to leave the old one (AutoReset)
        // ticking forever, and its Elapsed lambda stopped whatever _timer pointed at by then, i.e. the
        // next enter's timer. The handler now captures its own instance and disposes itself.
        _timer?.Stop();
        _timer?.Dispose();

        Timer timer = new( 1000 ) { AutoReset = false };
        timer.Elapsed += ( o, args ) =>
        {
            timer.Dispose();

            if ( !_control.IsPointerOver )
            {
                return;
            }

            AudioPlayback.Play( Engine.GetResourceStream( "kiss.wav" ), sync: false );
        };

        _timer = timer;

        timer.Start();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}