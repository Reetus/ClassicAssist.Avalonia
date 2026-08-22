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
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;

namespace ClassicAssist.Plugin.Shared
{
    public interface IHostMethods
    {
        Task<bool> SendPacketToServer( byte[] packet, int length );
        Task<bool> SendPacketToClient( byte[] packet, int length );
        Task<string> GetClientPath();
        /// <summary>
        ///     The client version, as a string rather than a <see cref="Version" />.
        ///     <para>
        ///         Version does not survive the wire consistently: Newtonsoft on .NET serialises it as a
        ///         string, while on the Mono the legacy client bundles it comes out as an object of its
        ///         Major/Minor/Build/Revision properties, which the other end then refuses to read back.
        ///         A string means both runtimes agree.
        ///     </para>
        /// </summary>
        Task<string> GetClientVersion();
        Task<short> GetPacketLength( int id );
        Task<string> GetUOFilePath();
        Task<bool> RequestMove( int dir, bool run );
        void SetTitle( string title );
        Task<(int x, int y)> GetGumpPosition( uint id );
        Task<bool> WalkTo( int x, int y, int z, int distance );
        Task<bool> Pathfinding();
        void CancelPathfinding();
        Task<IntPtr> GetWindowHandle();
        Task<int> GetProcessId();
        void CreateMacroButton( string name, string value );
        Task<Point> GetGameWindowCenter();
        Task<Size> GetGameWindowSize();
        Task<bool> UsePrimaryAbility();
        Task<bool> UseSecondaryAbility();
        Task<bool> Following();
        void Logout();
        void Quit();
        void AddMapMarker( string name, int x, int y, int facet, int zoomLevel, string iconName );
        Task<bool> Follow( int serial );
        void PlayCUOMacro( string name );
        Task<bool> HasDisconnectedGump();

        /// <summary>
        ///     Whether a screenshot of the client window can be taken at all, i.e. whether the client's
        ///     graphics device is reachable from in-process. False on a NativeAOT ClassicUO, whose
        ///     graphics stack is native code - and which <see cref="IsReflectionAvailable" /> cannot
        ///     stand in for, since it loads the plugin managed through its bootstrap and so reports
        ///     true. Callers should surface a capture as unavailable rather than failed.
        /// </summary>
        Task<bool> CanCaptureClientFrame();

        /// <summary>
        ///     Reads the frame the client last drew and returns where the pixels were written, or null
        ///     when this client cannot be captured or stopped ticking before the read happened.
        /// </summary>
        Task<ScreenshotFrame> CaptureClientFrame();

        /// <summary>
        ///     False when the plugin was loaded via the native DNNE export (modern ClassicUO) rather than
        ///     the managed load path TazUO always uses. Client-internals reflection
        ///     (<see cref="ClassicAssist.Plugin.Shared.Reflection" />) targets TazUO's shapes specifically
        ///     and is not expected to work against other clients reached this way.
        /// </summary>
        Task<bool> IsReflectionAvailable();

        void OnShutdown();
    }
}