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
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Shared;
using StreamJsonRpc;

// ReSharper disable LocalizableElement

namespace PacketTransportBenchmark
{
    /// <summary>
    ///     Replays a CACL packet log (<c>--packet-log --packet-log-relay</c>, format in CACL's
    ///     docs/packet-log.md) through the plugin -> UI transport. The client-leg packets are the exact
    ///     stream one UO client received and sent, so each becomes an OnPacketReceive / OnPacketSend
    ///     call made the way <c>PluginEngine.Filter</c> makes it: synchronously, blocking the caller.
    ///     <para>
    ///         Packets the server delivered together arrive at the client together and are filtered
    ///         back to back on its thread, so the number that matters is the summed round trip per
    ///         burst: that is how long the client's frame is held up. Bursts are replayed at their
    ///         original spacing (pass <c>fast</c> to skip the idle gaps), because idle time between
    ///         them is what lets the UI side's threads go cold.
    ///     </para>
    ///     <para>
    ///         Usage: <c>replay &lt;log.jsonl&gt; [json|messagepack] [inprocess] [fast] [rules=&lt;file|default&gt;]
    ///         [notify=D6,DC [batch]]</c>. Defaults are the product's: MessagePack, UI in a separate
    ///         process, and with no rules every packet waited for (today's behaviour).
    ///     </para>
    ///     <para>
    ///         <c>rules=</c> decides per packet the way the plugin does: <see cref="PacketWaitRuleSet.MustWait" />
    ///         against the rule set, waited packets as blocking calls, everything else packed with
    ///         <see cref="PacketBatchWriter" /> into one-way <see cref="IPluginMethods.OnPacketBatch" />
    ///         messages. The batch is flushed before every waited packet (so order is kept), at the end
    ///         of each burst, and once it passes <see cref="BatchFlushBytes" /> or
    ///         <see cref="BatchFlushPackets" />. A rules file holds one <see cref="PacketWaitRule.FromPattern" />
    ///         pattern per line, prefixed <c>&gt;</c> for outgoing (<c>&lt;</c> or nothing for incoming),
    ///         with <c>#</c> comments; <c>default</c> approximates a default profile.
    ///     </para>
    ///     <para>
    ///         <c>notify=</c> is the cruder experiment: the listed ids are not waited for and every
    ///         other id is, each sent as its own notification unless <c>batch</c> is also given.
    ///     </para>
    /// </summary>
    internal static partial class Program
    {
        // Gap in CACL's send times that separates two deliveries to the client
        private const double BurstGapMs = 2.0;

        // One frame at 60 fps
        private const double FrameMs = 1000.0 / 60;

        // Flush thresholds for a pending batch, matching the plugin's
        private const int BatchFlushBytes = 64 * 1024;
        private const int BatchFlushPackets = 512;

        /// <summary>
        ///     Roughly what a default profile asks the plugin to wait on: the filters that are on out of
        ///     the box (weather, light level) and the outgoing packets the UI always inspects.
        /// </summary>
        private static readonly string[] DefaultRules =
        [
            "< 65", // weather filter
            "< 4E", // light level filter: personal light
            "< 4F", // light level filter: global light
            "> 05", // attack request
            "> 06", // use request
            "> 80", // account login
            "> 91", // game server login
            "> 03", // speech, checked for commands
            "> AD" // unicode speech, checked for commands
        ];

