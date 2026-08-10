using System.Collections.ObjectModel;
using System.Text;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects.Gumps;

namespace ClassicAssist.Shared.UI.ViewModels.Debug;

public class DebugGumpViewModel : BaseViewModel
{
    public DebugGumpViewModel()
    {
        if ( Engine.Gumps.GetGumps( out Gump[] gumps ) )
        {
            foreach ( Gump gump in gumps )
            {
                Items.Add( gump );
            }
        }

        IncomingPacketHandlers.GumpEvent += ( id, serial, gump ) => _dispatcher.Invoke( () =>
        {
            if ( Items.Contains( gump ) )
            {
                UpdateText( gump );
            }
            else
            {
                Items.Add( gump );
            }
        } );

        OutgoingPacketHandlers.GumpEvent +=
            ( id, serial, gump ) => _dispatcher.Invoke( () => Items.Remove( gump ) );
    }

    public ObservableCollection<Gump> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public Gump SelectedItem
    {
        get;
        set
        {
            SetProperty( ref field, value );
            UpdateText( value );
        }
    }

    public string Text
    {
        get;
        set => SetProperty( ref field, value );
    }

    private void UpdateText( Gump value )
    {
        if ( value == null )
        {
            return;
        }

        StringBuilder sb = new();

        sb.AppendLine( $"Gump ID: 0x{value.ID:x8}" );
        sb.AppendLine( $"Serial: 0x{value.Serial:x8}" );
        sb.AppendLine( $"Pages: {value.Pages?.Length}" );
        sb.AppendLine();
        sb.AppendLine(
            $"Layout: ({value.Layout?.Length})\r\n\r\n{string.Join( "}\r\n", value.Layout?.Split( '}' ) ?? [] )}" );
        sb.AppendLine();
        sb.AppendLine( $"Text: ({value.Strings.Length})\r\n\r\n{string.Join( "\r\n", value.Strings )}" );
        sb.AppendLine();
        sb.AppendLine( $"Elements ({value.GumpElements?.Length}):" );
        sb.AppendLine();

        if ( value.GumpElements != null )
        {
            foreach ( GumpElement element in value.GumpElements )
            {
                sb.AppendLine(
                    $"X: {element.X}, Y: {element.Y}, Type: {element.Type}, Cliloc: {element.Cliloc}, Text: {element.Text}" );
            }
        }

        Text = sb.ToString();
    }
}