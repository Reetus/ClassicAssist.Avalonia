using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using ClassicAssist.Misc;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Shared;
using ClassicAssist.UO.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     The UI process arms a startup gate while Avalonia, the UO data files and the tab view models
    ///     load. Packets arriving in that window used to be dropped, which lost the shard list (0xA8 /
    ///     0xA0) and the whole login state for a fast login. They must be buffered and replayed in order
    ///     once the UI is up, and must not reach the packet path a moment earlier.
    /// </summary>
    [TestClass]
    public class StartupPacketGateTests
    {
        [TestMethod]
        public void BufferedPacketsAreReplayedInOrderAfterStartup()
        {
            ThreadQueue<Packet> savedIncoming = Engine.IncomingQueue;
            ThreadQueue<Packet> savedOutgoing = Engine.OutgoingQueue;
            bool savedInstalled = Engine.Installed;

            ConcurrentQueue<byte[]> incoming = new();
            ConcurrentQueue<byte[]> outgoing = new();

            using ThreadQueue<Packet> incomingQueue = new( p => incoming.Enqueue( p.GetPacket() ) );
            using ThreadQueue<Packet> outgoingQueue = new( p => outgoing.Enqueue( p.GetPacket() ) );

            try
            {
                Engine.IncomingQueue = incomingQueue;
                Engine.OutgoingQueue = outgoingQueue;
                Engine.Installed = true;

                Engine.ArmStartupBuffer();

                Engine.PluginMethods plugin = new();

                // A fast login, split across the three callback shapes the plugin uses. The marker is
                // byte 4; 0xDC / 0x09 are the ids the existing batch test already proves reach the
                // queues without a filter claiming them.
                _ = plugin.OnPacketReceive( new byte[] { 0xDC, 0, 0, 0, 1, 0, 0, 0, 1 }, 9, 0 );
                _ = plugin.OnPacketSend( new byte[] { 0x09, 0, 0, 0, 2 }, 5, 0 );

                PacketBatchWriter writer = new();
                writer.Add( new byte[] { 0xDC, 0, 0, 0, 3, 0, 0, 0, 1 }, false );
                writer.Add( new byte[] { 0x09, 0, 0, 0, 4 }, true );
                plugin.OnPacketBatch( writer.ToArray() );

                // Nothing may reach the packet path while the gate is shut - replay runs on fully
                // built state, not half-built state.
                Thread.Sleep( 50 );
                Assert.AreEqual( 0, incoming.Count );
                Assert.AreEqual( 0, outgoing.Count );

                Engine.CompleteStartup();

                SpinWait.SpinUntil( () => incoming.Count == 2 && outgoing.Count == 2, TimeSpan.FromSeconds( 5 ) );

                CollectionAssert.AreEqual( new byte[] { 1, 3 }, incoming.Select( p => p[4] ).ToArray() );
                CollectionAssert.AreEqual( new byte[] { 2, 4 }, outgoing.Select( p => p[4] ).ToArray() );
            }
            finally
            {
                // Always open the gate; a failed assertion must not leave it buffering for every
                // later test in the run.
                Engine.CompleteStartup();
                Engine.IncomingQueue = savedIncoming;
                Engine.OutgoingQueue = savedOutgoing;
                Engine.Installed = savedInstalled;
            }
        }
    }
}