        private static async Task RunReplay( string path, string[] args )
        {
            FormatterKind formatter = args.Length >= 3 && ( args[2] == "json" || args[2] == "messagepack" || args[2] == "msgpack" )
                ? ParseFormatter( args[2] )
                : FormatterKind.MessagePack;
            Transport transport = Array.IndexOf( args, "inprocess" ) >= 0 ? Transport.InProcess : Transport.CrossProcess;
            bool fast = Array.IndexOf( args, "fast" ) >= 0;
            bool batch = Array.IndexOf( args, "batch" ) >= 0;

            string rulesArg = args.FirstOrDefault( a => a.StartsWith( "rules=" ) )?["rules=".Length..];
            string notifyArg = args.FirstOrDefault( a => a.StartsWith( "notify=" ) )?["notify=".Length..];
            List<PacketWaitRule> ruleList = null;

            if ( rulesArg != null )
            {
                ruleList = ParseRules( rulesArg == "default" ? DefaultRules : File.ReadAllLines( rulesArg ) );
                batch = true; // what the plugin does with everything it doesn't wait for
            }
            else if ( notifyArg != null )
            {
                // Everything not listed is waited for, both directions
                HashSet<byte> notified = notifyArg.Split( ',', StringSplitOptions.RemoveEmptyEntries )
                    .Select( id => Convert.ToByte( id, 16 ) ).ToHashSet();

                ruleList = Enumerable.Range( 0, 0x100 ).Where( id => !notified.Contains( (byte) id ) )
                    .SelectMany( id => new[] { new PacketWaitRule( (byte) id ), new PacketWaitRule( (byte) id, true ) } ).ToList();
            }

            PacketWaitRuleSet waitRules = ruleList == null ? PacketWaitRuleSet.WaitForEverything : PacketWaitRuleSet.Create( ruleList );

            List<ReplayPacket> packets = LoadClientLeg( path, out int client );
            List<List<ReplayPacket>> bursts = SplitBursts( packets );

            Console.WriteLine( $"log: {Path.GetFileName( path )}, client {client}: {packets.Count} packets " +
                               $"({packets.Count( p => !p.Outgoing )} s2c, {packets.Count( p => p.Outgoing )} c2s) in {bursts.Count} bursts, " +
                               $"{( packets[^1].Time - packets[0].Time ) / 1000:F1} s" );
            Console.WriteLine( $"transport: {formatter} / {transport}, pacing: {( fast ? "back to back" : "original" )}" );

            if ( rulesArg != null )
            {
                Console.WriteLine( $"wait rules ({rulesArg}, everything else batched): {string.Join( ", ", ruleList.Select( r => r.ToString() ) )}" );
            }
            else if ( notifyArg != null )
            {
                Console.WriteLine( $"one-way (not waited for): {notifyArg}{( batch ? ", batched" : ", one notification each" )}" );
            }
            else
            {
                Console.WriteLine( "wait rules: none, every packet waited for" );
            }

            Console.WriteLine();

            string pipeName = $"CAPacketReplay_{Guid.NewGuid():N}";

            using NamedPipeServerStream server = new(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous );

            Process child = transport == Transport.CrossProcess ? StartUiProcess( pipeName, formatter ) : null;
            Task uiTask = transport == Transport.InProcess ? Task.Run( () => ConnectAndServe( pipeName, formatter ) ) : null;

            await server.WaitForConnectionAsync();

            using JsonRpc rpc = new( CreateHandler( server, formatter ) );
            rpc.StartListening();

            IPluginMethods plugin = rpc.Attach<IPluginMethods>();

            byte[] warmup = [0x1B, 0, 0, 0, 0];

            for ( int i = 0; i < Warmup; i++ )
            {
                _ = Call( plugin, warmup );
            }

            PacketBatchWriter pending = new();

            // Sends what has been batched as one one-way message, as the plugin does
            void Flush()
            {
                if ( pending.Count == 0 )
                {
                    return;
                }

                _ = rpc.NotifyAsync( nameof( IPluginMethods.OnPacketBatch ), new object[] { pending.ToArray() } );
                pending.Clear();
            }

            double[] callUs = new double[packets.Count];
            double[] burstMs = new double[bursts.Count];
            int index = 0;

            Stopwatch clock = Stopwatch.StartNew();
            double origin = bursts[0][0].Time;

            for ( int b = 0; b < bursts.Count; b++ )
            {
                List<ReplayPacket> burst = bursts[b];

                if ( !fast )
                {
                    WaitUntil( clock, burst[0].Time - origin );
                }

                long burstStart = Stopwatch.GetTimestamp();

                foreach ( ReplayPacket packet in burst )
                {
                    long start = Stopwatch.GetTimestamp();
                    bool wait = waitRules.MustWait( packet.Data, packet.Outgoing );

                    if ( !wait && batch )
                    {
                        pending.Add( packet.Data, packet.Outgoing );

                        if ( pending.Length >= BatchFlushBytes || pending.Count >= BatchFlushPackets )
                        {
                            Flush();
                        }
                    }
                    else if ( !wait )
                    {
                        // Queued on the pipe in order; the caller moves on as soon as it is written
                        _ = rpc.NotifyAsync( packet.Outgoing ? nameof( IPluginMethods.OnPacketSend ) : nameof( IPluginMethods.OnPacketReceive ),
                            packet.Data, packet.Data.Length, (long) Environment.TickCount );
                    }
                    else if ( packet.Outgoing )
                    {
                        Flush();
                        _ = plugin.OnPacketSend( packet.Data, packet.Data.Length, Environment.TickCount ).GetAwaiter().GetResult();
                    }
                    else
                    {
                        Flush();
                        _ = Call( plugin, packet.Data );
                    }

                    callUs[index++] = Stopwatch.GetElapsedTime( start ).TotalMicroseconds;
                }

                Flush();

                burstMs[b] = Stopwatch.GetElapsedTime( burstStart ).TotalMilliseconds;
            }

            rpc.Dispose();

            try
            {
                if ( uiTask != null )
                {
                    await uiTask;
                }
            }
            catch ( Exception )
            {
                // The UI side sees the pipe close when the plugin is done; that is expected.
            }

            child?.WaitForExit( 3000 );

            ReportReplay( packets, callUs, bursts, burstMs );
        }

