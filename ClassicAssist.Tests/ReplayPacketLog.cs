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
using ClassicAssist.Shared;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Replays the packet sequence a pre-7.0 client sees for a poisoned mobile - it comes into view
    ///     (0x78), walks (0x77), gets a full update (0x20), is cured - and checks the poison state after
    ///     every packet. Each packet rewrites the whole status byte, so one handler that forgets the
    ///     0x04 flag means of the old protocol makes the mobile flicker between poisoned and not.
    ///     <para>
    ///         Originally replayed a capture from a developer's disk; the packets are built here instead.
    ///     </para>
    /// </summary>
    public class ReplayPacketLog : IDisposable
    {
        private const int SERIAL = 0x002549BB;
        private const byte POISONED = 0x04;

        public ReplayPacketLog()
        {
            EngineState.Reset();
            Engine.ClientVersion = new Version( 5, 0, 9, 1 );
        }

        public void Dispose()
        {
            EngineState.Reset();
        }

        /// <summary>0x78 mobile incoming, no equipment.</summary>
        private static byte[] Incoming( byte status )
        {
            PacketWriter writer = new PacketWriter( 23 );
            writer.Write( (byte) 0x78 );
            writer.Write( (short) 23 );
            writer.Write( SERIAL );
            writer.Write( (short) 0x190 ); // body
            writer.Write( (short) 0x0DF9 ); // x
            writer.Write( (short) 0x0A37 ); // y
            writer.Write( (sbyte) 0 ); // z
            writer.Write( (byte) 4 ); // direction
            writer.Write( (short) 0x03EA ); // hue
            writer.Write( status );
            writer.Write( (byte) 1 ); // notoriety
            writer.Write( 0 ); // end of equipment

            return writer.ToArray();
        }

        /// <summary>0x77 mobile moving.</summary>
        private static byte[] Moving( byte status, short x )
        {
            PacketWriter writer = new PacketWriter( 17 );
            writer.Write( (byte) 0x77 );
            writer.Write( SERIAL );
            writer.Write( (short) 0x190 );
            writer.Write( x );
            writer.Write( (short) 0x0A37 );
            writer.Write( (sbyte) 0 );
            writer.Write( (byte) 4 );
            writer.Write( (short) 0x03EA );
            writer.Write( status );
            writer.Write( (byte) 1 );

            return writer.ToArray();
        }

        /// <summary>0x20 mobile update.</summary>
        private static byte[] Update( byte status )
        {
            PacketWriter writer = new PacketWriter( 19 );
            writer.Write( (byte) 0x20 );
            writer.Write( SERIAL );
            writer.Write( (short) 0x190 );
            writer.Write( (byte) 0 );
            writer.Write( (short) 0x03EA );
            writer.Write( status );
            writer.Write( (short) 0x0DFA );
            writer.Write( (short) 0x0A37 );
            writer.Write( (short) 0 );
            writer.Write( (byte) 4 );
            writer.Write( (sbyte) 0 );

            return writer.ToArray();
        }

        [Fact]
        public void CheckPoisonStatus()
        {
            (byte[] packet, bool poisoned)[] replay =
            {
                ( Incoming( POISONED ), true ),
                ( Moving( POISONED, 0x0DFA ), true ),
                ( Update( POISONED ), true ),
                ( Moving( POISONED, 0x0DFB ), true ),
                // Cured.
                ( Update( 0 ), false ),
                ( Moving( 0, 0x0DFC ), false ),
                ( Incoming( 0 ), false )
            };

            for ( int i = 0; i < replay.Length; i++ )
            {
                EngineState.Receive( replay[i].packet );

                Mobile mobile = Engine.Mobiles.GetMobile( SERIAL );

                Assert.NotNull( mobile );
                Assert.True( replay[i].poisoned == mobile.IsPoisoned,
                    $"packet {i} (0x{replay[i].packet[0]:X2}): expected poisoned={replay[i].poisoned}" );
            }
        }
    }
}
