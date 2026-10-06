#region License
// Copyright (C) 2025 Reetus
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
using System.Threading.Tasks;

namespace ClassicAssist.Shared
{
    public interface IPluginMethods
    {
        void OnConnected();
        void OnDisconnected();
        Task<(bool, byte[], int)> OnPacketReceive( byte[] data, int length, long sentAt );
        Task<(bool, byte[], int)> OnPacketSend( byte[] data, int length, long sentAt );
        void OnClientClosing();
        Task<bool> OnHotkeyPressed( int key, int mod, bool pressed );
        void OnMouse( int button, int wheel );
        void OnTick();
        void OnFocusChanged( bool focus );
        void OnPlayerPositionChanged( int x, int y, int z );

        /// <summary>
        ///     Packets the plugin let through without waiting, because no
        ///     <see cref="ClassicAssist.Plugin.Shared.PacketWaitRule" /> asked it to. Packed by
        ///     <see cref="ClassicAssist.Plugin.Shared.PacketBatchWriter" />, in the order the client saw
        ///     them, and always sent before the next packet the plugin does wait on. One-way: the
        ///     client already has these packets, so they can be observed but not dropped or rewritten.
        /// </summary>
        void OnPacketBatch( byte[] packed );
    }
}