#region License

// Copyright (C) 2026 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY

#endregion

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace ClassicAssist.Plugin.Shared
{
    /// <summary>
    ///     Packets the plugin let through without waiting, packed into one blob for
    ///     <see cref="ClassicAssist.Shared.IPluginMethods.OnPacketBatch" />. One message instead of
    ///     one per packet is the point: the UI handles messages one at a time, so per-message cost is
    ///     what a burst of packets pays.
    ///     <para>
    ///         Layout, repeated: a flags byte (bit 0 = outgoing, client to server), the packet length
    ///         as a little-endian int32, then the packet bytes.
    ///     </para>
    /// </summary>
    public sealed class PacketBatchWriter
    {
        private const int HeaderSize = 5;
        private const byte OutgoingFlag = 0x01;

        private byte[] _buffer;

        public PacketBatchWriter( int initialCapacity = 16 * 1024 )
        {
            _buffer = new byte[Math.Max( initialCapacity, 64 )];
        }

        /// <summary>Packets added since the last <see cref="Clear" />.</summary>
        public int Count { get; private set; }

        /// <summary>Bytes used, headers included.</summary>
        public int Length { get; private set; }

        public void Add( ReadOnlySpan<byte> packet, bool outgoing )
        {
            int needed = Length + HeaderSize + packet.Length;

            if ( needed > _buffer.Length )
            {
                Array.Resize( ref _buffer, Math.Max( needed, _buffer.Length * 2 ) );
            }

            Span<byte> destination = _buffer.AsSpan( Length );

            destination[0] = outgoing ? OutgoingFlag : (byte) 0;
            BinaryPrimitives.WriteInt32LittleEndian( destination.Slice( 1 ), packet.Length );
            packet.CopyTo( destination.Slice( HeaderSize ) );

            Length = needed;
            Count++;
        }

        /// <summary>A copy of the packed batch, safe to hand to an asynchronous send.</summary>
        public byte[] ToArray()
        {
            return _buffer.AsSpan( 0, Length ).ToArray();
        }

        public void Clear()
        {
            Length = 0;
            Count = 0;
        }

        /// <summary>
        ///     Unpacks a batch in order. Each packet gets its own array, because the UI's packet path
        ///     keeps and rewrites them.
        /// </summary>
        public static List<PacketBatchEntry> Read( byte[] packed )
        {
            List<PacketBatchEntry> entries = new List<PacketBatchEntry>();

            if ( packed == null )
            {
                return entries;
            }

            ReadOnlySpan<byte> remaining = packed;

            while ( remaining.Length >= HeaderSize )
            {
                bool outgoing = ( remaining[0] & OutgoingFlag ) != 0;
                int length = BinaryPrimitives.ReadInt32LittleEndian( remaining.Slice( 1 ) );

                if ( length < 0 || length > remaining.Length - HeaderSize )
                {
                    throw new FormatException( $"Packet batch is truncated: a {length} byte packet with {remaining.Length - HeaderSize} bytes left." );
                }

                entries.Add( new PacketBatchEntry( remaining.Slice( HeaderSize, length ).ToArray(), outgoing ) );
                remaining = remaining.Slice( HeaderSize + length );
            }

            if ( !remaining.IsEmpty )
            {
                throw new FormatException( $"Packet batch has {remaining.Length} trailing bytes." );
            }

            return entries;
        }
    }

    public readonly struct PacketBatchEntry
    {
        public PacketBatchEntry( byte[] packet, bool outgoing )
        {
            Packet = packet;
            Outgoing = outgoing;
        }

        public byte[] Packet { get; }

        /// <summary>Client to server (an OnPacketSend) rather than server to client.</summary>
        public bool Outgoing { get; }
    }
}
