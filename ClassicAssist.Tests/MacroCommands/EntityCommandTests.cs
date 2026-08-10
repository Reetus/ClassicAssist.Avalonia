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

using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared;
using ClassicAssist.UO.Network.PacketFilter;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests.MacroCommands
{
    /// <summary>
    ///     <see cref="EntityCommands.WaitForRemoveObject" /> waits on the 0x1D remove object packet, matched
    ///     on the serial at offset 1. Its default timeout is -1, so anything that stops it from matching
    ///     hangs the macro rather than returning False - hence the unknown-alias and wrong-serial cases.
    /// </summary>
    [TestClass]
    public class EntityCommandTests
    {
        private const int SERIAL = 0x40000001;

        /// <summary>
        ///     Runs the command and satisfies it with a 0x1D naming <paramref name="removedSerial" />, or with
        ///     nothing at all when it is null.
        /// </summary>
        private static bool RunWaitForRemoveObject( object obj, int timeout, int? removedSerial )
        {
            Engine.PacketWaitEntries = new PacketWaitEntries();

            void OnWaitEntryAddedEvent( PacketWaitEntry entry )
            {
                if ( removedSerial == null )
                {
                    return;
                }

                int serial = removedSerial.Value;

                byte[] packet =
                {
                    0x1D, (byte) ( serial >> 24 ), (byte) ( serial >> 16 ), (byte) ( serial >> 8 ), (byte) serial
                };

                Engine.PacketWaitEntries.CheckWait( packet, PacketDirection.Incoming );
            }

            Engine.PacketWaitEntries.WaitEntryAddedEvent += OnWaitEntryAddedEvent;

            try
            {
                return EntityCommands.WaitForRemoveObject( obj, timeout );
            }
            finally
            {
                Engine.PacketWaitEntries.WaitEntryAddedEvent -= OnWaitEntryAddedEvent;
            }
        }

        [TestMethod]
        public void WillWaitForRemoveObject()
        {
            Assert.IsTrue( RunWaitForRemoveObject( SERIAL, 1000, SERIAL ) );
        }

        /// <summary>
        ///     The default timeout is infinite, so this only returns because the packet arrives.
        /// </summary>
        [TestMethod]
        public void WillWaitForRemoveObjectWithNoTimeout()
        {
            Engine.PacketWaitEntries = new PacketWaitEntries();

            void OnWaitEntryAddedEvent( PacketWaitEntry entry )
            {
                byte[] packet = { 0x1D, 0x40, 0x00, 0x00, 0x01 };

                Engine.PacketWaitEntries.CheckWait( packet, PacketDirection.Incoming );
            }

            Engine.PacketWaitEntries.WaitEntryAddedEvent += OnWaitEntryAddedEvent;

            try
            {
                Assert.IsTrue( EntityCommands.WaitForRemoveObject( SERIAL ) );
            }
            finally
            {
                Engine.PacketWaitEntries.WaitEntryAddedEvent -= OnWaitEntryAddedEvent;
            }
        }

        [TestMethod]
        public void WillResolveAlias()
        {
            AliasCommands.SetAlias( "removeobjecttest", SERIAL );

            try
            {
                Assert.IsTrue( RunWaitForRemoveObject( "removeobjecttest", 1000, SERIAL ) );
            }
            finally
            {
                AliasCommands.UnsetAlias( "removeobjecttest" );
            }
        }

        [TestMethod]
        public void WillNotWaitForRemoveObjectOfDifferentSerial()
        {
            Assert.IsFalse( RunWaitForRemoveObject( SERIAL, 100, 0x40000002 ) );
        }

        [TestMethod]
        public void WillTimeoutWithNoRemoveObject()
        {
            Assert.IsFalse( RunWaitForRemoveObject( SERIAL, 100, null ) );
        }

        /// <summary>
        ///     An unknown alias resolves to -1, which must not become a wait on serial 0xFFFFFFFF.
        /// </summary>
        [TestMethod]
        public void WillNotWaitForUnknownAlias()
        {
            Engine.PacketWaitEntries = new PacketWaitEntries();

            Assert.IsFalse( EntityCommands.WaitForRemoveObject( "notanalias" ) );
            Assert.AreEqual( 0, Engine.PacketWaitEntries.GetEntries().Length );
        }

        /// <summary>
        ///     A timed-out entry is never matched, so nothing else removes it.
        /// </summary>
        [TestMethod]
        public void WillRemoveWaitEntryOnTimeout()
        {
            RunWaitForRemoveObject( SERIAL, 100, null );

            Assert.AreEqual( 0, Engine.PacketWaitEntries.GetEntries().Length );
        }
    }
}
