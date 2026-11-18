using System;
using System.Collections.Generic;
using ClassicAssist.Plugin.Shared;
using Xunit;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     The plugin decides with <see cref="PacketWaitRuleSet" /> whether a packet must wait for the
    ///     UI or can be batched, so these pin the matching (generous by design: a wrong "wait" costs a
    ///     round trip, a wrong "batch" breaks a filter), pattern parsing and the batch format.
    /// </summary>
    public class PacketWaitRulesTests
    {
        private static readonly byte[] _deleteFF = [0x1D, 0x40, 0x00, 0x00, 0x01, 0xFF];
        private static readonly byte[] _delete00 = [0x1D, 0x40, 0x00, 0x00, 0x01, 0x00];

        [Fact]
        public void FromPatternParsesWildcardsIntoConditionRuns()
        {
            PacketWaitRule rule = PacketWaitRule.FromPattern( "0x1d .. ?? * ?? ff" );

            Assert.Equal( 0x1D, rule.PacketId );
            Assert.False( rule.Outgoing );
            Assert.Single( rule.Conditions );
            Assert.Equal( 5, rule.Conditions[0].Position );
            Assert.Equal( new byte[] { 0xFF }, rule.Conditions[0].Bytes );
        }

        [Fact]
        public void FromPatternMergesAdjacentLiteralBytes()
        {
            PacketWaitRule rule = PacketWaitRule.FromPattern( "BF ?? ?? 00 08", true );

            Assert.True( rule.Outgoing );
            Assert.Single( rule.Conditions );
            Assert.Equal( 3, rule.Conditions[0].Position );
            Assert.Equal( new byte[] { 0x00, 0x08 }, rule.Conditions[0].Bytes );
        }

        [Fact]
        public void FromPatternWithOnlyAnIdHasNoConditions()
        {
            Assert.Null( PacketWaitRule.FromPattern( "65" ).Conditions );
        }

        [Fact]
        public void FromPatternRejectsBadTokens()
        {
            Assert.Throws<FormatException>( () => PacketWaitRule.FromPattern( "1D zz" ) );
            Assert.Throws<FormatException>( () => PacketWaitRule.FromPattern( "123" ) );
            Assert.Throws<ArgumentException>( () => PacketWaitRule.FromPattern( " " ) );
        }

        [Fact]
        public void ToStringRoundTripsThePattern()
        {
            Assert.Equal( "< 1D ?? ?? ?? ?? FF", PacketWaitRule.FromPattern( "1D .. .. .. .. FF" ).ToString() );
            Assert.Equal( "> 65", PacketWaitRule.FromPattern( "65", true ).ToString() );
        }

        [Fact]
        public void RuleMatchesOnlyItsBytes()
        {
            PacketWaitRule rule = PacketWaitRule.FromPattern( "1D ?? ?? ?? ?? FF" );

            Assert.True( rule.Matches( _deleteFF ) );
            Assert.False( rule.Matches( _delete00 ) );
            Assert.False( rule.Matches( new byte[] { 0x1A, 0, 0, 0, 0, 0xFF } ) );
        }

        [Fact]
        public void OutOfRangeConditionDoesNotRuleThePacketOut()
        {
            PacketWaitRule rule = PacketWaitRule.FromPattern( "1D ?? ?? ?? ?? ?? ?? ?? 01" );

            Assert.True( rule.Matches( _delete00 ) );
        }

        [Fact]
        public void NegatedConditionDoesNotNarrowTheRule()
        {
            // The UI's PacketFilter ignores Negate, so the plugin must not rely on it either
            PacketWaitRule rule = new( 0x1D, false, new PacketWaitCondition( 5, [0xFF], true ) );

            Assert.True( rule.Matches( _deleteFF ) );
            Assert.True( rule.Matches( _delete00 ) );
        }

        [Fact]
        public void WaitForEverythingWaitsOnAnything()
        {
            Assert.True( PacketWaitRuleSet.WaitForEverything.MustWait( _delete00, false ) );
            Assert.True( PacketWaitRuleSet.WaitForEverything.MustWait( new byte[] { 0xD6 }, true ) );
        }

        [Fact]
        public void RuleSetSeparatesDirectionsAndIds()
        {
            PacketWaitRuleSet set = PacketWaitRuleSet.Create( [PacketWaitRule.FromPattern( "65" ), PacketWaitRule.FromPattern( "06", true )] );

            Assert.Equal( 2, set.RuleCount );
            Assert.True( set.MustWait( new byte[] { 0x65, 0x01 }, false ) );
            Assert.False( set.MustWait( new byte[] { 0x65, 0x01 }, true ) );
            Assert.True( set.MustWait( new byte[] { 0x06, 0, 0, 0, 1 }, true ) );
            Assert.False( set.MustWait( new byte[] { 0x06, 0, 0, 0, 1 }, false ) );
            Assert.False( set.MustWait( new byte[] { 0xD6, 0x00 }, false ) );
        }

        [Fact]
        public void RuleSetMatchesAnyRuleForAnId()
        {
            PacketWaitRuleSet set = PacketWaitRuleSet.Create(
                [PacketWaitRule.FromPattern( "1D ?? ?? ?? ?? FF" ), PacketWaitRule.FromPattern( "1D ?? ?? ?? ?? 01" )] );

            Assert.True( set.MustWait( _deleteFF, false ) );
            Assert.False( set.MustWait( _delete00, false ) );
        }

        [Fact]
        public void UnconditionalRuleCoversEveryPacketWithTheId()
        {
            PacketWaitRuleSet set = PacketWaitRuleSet.Create( [PacketWaitRule.FromPattern( "1D ?? ?? ?? ?? FF" ), new PacketWaitRule( 0x1D )] );

            Assert.True( set.MustWait( _delete00, false ) );
        }

        [Fact]
        public void EmptyRuleSetWaitsOnNothing()
        {
            PacketWaitRuleSet set = PacketWaitRuleSet.Create( null );

            Assert.False( set.WaitsForEverything );
            Assert.False( set.MustWait( _deleteFF, false ) );
            Assert.False( set.MustWait( ReadOnlySpan<byte>.Empty, false ) );
        }

        [Fact]
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

            Assert.Equal( 4, writer.Count );

            List<PacketBatchEntry> entries = PacketBatchWriter.Read( writer.ToArray() );

            Assert.Equal( 4, entries.Count );
            Assert.Equal( new byte[] { 0xD6, 1, 2, 3 }, entries[0].Packet );
            Assert.False( entries[0].Outgoing );
            Assert.Equal( new byte[] { 0x06, 0, 0, 0, 9 }, entries[1].Packet );
            Assert.True( entries[1].Outgoing );
            Assert.Empty( entries[2].Packet );
            Assert.Equal( large, entries[3].Packet );
        }

        [Fact]
        public void BatchClearStartsAgain()
        {
            PacketBatchWriter writer = new();

            writer.Add( new byte[] { 0xD6 }, false );
            writer.Clear();
            writer.Add( new byte[] { 0xDC }, false );

            List<PacketBatchEntry> entries = PacketBatchWriter.Read( writer.ToArray() );

            Assert.Single( entries );
            Assert.Equal( 0xDC, entries[0].Packet[0] );
        }

        [Fact]
        public void TruncatedBatchThrows()
        {
            PacketBatchWriter writer = new();
            writer.Add( new byte[] { 0xD6, 1, 2, 3 }, false );

            byte[] packed = writer.ToArray();

            Assert.Throws<FormatException>( () => PacketBatchWriter.Read( packed.AsSpan( 0, packed.Length - 1 ).ToArray() ) );
            Assert.Empty( PacketBatchWriter.Read( null ) );
        }
    }
}
