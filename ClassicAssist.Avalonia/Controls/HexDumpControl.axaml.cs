using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using ClassicAssist.Shared;
using ClassicAssist.UI.Misc;
using ClassicAssist.UO.Network.PacketFilter;

namespace ClassicAssist.Avalonia.Controls;

public partial class HexDumpControl : UserControl
{
    public static readonly DirectProperty<HexDumpControl, PacketEntry> PacketProperty =
        AvaloniaProperty.RegisterDirect<HexDumpControl, PacketEntry>( nameof( Packet ), o => o.Packet,
            ( o, v ) => o.Packet = v, defaultBindingMode: BindingMode.OneWay );

    public HexDumpControl()
    {
        InitializeComponent();

        this.FindControl<MenuItem>( "ContextCopy" ).PointerPressed += ( sender, args ) =>
        {
            if ( Packet == null )
            {
                return;
            }

            string prepend = "byte[] packet = new byte[] { ";

            for ( int i = 0; i < Packet.Data.Length; i++ )
            {
                prepend += $"0x{Packet.Data[i]:X2}";

                if ( i + 1 < Packet.Data.Length )
                {
                    prepend += ", ";
                }
            }

            prepend += " };";

            // IClipboard service = (IClipboard) AvaloniaLocator.Current.GetService( typeof( IClipboard ) );
            //
            // service.SetTextAsync( prepend ).ConfigureAwait( false );
        };

        this.FindControl<MenuItem>( "ContextReplay" ).PointerPressed += ( sender, args ) =>
        {
            if ( Packet == null )
            {
                return;
            }

            switch ( Packet.Direction )
            {
                case PacketDirection.Incoming:
                    Engine.SendPacketToClient( Packet.Data, Packet.Data.Length );
                    break;
                case PacketDirection.Outgoing:
                    Engine.SendPacketToServer( Packet.Data, Packet.Data.Length );
                    break;
                case PacketDirection.Any:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        };
    }

    public PacketEntry Packet
    {
        get;
        set => SetAndRaise( PacketProperty, ref field, value );
    }

    public string Status
    {
        get
        {
            if ( Packet?.Data == null )
            {
                return "";
            }

            return "Length: " + Packet.Length;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}