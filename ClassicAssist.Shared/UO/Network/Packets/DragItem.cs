using System;
using System.Threading;
using ClassicAssist.Data;
using ClassicAssist.Shared;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.UO.Network.Packets;

public class DragItem : BasePacket
{
    private static readonly Lock _dragPacketLock = new();
    private static DateTime _lastDragPacketTime = DateTime.MinValue;

    public DragItem( int serial, int amount, bool checkAmount = false )
    {
        if ( checkAmount && amount == -1 )
        {
            Item item = Engine.Items.GetItem( serial );

            if ( item != null )
            {
                amount = item.Count;
            }
        }

        _writer = new PacketWriter( 7 );
        _writer.Write( (byte) 0x07 );
        _writer.Write( serial );
        _writer.Write( (short) amount );
    }

    /// <summary>
    ///     Sphere-X shards throttle drag packets server-side, so drags need their own spacing on top of
    ///     the global send delay.
    ///     <para>
    ///         WPF passes the delay in as a constructor argument, which meant every one of its eight
    ///         call sites repeated <c>DragDelay ? DragDelayMS : 0</c>. No caller ever wants a different
    ///         value, so it's read from the options here instead and the constructor keeps its shape.
    ///     </para>
    /// </summary>
    public override void ThrottleBeforeSend()
    {
        Options options = Options.CurrentOptions;

        if ( options == null || !options.DragDelay || options.DragDelayMS <= 0 )
        {
            return;
        }

        lock ( _dragPacketLock )
        {
            DateTime nextAllowed = _lastDragPacketTime + TimeSpan.FromMilliseconds( options.DragDelayMS );
            DateTime now = DateTime.Now;

            if ( nextAllowed > now )
            {
                Thread.Sleep( nextAllowed - now );
            }

            _lastDragPacketTime = DateTime.Now;
        }
    }
}
