using System;
using System.Collections.Generic;
using ClassicAssist.Plugin.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     The plugin decides with <see cref="PacketWaitRuleSet" /> whether a packet must wait for the
    ///     UI or can be batched, so these pin the matching (generous by design: a wrong "wait" costs a
    ///     round trip, a wrong "batch" breaks a filter), pattern parsing and the batch format.
    /// </summary>
    [TestClass]
    public class PacketWaitRulesTests
    {
        private static readonly byte[] _deleteFF = [0x1D, 0x40, 0x00, 0x00, 0x01, 0xFF];
        private static readonly byte[] _delete00 = [0x1D, 0x40, 0x00, 0x00, 0x01, 0x00];

        [TestMethod]
        public void FromPatternParsesWildcardsIntoConditionRuns()
        {
            PacketWaitRule rule = PacketWaitRule.FromPattern( "0x1d .. ?? * ?? ff" );

            Assert.AreEqual( 0x1D, rule.PacketId );
            Assert.IsFalse( rule.Outgoing );
            Assert.AreEqual( 1, rule.Conditions.Length );
            Assert.AreEqual( 5, rule.Conditions[0].Position );
            CollectionAssert.AreEqual( new byte[] { 0xFF }, rule.Conditions[0].Bytes );
        }

        [TestMethod]
        public void FromPatternMergesAdjacentLiteralBytes()
        {
            PacketWaitRule rule = PacketWaitRule.FromPattern( "BF ?? ?? 00 08", true );

            Assert.IsTrue( rule.Outgoing );
            Assert.AreEqual( 1, rule.Conditions.Length );
            Assert.AreEqual( 3, rule.Conditions[0].Position );
            CollectionAssert.AreEqual( new byte[] { 0x00, 0x08 }, rule.Conditions[0].Bytes );
        }

        [TestMethod]
        public void FromPatternWithOnlyAnIdHasNoConditions()
        {
            Assert.IsNull( PacketWaitRule.FromPattern( "65" ).Conditions );
        }

        [TestMethod]
        public void FromPatternRejectsBadTokens()
        {
            Assert.ThrowsException<FormatException>( () => PacketWaitRule.FromPattern( "1D zz" ) );
            Assert.ThrowsException<FormatException>( () => PacketWaitRule.FromPattern( "123" ) );
            Assert.ThrowsException<ArgumentException>( () => PacketWaitRule.FromPattern( " " ) );
        }

        [TestMethod]
        public void ToStringRoundTripsThePattern()
        {
            Assert.AreEqual( "< 1D ?? ?? ?? ?? FF", PacketWaitRule.FromPattern( "1D .. .. .. .. FF" ).ToString() );
            Assert.AreEqual( "> 65", PacketWaitRule.FromPattern( "65", true ).ToString() );
        }

        [TestMethod]
        public void RuleMatchesOnlyItsBytes()
        {
            PacketWaitRule rule = PacketWaitRule.FromPattern( "1D ?? ?? ?? ?? FF" );

            Assert.IsTrue( rule.Matches( _deleteFF ) );
            Assert.IsFalse( rule.Matches( _delete00 ) );
            Assert.IsFalse( rule.Matches( new byte[] { 0x1A, 0, 0, 0, 0, 0xFF } ) );
        }

        [TestMethod]
        public void OutOfRangeConditionDoesNotRuleThePacketOut()
        {
            PacketWaitRule rule = PacketWaitRule.FromPattern( "1D ?? ?? ?? ?? ?? ?? ?? 01" );

            Assert.IsTrue( rule.Matches( _delete00 ) );
        }

        [TestMethod]
        public void NegatedConditionDoesNotNarrowTheRule()
        {
            // The UI's PacketFilter ignores Negate, so the plugin must not rely on it either
            PacketWaitRule rule = new( 0x1D, false, new PacketWaitCondition( 5, [0xFF], true ) );

            Assert.IsTrue( rule.Matches( _deleteFF ) );
            Assert.IsTrue( rule.Matches( _delete00 ) );
        }

        [TestMethod]
        public void WaitForEverythingWaitsOnAnything()
        {
            Assert.IsTrue( PacketWaitRuleSet.WaitForEverything.MustWait( _delete00, false ) );
            Assert.IsTrue( PacketWaitRuleSet.WaitForEverything.MustWait( new byte[] { 0xD6 }, true ) );
        }

        [TestMethod]
        public void RuleSetSeparatesDirectionsAndIds()
        {
            PacketWaitRuleSet set = PacketWaitRuleSet.Create( [PacketWaitRule.FromPattern( "65" ), PacketWaitRule.FromPattern( "06", true )] );

            Assert.AreEqual( 2, set.RuleCount );
            Assert.IsTrue( set.MustWait( new byte[] { 0x65, 0x01 }, false ) );
            Assert.IsFalse( set.MustWait( new byte[] { 0x65, 0x01 }, true ) );
            Assert.IsTrue( set.MustWait( new byte[] { 0x06, 0, 0, 0, 1 }, true ) );
            Assert.IsFalse( set.MustWait( new byte[] { 0x06, 0, 0, 0, 1 }, false ) );
            Assert.IsFalse( set.MustWait( new byte[] { 0xD6, 0x00 }, false ) );
        }

        [TestMethod]
        public void RuleSetMatchesAnyRuleForAnId()
        {
            PacketWaitRuleSet set = PacketWaitRuleSet.Create(
                [PacketWaitRule.FromPattern( "1D ?? ?? ?? ?? FF" ), PacketWaitRule.FromPattern( "1D ?? ?? ?? ?? 01" )] );

            Assert.IsTrue( set.MustWait( _deleteFF, false ) );
            Assert.IsFalse( set.MustWait( _delete00, false ) );
        }

        [TestMethod]
        public void UnconditionalRuleCoversEveryPacketWithTheId()
        {
            PacketWaitRuleSet set = PacketWaitRuleSet.Create( [PacketWaitRule.FromPattern( "1D ?? ?? ?? ?? FF" ), new PacketWaitRule( 0x1D )] );

            Assert.IsTrue( set.MustWait( _delete00, false ) );
        }

        [TestMethod]
        public void EmptyRuleSetWaitsOnNothing()
        {
            PacketWaitRuleSet set = PacketWaitRuleSet.Create( null );

            Assert.IsFalse( set.WaitsForEverything );
            Assert.IsFalse( set.MustWait( _deleteFF, false ) );
            Assert.IsFalse( set.MustWait( ReadOnlySpan<byte>.Empty, false ) );
        }

        [TestMethod]
        public void BatchRoundTripsPacketsInOrder()
        {
            PacketBatchWriter writer = new( 64 );

            writer.Add( new byte[] { 0xD6, 1, 2, 3 }, false );
            writer.Add( new byte[] { 0x06, 0, 0, 0, 9 }, true );
            writer.Add( ReadOnlySpan<byte>.Empty, false );

            // Forces a resize past the initial 64 bytes
            byte[] large = new byte[300];
            large[0] = 0xDC;
            writer.Add( large, false );

            Assert.AreEqual( 4, writer.Count );

            List<PacketBatchEntry> entries = PacketBatchWriter.Read( writer.ToArray() );

            Assert.AreEqual( 4, entries.Count );
            CollectionAssert.AreEqual( new byte[] { 0xD6, 1, 2, 3 }, entries[0].Packet );
            Assert.IsFalse( entries[0].Outgoing );
            CollectionAssert.AreEqual( new byte[] { 0x06, 0, 0, 0, 9 }, entries[1].Packet );
            Assert.IsTrue( entries[1].Outgoing );
            Assert.AreEqual( 0, entries[2].Packet.Length );
            CollectionAssert.AreEqual( large, entries[3].Packet );
        }

        [TestMethod]
        public void BatchClearStartsAgain()
        {
            PacketBatchWriter writer = new();

            writer.Add( new byte[] { 0xD6 }, false );
            writer.Clear();
            writer.Add( new byte[] { 0xDC }, false );

            List<PacketBatchEntry> entries = PacketBatchWriter.Read( writer.ToArray() );

            Assert.AreEqual( 1, entries.Count );
            Assert.AreEqual( 0xDC, entries[0].Packet[0] );
        }

        [TestMethod]
        public void TruncatedBatchThrows()
        {
            PacketBatchWriter writer = new();
            writer.Add( new byte[] { 0xD6, 1, 2, 3 }, false );

            byte[] packed = writer.ToArray();

            Assert.ThrowsException<FormatException>( () => PacketBatchWriter.Read( packed.AsSpan( 0, packed.Length - 1 ).ToArray() ) );
            Assert.AreEqual( 0, PacketBatchWriter.Read( null ).Count );
        }
    }
}
