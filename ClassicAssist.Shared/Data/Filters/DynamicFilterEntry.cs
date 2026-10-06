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
using System.Linq;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.UO.Network.PacketFilter;

namespace ClassicAssist.Data.Filters;

public abstract class DynamicFilterEntry : FilterEntry
{
    protected DynamicFilterEntry()
    {
        Filters.Add( this );
    }

    public static List<DynamicFilterEntry> Filters { get; set; } = [];

    protected override void OnChanged( bool enabled )
    {
    }

    public virtual bool CheckPacket( ref byte[] packet, ref int length, PacketDirection direction )
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///     The packets <see cref="CheckPacket" /> might drop or rewrite while the filter is
    ///     <see cref="FilterEntry.Enabled" />; only asked then. The plugin waits on these and batches
    ///     the rest, so a filter that cannot say precisely must not under-report. This default knows
    ///     nothing about the subclass, so it claims every incoming packet (the only direction
    ///     <see cref="CheckPacket" /> is called for); every filter here overrides it.
    /// </summary>
    public virtual IEnumerable<PacketWaitRule> GetWaitRules()
    {
        return Enumerable.Range( 0, 0x100 ).Select( id => new PacketWaitRule( (byte) id ) );
    }
}