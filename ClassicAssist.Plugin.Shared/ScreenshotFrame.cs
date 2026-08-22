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

namespace ClassicAssist.Plugin.Shared
{
    /// <summary>
    ///     One frame read back out of the client's graphics device, handed to the UI process as a file
    ///     rather than as bytes on the wire.
    ///     <para>
    ///         The RPC link is JSON, so a <c>byte[]</c> crosses it base64-encoded - about 11MB for a
    ///         1080p frame, on a link that also carries every packet. Both halves are on the same
    ///         machine by construction (the plugin launches the UI), so the pixels go through a temp
    ///         file and only its path is sent. The reader owns the file and is expected to delete it;
    ///         <see cref="ClassicAssist.Plugin.Shared.Reflection.ReflectionImpl" /> also sweeps stale
    ///         ones, for the case where the UI died between the capture and the read.
    ///     </para>
    /// </summary>
    public class ScreenshotFrame
    {
        /// <summary>
        ///     Path to <see cref="Width" /> * <see cref="Height" /> * 4 bytes of RGBA, top row first.
        /// </summary>
        public string Path { get; set; }

        public int Width { get; set; }
        public int Height { get; set; }
    }
}
