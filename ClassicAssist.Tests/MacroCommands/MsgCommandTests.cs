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

using System.Collections.Generic;
using System.Diagnostics;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared;
using ClassicAssist.UO.Network.PacketFilter;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests.MacroCommands
{
    /// <summary>
    ///     The server picks whether a prompt is ASCII (0x9A) or Unicode (0xC2), so the reply, the cancel
    ///     and the wait all have to handle both. <see cref="Engine.LastPromptType" /> is 0 for ASCII and
    ///     1 for Unicode, set by the incoming handler for whichever prompt packet arrived.
    /// </summary>
    [TestClass]
    public class MsgCommandTests
    {
        private const byte ASCII_PROMPT = 0x9A;
        private const byte UNICODE_PROMPT = 0xC2;

        /// <summary>
        ///     Captures the packet ids <paramref name="action" /> sends to the server.
        /// </summary>
        private static List<byte> CapturePacketIds( int lastPromptType, System.Action action )
        {
            List<byte> sent = new List<byte>();

            Engine.LastPromptType = lastPromptType;
            Engine.LastPromptSerial = 0x11223344;
            Engine.LastPromptID = 0x55667788;

            void OnInternalPacketSentEvent( byte[] data, int length )
            {
                sent.Add( data[0] );
            }

            Engine.InternalPacketSentEvent += OnInternalPacketSentEvent;

            try
            {
                action();
            }
            finally
            {
                // Engine is a process-wide singleton, so a handler left attached here fires against
                // packets sent by every later test.
                Engine.InternalPacketSentEvent -= OnInternalPacketSentEvent;
            }

            return sent;
        }

        /// <summary>
        ///     Runs <see cref="MsgCommands.WaitForPrompt" /> and satisfies it with a
        ///     <paramref name="packetId" /> prompt, or with nothing at all when it is null.
        /// </summary>
        private static bool RunWaitForPrompt( byte? packetId, int timeout )
        {
            Engine.PacketWaitEntries = new PacketWaitEntries();

            void OnWaitEntryAddedEvent( PacketWaitEntry entry )
            {
                if ( packetId == null || entry.PFI.PacketID != packetId )
                {
                    return;
                }

                // A prompt packet is only matched on its id here, so the body can stay zeroed.
                byte[] packet = new byte[21];
                packet[0] = packetId.Value;

                Engine.PacketWaitEntries.CheckWait( packet, PacketDirection.Incoming );
            }

            Engine.PacketWaitEntries.WaitEntryAddedEvent += OnWaitEntryAddedEvent;

            try
            {
                return MsgCommands.WaitForPrompt( timeout );
            }
            finally
            {
                Engine.PacketWaitEntries.WaitEntryAddedEvent -= OnWaitEntryAddedEvent;
                Engine.PacketWaitEntries = new PacketWaitEntries();
            }
        }

        [TestMethod]
        public void WillReplyToAsciiPromptWithAsciiResponse()
        {
            List<byte> sent = CapturePacketIds( 0, () => MsgCommands.PromptMsg( "hello" ) );

            CollectionAssert.AreEqual( new List<byte> { ASCII_PROMPT }, sent );
        }

        [TestMethod]
        public void WillReplyToUnicodePromptWithUnicodeResponse()
        {
            List<byte> sent = CapturePacketIds( 1, () => MsgCommands.PromptMsg( "hello" ) );

            CollectionAssert.AreEqual( new List<byte> { UNICODE_PROMPT }, sent );
        }

        [TestMethod]
        public void WillCancelAsciiPromptWithAsciiCancel()
        {
            List<byte> sent = CapturePacketIds( 0, MsgCommands.CancelPrompt );

            CollectionAssert.AreEqual( new List<byte> { ASCII_PROMPT }, sent );
        }

        [TestMethod]
        public void WillCancelUnicodePromptWithUnicodeCancel()
        {
            List<byte> sent = CapturePacketIds( 1, MsgCommands.CancelPrompt );

            CollectionAssert.AreEqual( new List<byte> { UNICODE_PROMPT }, sent );
        }

        [TestMethod]
        public void WillWaitForUnicodePrompt()
        {
            Assert.IsTrue( RunWaitForPrompt( UNICODE_PROMPT, 5000 ) );
        }

        [TestMethod]
        public void WillWaitForAsciiPrompt()
        {
            Assert.IsTrue( RunWaitForPrompt( ASCII_PROMPT, 5000 ) );
        }

        [TestMethod]
        public void WillTimeoutWaitingForPrompt()
        {
            Stopwatch sw = Stopwatch.StartNew();

            bool result = RunWaitForPrompt( null, 500 );

            sw.Stop();

            Assert.IsFalse( result );

            // Guards against the wait returning false instantly rather than actually waiting.
            Assert.IsTrue( sw.ElapsedMilliseconds >= 400, $"Returned after only {sw.ElapsedMilliseconds}ms" );
        }
    }
}
