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

using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;
using System;
using Xunit;

namespace ClassicAssist.Tests
{
    public class OutgoingPacketHandlerTests : IDisposable
    {
        public OutgoingPacketHandlerTests()
        {
            Engine.Items = new ItemCollection( 0 );
            Engine.Mobiles = new MobileCollection( Engine.Items );
            Engine.Player = new PlayerMobile( 1 );

            OutgoingPacketHandlers.Initialize();
        }

        [Fact]
        public void WillSetHolding()
        {
            byte[] packet = { 0x07, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };

            PacketHandler handler = OutgoingPacketHandlers.GetHandler( 0x07 );

            handler.OnReceive( new PacketReader( packet, packet.Length, true ) );

            Assert.Equal( unchecked( (int) 0xAABBCCDD ), Engine.Player.Holding );
            Assert.Equal( 0xEEFF, Engine.Player.HoldingAmount );
        }

        /// <summary>
        ///     0x12 sub-command 0x24 is the client using a skill. Tracking it is what makes "Use Last
        ///     Skill" follow a skill the player used from the client's own macros, not just ours.
        /// </summary>
        [Fact]
        public void WillTrackLastSkillFromClient()
        {
            Engine.LastSkillID = 0;

            // "12 0" - Item Identification, followed by the argument and a terminator.
            byte[] packet = { 0x12, 0x00, 0x09, 0x24, 0x31, 0x32, 0x20, 0x30, 0x00 };

            OutgoingPacketHandlers.GetHandler( 0x12 ).OnReceive( new PacketReader( packet, packet.Length, false ) );

            Assert.Equal( 12, Engine.LastSkillID );
        }

        [Fact]
        public void WillTrackLastSpellFromLegacyClient()
        {
            Engine.LastSpellID = 0;

            // Same packet, sub-command 0x56 - a client old enough to cast by text command.
            byte[] packet = { 0x12, 0x00, 0x09, 0x56, 0x34, 0x32, 0x20, 0x30, 0x00 };

            OutgoingPacketHandlers.GetHandler( 0x12 ).OnReceive( new PacketReader( packet, packet.Length, false ) );

            Assert.Equal( 42, Engine.LastSpellID );
        }

        [Fact]
        public void WillIgnoreMalformedSkillPacket()
        {
            Engine.LastSkillID = 7;

            // No space separating the id from its argument, which upstream substrings on unchecked.
            byte[] packet = { 0x12, 0x00, 0x07, 0x24, 0x31, 0x32, 0x00 };

            OutgoingPacketHandlers.GetHandler( 0x12 ).OnReceive( new PacketReader( packet, packet.Length, false ) );

            Assert.Equal( 7, Engine.LastSkillID ); // "a malformed packet should leave the last skill alone"
        }

        /// <summary>
        ///     The packet we send has to update the same state, or using a skill through a macro and then
        ///     pressing the hotkey would repeat whatever came before it.
        /// </summary>
        [Fact]
        public void WillTrackLastSkillFromOurOwnPacket()
        {
            Engine.LastSkillID = 0;

            // ReSharper disable once ObjectCreationAsStatement
            new UseSkill( 34 );

            Assert.Equal( 34, Engine.LastSkillID );
        }

        public void Dispose()
        {
        }
    }
}