        private static void ReportReplay( List<ReplayPacket> packets, double[] callUs, List<List<ReplayPacket>> bursts, double[] burstMs )
        {
            double[] sortedCalls = callUs.OrderBy( v => v ).ToArray();

            Console.WriteLine( "per call (us): " +
                               $"p50 {Percentile( sortedCalls, 0.5 ):F1}, p90 {Percentile( sortedCalls, 0.9 ):F1}, " +
                               $"p99 {Percentile( sortedCalls, 0.99 ):F1}, p99.9 {Percentile( sortedCalls, 0.999 ):F1}, max {sortedCalls[^1]:F1}" );

            double[] sortedBursts = burstMs.OrderBy( v => v ).ToArray();
            int[] sizes = bursts.Select( b => b.Count ).OrderBy( v => v ).ToArray();

            Console.WriteLine( $"burst size: p50 {sizes[sizes.Length / 2]}, p99 {sizes[(int) ( sizes.Length * 0.99 )]}, max {sizes[^1]}" );
            Console.WriteLine( "client blocked per burst (ms): " +
                               $"p50 {Percentile( sortedBursts, 0.5 ):F2}, p90 {Percentile( sortedBursts, 0.9 ):F2}, " +
                               $"p99 {Percentile( sortedBursts, 0.99 ):F2}, max {sortedBursts[^1]:F2}" );
            Console.WriteLine( $"bursts blocking longer than one 60 fps frame ({FrameMs:F1} ms): {burstMs.Count( v => v > FrameMs )}, " +
                               $"> 33 ms: {burstMs.Count( v => v > 33 )}, > 100 ms: {burstMs.Count( v => v > 100 )}" );

            double totalBlocked = burstMs.Sum();
            double duration = packets[^1].Time - packets[0].Time;

            Console.WriteLine( $"total client time blocked: {totalBlocked:F0} ms of {duration:F0} ms ({totalBlocked / duration * 100:F2}%)" );

            Console.WriteLine();
            Console.WriteLine( "by packet id (top 10 by total blocked time):" );
            Console.WriteLine( "  id  dir     count  total_ms   avg_us   p99_us" );

            foreach ( var group in packets.Select( ( p, i ) => (p, us: callUs[i]) )
                         .GroupBy( x => (x.p.Data[0], x.p.Outgoing) )
                         .OrderByDescending( g => g.Sum( x => x.us ) )
                         .Take( 10 ) )
            {
                double[] us = group.Select( x => x.us ).OrderBy( v => v ).ToArray();

                Console.WriteLine( $"  {group.Key.Item1:X2}  {( group.Key.Outgoing ? "c2s" : "s2c" )} {us.Length,9} {us.Sum() / 1000,9:F1} {us.Average(),8:F1} {Percentile( us, 0.99 ),8:F1}" );
            }

            Console.WriteLine();
            Console.WriteLine( "worst 10 bursts:" );
            Console.WriteLine( "  t_s      packets  blocked_ms  top ids" );

            foreach ( int b in Enumerable.Range( 0, bursts.Count ).OrderByDescending( i => burstMs[i] ).Take( 10 ) )
            {
                string ids = string.Join( ", ", bursts[b].GroupBy( p => p.Data[0] ).OrderByDescending( g => g.Count() ).Take( 3 )
                    .Select( g => $"{g.Key:X2} x{g.Count()}" ) );

                Console.WriteLine( $"  {bursts[b][0].Time / 1000,7:F1} {bursts[b].Count,8} {burstMs[b],11:F2}  {ids}" );
            }
        }

