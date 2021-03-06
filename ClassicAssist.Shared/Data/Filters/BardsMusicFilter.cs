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

using System.Linq;
using ClassicAssist.UO.Network.PacketFilter;

namespace ClassicAssist.Data.Filters
{
    [FilterOptions( Name = "Bards Music", DefaultEnabled = true )]
    public class BardsMusicFilter : DynamicFilterEntry
    {
        private static readonly int[] _bardsMusic = { 0x38, 0x39, 0x43, 0x44, 0x45, 0x46, 0x4C, 0x4D, 0x52, 0x53 };
        private static bool _isEnabled;

        protected override void OnChanged( bool enabled )
        {
            _isEnabled = enabled;
        }

        public override bool CheckPacket( ref byte[] packet, ref int length, PacketDirection direction )
        {
            if ( packet == null || !_isEnabled )
            {
                return false;
            }

            if ( packet[0] != 0x54 || direction != PacketDirection.Incoming )
            {
                return false;
            }

            int soundId = ( packet[2] << 8 ) | packet[3];

            return _bardsMusic.Contains( soundId );
        }
    }
}