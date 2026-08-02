using ClassicAssist.UO.Data;

namespace ClassicAssist.UO.Network.Packets
{
    public class HuePickerResponse : BasePacket
    {
        public HuePickerResponse( int serial, int itemid, int hue )
        {
            _writer = new PacketWriter( 9 );
            _writer.Write( (byte) 0x95 );
            _writer.Write( serial );
            _writer.Write( (short) itemid );
            _writer.Write( (short) hue );
        }
    }
}
