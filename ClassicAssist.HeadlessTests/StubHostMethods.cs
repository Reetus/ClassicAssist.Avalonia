#region License

// Copyright (C) 2026 Reetus
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
using System.Drawing;
using System.Threading.Tasks;
using ClassicAssist.Plugin.Shared;

namespace ClassicAssist.HeadlessTests;

/// <summary>
///     Stands in for the plugin at the other end of the RPC link. Only the screenshot members do
///     anything - everything else throws, so a test that reaches the client by accident says so rather
///     than quietly returning a default.
/// </summary>
internal sealed class StubHostMethods : IHostMethods
{
    public Func<ScreenshotFrame> Frame { get; set; }

    public bool CanCapture { get; set; } = true;

    public int CaptureCount { get; private set; }

    public Task<bool> CanCaptureClientFrame()
    {
        return Task.FromResult( CanCapture );
    }

    public Task<ScreenshotFrame> CaptureClientFrame()
    {
        CaptureCount++;

        return Task.FromResult( Frame?.Invoke() );
    }

    public Task<bool> SendPacketToServer( byte[] packet, int length ) => throw NotStubbed();
    public Task<bool> SendPacketToClient( byte[] packet, int length ) => throw NotStubbed();
    public Task<string> GetClientPath() => throw NotStubbed();
    public Task<string> GetClientVersion() => throw NotStubbed();
    public Task<short> GetPacketLength( int id ) => throw NotStubbed();
    public Task<string> GetUOFilePath() => throw NotStubbed();
    public Task<bool> RequestMove( int dir, bool run ) => throw NotStubbed();
    public void SetTitle( string title ) => throw NotStubbed();
    public Task<(int x, int y)> GetGumpPosition( uint id ) => throw NotStubbed();
    public Task<bool> WalkTo( int x, int y, int z, int distance ) => throw NotStubbed();
    public Task<bool> Pathfinding() => throw NotStubbed();
    public void CancelPathfinding() => throw NotStubbed();
    public Task<IntPtr> GetWindowHandle() => throw NotStubbed();
    public Task<int> GetProcessId() => throw NotStubbed();
    public void CreateMacroButton( string name, string value ) => throw NotStubbed();
    public Task<Point> GetGameWindowCenter() => throw NotStubbed();
    public Task<Size> GetGameWindowSize() => throw NotStubbed();
    public Task<bool> UsePrimaryAbility() => throw NotStubbed();
    public Task<bool> UseSecondaryAbility() => throw NotStubbed();
    public Task<bool> Following() => throw NotStubbed();
    public void Logout() => throw NotStubbed();
    public void Quit() => throw NotStubbed();

    public void AddMapMarker( string name, int x, int y, int facet, int zoomLevel, string iconName ) =>
        throw NotStubbed();

    public Task<bool> Follow( int serial ) => throw NotStubbed();
    public void PlayCUOMacro( string name ) => throw NotStubbed();
    public Task<bool> HasDisconnectedGump() => throw NotStubbed();
    public Task<bool> IsReflectionAvailable() => Task.FromResult( true );
    public void OnShutdown() => throw NotStubbed();

    private static NotSupportedException NotStubbed()
    {
        return new NotSupportedException( "The screenshot tests only stub the capture members." );
    }
}
