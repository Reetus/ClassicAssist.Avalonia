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

namespace ClassicAssist.Plugin.Shared
{
    /// <summary>
    ///     Managed counterparts to the CUO_API function pointers.
    ///     The UI process never marshals these to native code - it only ever binds them to lambdas that
    ///     forward to <see cref="IHostMethods" /> - so they are plain delegates with no interop attributes.
    ///     The plugin process keeps using the real CUO_API types, which is where marshalling matters.
    /// </summary>
    public delegate bool SendRecvPacket( ref byte[] data, ref int length );

    public delegate short GetPacketLength( int id );

    public delegate string GetUOFilePath();

    public delegate bool Move( int dir, bool run );

    public delegate void SetTitle( string title );
}
