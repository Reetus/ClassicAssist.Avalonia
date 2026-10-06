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

using System.Collections.Generic;
using System.Linq;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.PacketFilter;

// ReSharper disable once CheckNamespace
namespace ClassicAssist.Shared;

public static partial class Engine
{
    #region Filters

    // Adding a filter waits for the plugin to start waiting on its packets, so a macro that adds a
    // filter and then provokes the packet can rely on catching it. Removal only makes the rules wider
    // than needed until the next push, so that is left to the registry's coalesced update.

    public static void AddSendPreFilter( PacketFilterInfo pfi )
    {
        _outgoingPacketPreFilter.Add( pfi );
        PacketWaitRegistry.PushNow();
    }

    public static void AddSendPostFilter( PacketFilterInfo pfi )
    {
        _outgoingPacketPostFilter.Add( pfi );
        PacketWaitRegistry.PushNow();
    }

    public static void AddReceiveFilter( PacketFilterInfo pfi )
    {
        _incomingPacketFilter.Add( pfi );
        PacketWaitRegistry.PushNow();
    }

    public static void RemoveReceiveFilter( PacketFilterInfo pfi )
    {
        _incomingPacketFilter.Remove( pfi );
    }

    public static void RemoveSendPreFilter( PacketFilterInfo pfi )
    {
        _outgoingPacketPreFilter.Remove( pfi );
    }

    public static void ClearSendPreFilter()
    {
        _outgoingPacketPreFilter?.Clear();
    }

    public static void RemoveSendPostFilter( PacketFilterInfo pfi )
    {
        _outgoingPacketPostFilter.Remove( pfi );
    }

    public static void ClearSendPostFilter()
    {
        _outgoingPacketPostFilter?.Clear();
    }

    public static void ClearReceiveFilter()
    {
        _incomingPacketFilter?.Clear();
    }

    /// <summary>The runtime filters the synchronous packet path consults, for change notifications.</summary>
    internal static PacketFilter[] RuntimePacketFilters => [_incomingPacketFilter, _outgoingPacketPreFilter, _outgoingPacketPostFilter];

    /// <summary>Wait rules for every runtime filter currently registered.</summary>
    internal static IEnumerable<PacketWaitRule> GetRuntimeFilterWaitRules()
    {
        return _incomingPacketFilter.GetFilters().SelectMany( pfi => PacketWaitRegistry.FromFilter( pfi, false ) )
            .Concat( _outgoingPacketPreFilter.GetFilters().SelectMany( pfi => PacketWaitRegistry.FromFilter( pfi, true ) ) )
            .Concat( _outgoingPacketPostFilter.GetFilters().SelectMany( pfi => PacketWaitRegistry.FromFilter( pfi, true ) ) );
    }

    #endregion
}