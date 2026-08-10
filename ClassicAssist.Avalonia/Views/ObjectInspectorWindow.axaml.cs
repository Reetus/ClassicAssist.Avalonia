using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ClassicAssist.UI.Models;

namespace ClassicAssist.Avalonia.Views;

public partial class ObjectInspectorWindow : Window
{
    public ObjectInspectorWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }

    private void OnItemDoubleTapped( object sender, TappedEventArgs e )
    {
        // The tap lands on whatever part of the row was hit, so walk up to the row it belongs to
        // rather than trusting the sender.
        ListBoxItem item = ( e.Source as Control )?.FindAncestorOfType<ListBoxItem>( true );

        if ( item?.DataContext is not ObjectInspectorData data )
        {
            return;
        }

        data.OnDoubleClick?.Invoke( data );
    }
}
