#region License

// Copyright (C) 2020 Reetus
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Shared;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.UO.Network
{
    public static class ActionPacketQueue
    {
        private const int DRAG_DROP_DISTANCE = 3;
        private const int MAX_ATTEMPTS = 5;

        private static readonly ThreadPriorityQueue<BaseQueueItem> _actionPacketQueue =
            new ThreadPriorityQueue<BaseQueueItem>( ProcessActionPacketQueue );

        private static readonly object _actionPacketQueueLock = new object();

        private static void ProcessActionPacketQueue( BaseQueueItem queueItem )
        {
            // Lock here so wait don't set the WaitHandle before we've called ToTask(?)
            lock ( _actionPacketQueueLock )
            {
                if ( queueItem is PacketQueueItem actionQueueItem )
                {
                    if ( actionQueueItem.DelaySend )
                    {
                        while ( Engine.LastActionPacket +
                                TimeSpan.FromMilliseconds( Options.CurrentOptions.ActionDelayMS ) > DateTime.Now )
                        {
                            Thread.Sleep( 1 );
                        }
                    }

                    byte[] data = actionQueueItem.Packet;
                    int length = actionQueueItem.Length;

                    Engine.LastActionPacket = DateTime.Now;
                    Engine.SendPacketToServer( data, length );

                    actionQueueItem.WaitHandle.Set();
                }
                else if ( queueItem is ActionQueueItem actionItem )
                {
                    if ( actionItem.DelaySend )
                    {
                        while ( Engine.LastActionPacket +
                                TimeSpan.FromMilliseconds( Options.CurrentOptions.ActionDelayMS ) > DateTime.Now )
                        {
                            Thread.Sleep( 1 );
                        }
                    }

                    bool? result = actionItem.Action?.Invoke( actionItem.CheckRange );

                    if ( result.HasValue && result.Value )
                    {
                        Engine.LastActionPacket = DateTime.Now;
                    }

                    actionItem.WaitHandle.Set();
                }
            }
        }

        // ReSharper disable once UnusedMember.Global
        public static Task EnqueueActionPacket( byte[] packet, int length, QueuePriority priority, bool delaySend )
        {
            return EnqueueActionPacket( new PacketQueueItem( packet, length, delaySend ), priority );
        }

        public static Task EnqueueActionPacket( BasePacket packet, QueuePriority priority = QueuePriority.Low,
            bool delaySend = true )
        {
            byte[] data = packet.ToArray();

            return EnqueueActionPacket( new PacketQueueItem( data, data.Length, delaySend ), priority );
        }

        public static Task EnqueueActionPacket( PacketQueueItem packetQueueItem, QueuePriority priority )
        {
            lock ( _actionPacketQueueLock )
            {
                _actionPacketQueue.Enqueue( packetQueueItem, priority );

                return packetQueueItem.WaitHandle.ToTask();
            }
        }

        /// <summary>
        ///     Sends the drag and drop packets as one queue item with a fixed gap between them, rather than
        ///     two independently-throttled ones.
        ///     <para>
        ///         Queuing them separately (as this used to) meant the drop packet re-entered the
        ///         <see cref="Options.ActionDelayMS" /> gate on its own, right after the drag packet had
        ///         just reset it - so the drop routinely landed several hundred ms after the pickup instead
        ///         of the ~50ms below. Long enough for the player to have moved in between, which
        ///         invalidates the ground coordinates computed against their old position and gets the
        ///         drop rejected - the item bounces back. Matches <see cref="EnqueueDragDrop(int,int,int,QueuePriority,bool,bool,bool,int,int,DragDropOptions,int)" />,
        ///         which already did this correctly.
        ///     </para>
        /// </summary>
        public static Task EnqueueDragDropGround( int serial, int amount, int x, int y, int z,
            QueuePriority priority = QueuePriority.Low, bool delaySend = true )
        {
            lock ( _actionPacketQueueLock )
            {
                ActionQueueItem actionQueueItem = new ActionQueueItem( _ =>
                {
                    Engine.SendPacketToServer( new DragItem( serial, amount ) );
                    Thread.Sleep( 50 );
                    Engine.SendPacketToServer( new DropItem( serial, -1, x, y, z ) );

                    return true;
                } ) { DelaySend = delaySend, Serial = serial };

                _actionPacketQueue.Enqueue( actionQueueItem, priority );

                return actionQueueItem.WaitHandle.ToTask();
            }
        }

        public static Task EnqueueActionPackets( IEnumerable<BasePacket> packets,
            QueuePriority priority = QueuePriority.Low, bool delaySend = true )
        {
            lock ( _actionPacketQueueLock )
            {
                List<EventWaitHandle> handles = new List<EventWaitHandle>();

                foreach ( BasePacket packet in packets )
                {
                    byte[] data = packet.ToArray();

                    PacketQueueItem packetQueueItem = new PacketQueueItem( data, data.Length, delaySend );
                    handles.Add( packetQueueItem.WaitHandle );

                    _actionPacketQueue.Enqueue( packetQueueItem, priority );
                }

                return handles.ToTask();
            }
        }

        public static Task EnqueueDragDrop( int serial, int amount, int containerSerial,
            QueuePriority priority = QueuePriority.Low, bool checkRange = false, bool checkExisting = false, bool delaySend = true, int x = -1,
            int y = -1, DragDropOptions options = null, int attempt = 0 )
        {
            lock ( _actionPacketQueueLock )
            {
                if ( ( options?.CheckExisting ?? checkExisting ) &&
                     _actionPacketQueue.Contains( e => e is ActionQueueItem aqi && aqi.Serial == serial ) )
                {
                    return Task.CompletedTask;
                }

                ActionQueueItem actionQueueItem = new ActionQueueItem( check =>
                {
                    if ( check )
                    {
                        Item item = Engine.Items.GetItem( serial );

                        if ( item == null || item.Distance >= DRAG_DROP_DISTANCE )
                        {
                            if ( !MacroManager.QuietMode )
                            {
                                Commands.SystemMessage( Strings.Item_out_of_range___ );
                            }

                            return false;
                        }
                    }

                    Engine.SendPacketToServer( new DragItem( serial, amount ) );
                    Thread.Sleep( 50 );
                    Engine.SendPacketToServer( new DropItem( serial, containerSerial, x, y, 0 ) );

                    if ( !( options?.RequeueFailure ?? false ) || options.SuccessPredicate == null ||
                         attempt >= MAX_ATTEMPTS )
                    {
                        return true;
                    }

                    Commands.WaitForContainerContents( containerSerial, Options.CurrentOptions.ActionDelayMS );

                    bool result = options.SuccessPredicate.Invoke( serial, containerSerial );

                    if ( result )
                    {
                        return true;
                    }

                    EnqueueDragDrop( serial, amount, containerSerial, priority, checkRange, checkExisting, delaySend, x,
                        y, options, ++attempt );

                    // Return false so the queue processor doesn't reset the action delay before the retry.
                    return false;
                } ) { CheckRange = options?.CheckRange ?? checkRange, DelaySend = options?.DelaySend ?? delaySend, Serial = serial };

                _actionPacketQueue.Enqueue( actionQueueItem, priority );

                return actionQueueItem.WaitHandle.ToTask();
            }
        }

        public static int Count()
        {
            lock ( _actionPacketQueueLock )
            {
                return _actionPacketQueue.Count();
            }
        }

        public static void Clear()
        {
            lock ( _actionPacketQueueLock )
            {
                _actionPacketQueue.Clear();
            }
        }
    }
}