        /// <summary>One pattern per line; '>' marks outgoing, '<' or nothing incoming, '#' starts a comment.</summary>
        private static List<PacketWaitRule> ParseRules( IEnumerable<string> lines )
        {
            List<PacketWaitRule> rules = [];

            foreach ( string raw in lines )
            {
                int comment = raw.IndexOf( '#' );
                string line = ( comment >= 0 ? raw[..comment] : raw ).Trim();

                if ( line.Length == 0 )
                {
                    continue;
                }

                bool outgoing = line[0] == '>';

                if ( line[0] is '>' or '<' )
                {
                    line = line[1..].Trim();
                }

                rules.Add( PacketWaitRule.FromPattern( line, outgoing ) );
            }

            return rules;
        }

        private static double Percentile( double[] sorted, double p )
        {
            return sorted[Math.Min( sorted.Length - 1, (int) ( sorted.Length * p ) )];
        }

        private static void WaitUntil( Stopwatch clock, double targetMs )
        {
            double remaining = targetMs - clock.Elapsed.TotalMilliseconds;

            if ( remaining > 2 )
            {
                Thread.Sleep( TimeSpan.FromMilliseconds( remaining - 1.5 ) );
            }

            while ( clock.Elapsed.TotalMilliseconds < targetMs )
            {
                Thread.SpinWait( 50 );
            }
        }

        /// <summary>
        ///     The client leg of the busiest relayed client: s2c is what the client received (an
        ///     OnPacketReceive in the plugin), c2s what it sent (an OnPacketSend). Ordered by time;
        ///     the sort is stable, so ties keep file order.
        /// </summary>
        private static List<ReplayPacket> LoadClientLeg( string path, out int client )
        {
            List<(int Client, ReplayPacket Packet)> all = [];

            foreach ( string line in File.ReadLines( path ) )
            {
                if ( string.IsNullOrWhiteSpace( line ) )
                {
                    continue;
                }

                JsonElement json;

                try
                {
                    using JsonDocument document = JsonDocument.Parse( line );
                    json = document.RootElement.Clone();
                }
                catch ( JsonException )
                {
                    // A crash can leave the last line cut off
                    break;
                }

                if ( json.GetProperty( "type" ).GetString() != "pkt" || json.GetProperty( "leg" ).GetString() != "client" )
                {
                    continue;
                }

                all.Add( (json.GetProperty( "client" ).GetInt32(),
                    new ReplayPacket( json.GetProperty( "t" ).GetDouble(), json.GetProperty( "dir" ).GetString() == "c2s",
                        Convert.FromHexString( json.GetProperty( "data" ).GetString()! ) )) );
            }

            if ( all.Count == 0 )
            {
                throw new InvalidDataException( "No client-leg packets: the log needs --packet-log-relay." );
            }

            client = all.GroupBy( x => x.Client ).OrderByDescending( g => g.Count() ).First().Key;
            int chosen = client;

            return all.Where( x => x.Client == chosen ).Select( x => x.Packet ).OrderBy( p => p.Time ).ToList();
        }

        private static List<List<ReplayPacket>> SplitBursts( List<ReplayPacket> packets )
        {
            List<List<ReplayPacket>> bursts = [[packets[0]]];

            for ( int i = 1; i < packets.Count; i++ )
            {
                if ( packets[i].Time - packets[i - 1].Time > BurstGapMs )
                {
                    bursts.Add( [] );
                }

                bursts[^1].Add( packets[i] );
            }

            return bursts;
        }

        private sealed record ReplayPacket( double Time, bool Outgoing, byte[] Data );
    }
}
