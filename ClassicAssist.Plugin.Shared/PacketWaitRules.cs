#region License

// Copyright (C) 2026 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY

#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ClassicAssist.Plugin.Shared
{
    /// <summary>
    ///     Bytes a packet must (or, with <see cref="Negate" />, must not) carry at an absolute offset.
    ///     The same shape as the UI's PacketFilterCondition, so a runtime filter converts one-to-one.
    /// </summary>
    public sealed class PacketWaitCondition
    {
        public PacketWaitCondition()
        {
        }

        public PacketWaitCondition( int position, byte[] bytes, bool negate = false )
        {
            Position = position;
            Bytes = bytes;
            Negate = negate;
        }

        public int Position { get; set; }
        public byte[] Bytes { get; set; }
        public bool Negate { get; set; }

        /// <summary>
        ///     True when this condition does not rule the packet out. Deliberately generous - see
        ///     <see cref="PacketWaitRule.Matches" />.
        /// </summary>
        internal bool Allows( ReadOnlySpan<byte> packet )
        {
            // The UI's own matcher ignores Negate, so a negated condition never narrows the rule
            if ( Negate || Bytes == null || Bytes.Length == 0 )
            {
                return true;
            }

            // Out of range: the UI's matcher can still match on a later condition, so don't rule it out
            if ( Position < 0 || Position + Bytes.Length > packet.Length )
            {
                return true;
            }

            return packet.Slice( Position, Bytes.Length ).SequenceEqual( Bytes );
        }
    }

    /// <summary>
    ///     A packet the UI may drop or rewrite, so the plugin must wait for its answer instead of
    ///     batching it. Every condition must hold; none (null or empty) means every packet with
    ///     <see cref="PacketId" /> in that direction.
    /// </summary>
    public sealed class PacketWaitRule
    {
        public PacketWaitRule()
        {
        }

        public PacketWaitRule( byte packetId, bool outgoing = false, params PacketWaitCondition[] conditions )
        {
            PacketId = packetId;
            Outgoing = outgoing;

            // An empty params array means no conditions; keep that as null on the wire
            Conditions = conditions == null || conditions.Length == 0 ? null : conditions;
        }

        public byte PacketId { get; set; }
        public bool Outgoing { get; set; }
        public PacketWaitCondition[] Conditions { get; set; }

        /// <summary>
        ///     Whether the packet could be one this rule describes.
        ///     <para>
        ///         Errs towards true. Waiting on a packet the UI then leaves alone only costs a round
        ///         trip; batching one the UI meant to drop or rewrite silently breaks a filter. So a
        ///         condition only rules a packet out when it is in range, not negated, and its bytes
        ///         differ.
        ///     </para>
        /// </summary>
        public bool Matches( ReadOnlySpan<byte> packet )
        {
            if ( packet.IsEmpty || packet[0] != PacketId )
            {
                return false;
            }

            if ( Conditions == null )
            {
                return true;
            }

            foreach ( PacketWaitCondition condition in Conditions )
            {
                if ( condition != null && !condition.Allows( packet ) )
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     Parses a byte pattern such as <c>"1D ?? ?? ?? ?? FF"</c> or <c>"BF .. .. 00 08"</c>: the
        ///     first token is the packet id, then one token per byte, where <c>??</c>, <c>..</c> or
        ///     <c>*</c> match anything. Runs of literal bytes become one condition each.
        /// </summary>
        public static PacketWaitRule FromPattern( string pattern, bool outgoing = false )
        {
            if ( string.IsNullOrWhiteSpace( pattern ) )
            {
                throw new ArgumentException( "Empty packet pattern.", nameof( pattern ) );
            }

            string[] tokens = pattern.Split( new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries );
            List<PacketWaitCondition> conditions = new List<PacketWaitCondition>();
            List<byte> run = new List<byte>();
            int runStart = -1;

            for ( int i = 1; i < tokens.Length; i++ )
            {
                if ( IsWildcard( tokens[i] ) )
                {
                    FlushRun();

                    continue;
                }

                if ( runStart < 0 )
                {
                    runStart = i;
                }

                run.Add( ParseByte( tokens[i], pattern ) );
            }

            FlushRun();

            return new PacketWaitRule( ParseByte( tokens[0], pattern ), outgoing, conditions.Count == 0 ? null : conditions.ToArray() );

            void FlushRun()
            {
                if ( run.Count == 0 )
                {
                    return;
                }

                conditions.Add( new PacketWaitCondition( runStart, run.ToArray() ) );
                run.Clear();
                runStart = -1;
            }
        }

        /// <summary>The rule as a pattern (negated conditions appended), for logs and tests.</summary>
        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append( Outgoing ? "> " : "< " ).Append( PacketId.ToString( "X2" ) );

            PacketWaitCondition[] positive = ( Conditions ?? Array.Empty<PacketWaitCondition>() )
                .Where( c => c != null && !c.Negate && c.Bytes != null && c.Bytes.Length > 0 ).ToArray();

            int end = positive.Length == 0 ? 1 : positive.Max( c => c.Position + c.Bytes.Length );

            for ( int position = 1; position < end; position++ )
            {
                PacketWaitCondition covering =
                    positive.FirstOrDefault( c => position >= c.Position && position < c.Position + c.Bytes.Length );

                builder.Append( ' ' ).Append( covering == null ? "??" : covering.Bytes[position - covering.Position].ToString( "X2" ) );
            }

            foreach ( PacketWaitCondition negated in ( Conditions ?? Array.Empty<PacketWaitCondition>() ).Where( c => c != null && c.Negate ) )
            {
                builder.Append( $" !{BitConverter.ToString( negated.Bytes ?? Array.Empty<byte>() ).Replace( "-", "" )}@{negated.Position}" );
            }

            return builder.ToString();
        }

        private static bool IsWildcard( string token )
        {
            return token == "??" || token == ".." || token == "*" || token == "?";
        }

        private static byte ParseByte( string token, string pattern )
        {
            string hex = token.StartsWith( "0x", StringComparison.OrdinalIgnoreCase ) ? token.Substring( 2 ) : token;

            if ( hex.Length == 0 || hex.Length > 2 ||
                 !byte.TryParse( hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value ) )
            {
                throw new FormatException( $"'{token}' is not a hex byte in packet pattern \"{pattern}\"." );
            }

            return value;
        }
    }

    /// <summary>
    ///     The compiled form of a rule set, answering "must the plugin wait for this packet?" with a
    ///     table lookup per packet id. Immutable, so the plugin can swap a new one in from the RPC
    ///     thread while the client thread reads the old one.
    /// </summary>
    public sealed class PacketWaitRuleSet
    {
        private readonly Entry[] _incoming = new Entry[0x100];
        private readonly Entry[] _outgoing = new Entry[0x100];
        private readonly bool _waitForEverything;

        private PacketWaitRuleSet( bool waitForEverything )
        {
            _waitForEverything = waitForEverything;
        }

        /// <summary>
        ///     Wait on every packet: the behaviour before any rules arrive, and the safe answer if the
        ///     UI never sends any.
        /// </summary>
        public static PacketWaitRuleSet WaitForEverything { get; } = new PacketWaitRuleSet( true );

        public bool WaitsForEverything => _waitForEverything;

        public int RuleCount { get; private set; }

        public static PacketWaitRuleSet Create( IEnumerable<PacketWaitRule> rules )
        {
            PacketWaitRuleSet set = new PacketWaitRuleSet( false );

            if ( rules == null )
            {
                return set;
            }

            Dictionary<int, List<PacketWaitRule>> grouped = new Dictionary<int, List<PacketWaitRule>>();

            foreach ( PacketWaitRule rule in rules )
            {
                if ( rule == null )
                {
                    continue;
                }

                int key = ( rule.Outgoing ? 0x100 : 0 ) | rule.PacketId;

                if ( !grouped.TryGetValue( key, out List<PacketWaitRule> list ) )
                {
                    grouped[key] = list = new List<PacketWaitRule>();
                }

                list.Add( rule );
                set.RuleCount++;
            }

            foreach ( KeyValuePair<int, List<PacketWaitRule>> pair in grouped )
            {
                Entry[] table = ( pair.Key & 0x100 ) != 0 ? set._outgoing : set._incoming;

                // A rule without conditions already covers every packet with this id
                table[pair.Key & 0xFF] = pair.Value.Any( r => r.Conditions == null || r.Conditions.All( c => c == null ) )
                    ? Entry.Always
                    : new Entry( pair.Value.ToArray() );
            }

            return set;
        }

        public bool MustWait( ReadOnlySpan<byte> packet, bool outgoing )
        {
            if ( _waitForEverything )
            {
                return true;
            }

            if ( packet.IsEmpty )
            {
                return false;
            }

            Entry entry = ( outgoing ? _outgoing : _incoming )[packet[0]];

            if ( entry == null )
            {
                return false;
            }

            if ( entry.IsAlways )
            {
                return true;
            }

            foreach ( PacketWaitRule rule in entry.Rules )
            {
                if ( rule.Matches( packet ) )
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class Entry
        {
            public static readonly Entry Always = new Entry( null );

            public Entry( PacketWaitRule[] rules )
            {
                Rules = rules;
            }

            public PacketWaitRule[] Rules { get; }

            public bool IsAlways => Rules == null;
        }
    }
}
