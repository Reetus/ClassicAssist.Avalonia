using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network.PacketFilter;

namespace ClassicAssist.UO.Network.Packets
{
    public abstract class BasePacket
    {
        protected PacketWriter _writer;

        protected BasePacket()
        {
        }

        protected BasePacket( int length )
        {
            _writer = new PacketWriter( length );
        }

        protected BasePacket( PacketDirection direction )
        {
            Direction = direction;
        }

        public PacketDirection Direction { get; set; } = PacketDirection.Any;

        public virtual byte[] ToArray()
        {
            return _writer?.ToArray();
        }

        /// <summary>
        ///     Called by <see cref="ClassicAssist.Shared.Engine.SendPacketToServer(BasePacket)" /> before the
        ///     bytes go out, for packet types the server rate-limits independently of the global send delay.
        /// </summary>
        public virtual void ThrottleBeforeSend()
        {
            // No throttling by default
        }
    }
}