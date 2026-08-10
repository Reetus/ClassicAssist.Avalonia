using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClassicAssist.UI.ViewModels;

namespace ClassicAssist.Avalonia.Controls;

/// <summary>
///     Magnifying-glass search box with an optional close button, the Avalonia port of the WPF
///     <c>ClassicAssist.Controls.FilterControl</c>. The control itself never hides based on
///     <see cref="IsFilterVisible" /> - hosts that want it collapsed bind <c>IsVisible</c>
///     themselves (see MacrosTabControl).
/// </summary>
public partial class FilterControl : UserControl
{
    public static readonly StyledProperty<string> FilterTextProperty =
        AvaloniaProperty.Register<FilterControl, string>( nameof( FilterText ),
            defaultBindingMode: BindingMode.TwoWay );

    public static readonly StyledProperty<bool> IsFilterVisibleProperty =
        AvaloniaProperty.Register<FilterControl, bool>( nameof( IsFilterVisible ),
            defaultBindingMode: BindingMode.TwoWay );

    public static readonly StyledProperty<bool> ShowCloseButtonProperty =
        AvaloniaProperty.Register<FilterControl, bool>( nameof( ShowCloseButton ), true );
    private TextBox _textBox;

    public FilterControl()
    {
        InitializeComponent();

        _textBox = this.FindControl<TextBox>( "FilterTextBox" );

        IsFilterVisibleProperty.Changed.AddClassHandler<FilterControl>( ( o, e ) => o.OnIsFilterVisibleChanged( e ) );
    }

    public ICommand CloseCommand => field ??= new RelayCommand( Close );

    public string FilterText
    {
        get => GetValue( FilterTextProperty );
        set => SetValue( FilterTextProperty, value );
    }

    public bool IsFilterVisible
    {
        get => GetValue( IsFilterVisibleProperty );
        set => SetValue( IsFilterVisibleProperty, value );
    }

    public bool ShowCloseButton
    {
        get => GetValue( ShowCloseButtonProperty );
        set => SetValue( ShowCloseButtonProperty, value );
    }

    private void Close( object obj )
    {
        FilterText = string.Empty;
        IsFilterVisible = false;
    }

    private void OnIsFilterVisibleChanged( AvaloniaPropertyChangedEventArgs e )
    {
        if ( e.NewValue is not bool visible || !visible || _textBox == null )
        {
            return;
        }

        // Defer until the control has had a chance to become visible/layout out, otherwise
        // Focus() on a collapsed element is a no-op.
        Dispatcher.UIThread.Post( () => { _textBox.Focus(); }, DispatcherPriority.Input );
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}
