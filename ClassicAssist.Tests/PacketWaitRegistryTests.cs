using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ClassicAssist.Data.Filters;
using ClassicAssist.Misc;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Shared;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     The UI side of the packet wait rules: <see cref="PacketWaitRegistry" /> must claim every packet
    ///     something on the synchronous path might drop or rewrite, and nothing while the filter that
    ///     would is off; batched packets must still reach the packet path in order.
    /// </summary>
    public class PacketWaitRegistryTests
    {
        private const int RehuedSerial = 0x00001234;

        public PacketWaitRegistryTests()
        {
            IncomingPacketFilters.Initialize();
        }

        private static PacketWaitRuleSet CurrentRules()
        {
            return PacketWaitRuleSet.Create( PacketWaitRegistry.BuildRules() );
        }

        private static byte[] WithSerial( byte id, int offset, int serial, int length )
        {
            byte[] packet = new byte[length];
            packet[0] = id;
            packet[offset] = (byte) ( serial >> 24 );
            packet[offset + 1] = (byte) ( serial >> 16 );
            packet[offset + 2] = (byte) ( serial >> 8 );
            packet[offset + 3] = (byte) serial;

            return packet;
        }

        [Fact]
        public void WeatherFilterWaitsOnlyWhileEnabled()
        {
            WeatherFilter filter = new() { Enabled = false };

            try
            {
                Assert.False( CurrentRules().MustWait( new byte[] { 0x65, 0x00, 0x00, 0x00 }, false ) );

                filter.Enabled = true;

                Assert.True( CurrentRules().MustWait( new byte[] { 0x65, 0x00, 0x00, 0x00 }, false ) );
                Assert.False( CurrentRules().MustWait( new byte[] { 0x65, 0x00, 0x00, 0x00 }, true ) );
            }
            finally
            {
                filter.Enabled = false;
                DynamicFilterEntry.Filters.Remove( filter );
            }
        }

        [Fact]
        public void SeasonFilterWaitsOnSeasonAndMapChange()
        {
            SeasonFilter filter = new() { Enabled = true };

            try
            {
                PacketWaitRuleSet rules = CurrentRules();

                Assert.True( rules.MustWait( new byte[] { 0xBC, 0x01, 0x00 }, false ) );
                Assert.True( rules.MustWait( new byte[] { 0xBF, 0x00, 0x06, 0x00, 0x08, 0x01 }, false ) );
                Assert.False( rules.MustWait( new byte[] { 0xBF, 0x00, 0x06, 0x00, 0x04, 0x01 }, false ) );
            }
            finally
            {
                filter.Enabled = false;
                DynamicFilterEntry.Filters.Remove( filter );
            }

            Assert.False( CurrentRules().MustWait( new byte[] { 0xBC, 0x01, 0x00 }, false ) );
        }

        [Fact]
        public void UnknownDynamicFilterWaitsOnEveryIncomingPacket()
        {
            UnknownFilter filter = new() { Enabled = true };

            try
            {
                PacketWaitRuleSet rules = CurrentRules();

                Assert.True( rules.MustWait( new byte[] { 0xD6, 0x00, 0x05 }, false ) );
                Assert.True( rules.MustWait( new byte[] { 0xDC, 0x00 }, false ) );
            }
            finally
            {
                filter.Enabled = false;
                DynamicFilterEntry.Filters.Remove( filter );
            }

            Assert.False( CurrentRules().MustWait( new byte[] { 0xD6, 0x00, 0x05 }, false ) );
        }

        [Fact]
        public void RepeatedMessagesFilterWaitsOnAsciiSpeech()
        {
            bool saved = RepeatedMessagesFilter.IsEnabled;

            try
            {
                RepeatedMessagesFilter.IsEnabled = false;
                Assert.False( CurrentRules().MustWait( new byte[] { 0x1C, 0x00, 0x2C }, false ) );

                RepeatedMessagesFilter.IsEnabled = true;
                Assert.True( CurrentRules().MustWait( new byte[] { 0x1C, 0x00, 0x2C }, false ) );
            }
            finally
            {
                RepeatedMessagesFilter.IsEnabled = saved;
            }
        }

        [Fact]
        public void RehueWaitsOnlyOnTheRehuedSerial()
        {
            Engine.RehueList.Add( RehuedSerial, 0x0021 );

            try
            {
                PacketWaitRuleSet rules = CurrentRules();

                Assert.True( rules.MustWait( WithSerial( 0x20, 1, RehuedSerial, 19 ), false ) );
                Assert.True( rules.MustWait( WithSerial( 0x77, 1, RehuedSerial, 17 ), false ) );
                Assert.True( rules.MustWait( WithSerial( 0x78, 3, RehuedSerial, 23 ), false ) );
                Assert.True( rules.MustWait( WithSerial( 0xF3, 4, RehuedSerial, 26 ), false ) );

                Assert.False( rules.MustWait( WithSerial( 0x20, 1, RehuedSerial + 1, 19 ), false ) );
                Assert.False( rules.MustWait( WithSerial( 0xF3, 4, RehuedSerial + 1, 26 ), false ) );
            }
            finally
            {
                Engine.RehueList.Remove( RehuedSerial );
            }

            Assert.False( CurrentRules().MustWait( WithSerial( 0x20, 1, RehuedSerial, 19 ), false ) );
        }

        [Fact]
        public void RuntimeFilterWaitsWhileRegistered()
        {
            PacketFilterInfo pfi = new( 0xB0, [PacketFilterConditions.ByteAtPositionCondition( 0x05, 3 )] );
            byte[] matching = [0xB0, 0x00, 0x08, 0x05, 0x00, 0x00, 0x00, 0x00];
            byte[] other = [0xB0, 0x00, 0x08, 0x06, 0x00, 0x00, 0x00, 0x00];

            Engine.AddReceiveFilter( pfi );

            try
            {
                PacketWaitRuleSet rules = CurrentRules();

                Assert.True( rules.MustWait( matching, false ) );
                Assert.False( rules.MustWait( other, false ) );
            }
            finally
            {
                Engine.RemoveReceiveFilter( pfi );
            }

            Assert.False( CurrentRules().MustWait( matching, false ) );
        }

        [Fact]
        public void RuntimeSendFiltersWaitOnOutgoingPackets()
        {
            PacketFilterInfo pre = new( 0x12 );

            Engine.AddSendPreFilter( pre );

            try
            {
                Assert.True( CurrentRules().MustWait( new byte[] { 0x12, 0x00, 0x05, 0x24, 0x00 }, true ) );
                Assert.False( CurrentRules().MustWait( new byte[] { 0x12, 0x00, 0x05, 0x24, 0x00 }, false ) );
            }
            finally
            {
                Engine.RemoveSendPreFilter( pre );
            }
        }

        [Fact]
        public void FromFilterTurnsEachConditionIntoARule()
        {
            PacketFilterInfo unconditional = new( 0x1D );
            PacketFilterInfo conditional = new( 0x1D,
                [PacketFilterConditions.ByteAtPositionCondition( 0xFF, 5 ), PacketFilterConditions.ShortAtPositionCondition( 0x0102, 1 )] );

            List<PacketWaitRule> all = [.. PacketWaitRegistry.FromFilter( unconditional, false )];
            List<PacketWaitRule> perCondition = [.. PacketWaitRegistry.FromFilter( conditional, true )];

            Assert.Single( all );
            Assert.Null( all[0].Conditions );

            Assert.Equal( 2, perCondition.Count );
            Assert.True( perCondition.All( r => r.Outgoing && r.PacketId == 0x1D && r.Conditions.Length == 1 ) );
            Assert.Equal( "> 1D ?? ?? ?? ?? FF", perCondition[0].ToString() );
        }

        [Fact]
        public void BatchReachesThePacketPathInOrder()
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

                PacketBatchWriter writer = new();
                writer.Add( new byte[] { 0xDC, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 }, false );
                writer.Add( new byte[] { 0x09, 0x00, 0x00, 0x00, 0x02 }, true );
                writer.Add( new byte[] { 0xDC, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01 }, false );
                writer.Add( new byte[] { 0xDC, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01 }, false );

                new Engine.PluginMethods().OnPacketBatch( writer.ToArray() );

                SpinWait.SpinUntil( () => incoming.Count == 3 && outgoing.Count == 1, TimeSpan.FromSeconds( 5 ) );

                Assert.Equal( new byte[] { 1, 2, 3 }, incoming.Select( p => p[4] ).ToArray() );
                Assert.Equal( 0x09, outgoing.Single()[0] );
            }
            finally
            {
                Engine.IncomingQueue = savedIncoming;
                Engine.OutgoingQueue = savedOutgoing;
                Engine.Installed = savedInstalled;
            }
        }

        [Fact]
        public void BatchIsIgnoredBeforeInstall()
        {
            ThreadQueue<Packet> savedIncoming = Engine.IncomingQueue;
            bool savedInstalled = Engine.Installed;
            int received = 0;

            using ThreadQueue<Packet> incomingQueue = new( _ => Interlocked.Increment( ref received ) );

            try
            {
                Engine.IncomingQueue = incomingQueue;
                Engine.Installed = false;

                PacketBatchWriter writer = new();
                writer.Add( new byte[] { 0xDC, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 }, false );

                new Engine.PluginMethods().OnPacketBatch( writer.ToArray() );

                Thread.Sleep( 50 );

                Assert.Equal( 0, Volatile.Read( ref received ) );
            }
            finally
            {
                Engine.IncomingQueue = savedIncoming;
                Engine.Installed = savedInstalled;
            }
        }

        private sealed class UnknownFilter : DynamicFilterEntry
        {
            public override bool CheckPacket( ref byte[] packet, ref int length, PacketDirection direction )
            {
                return false;
            }
        }
    }
}
