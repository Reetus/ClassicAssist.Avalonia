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
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace ClassicAssist.Plugin
{
    /// <summary>
    ///     Diagnostic for the plugin's packet round trip.
    ///     <para>
    ///         Every packet the client hands the plugin is forwarded to the UI over the pipe, and the
    ///         client's own thread blocks inside <c>PluginEngine.Filter</c> until the UI answers. This
    ///         records how long that answer took, so a stutter can be attributed to the transport / UI
    ///         instead of guessed at.
    ///     </para>
    ///     <para>
    ///         Only used while <see cref="PluginEngine.PacketDiagnostics" /> is set. When it is false the
    ///         packet path pays nothing but a static field read. When it is true, one line is written per
    ///         round trip at or above <see cref="PluginEngine.PacketDiagnosticsSlowThresholdMs" />, plus
    ///         an aggregate line every <see cref="PluginEngine.PacketDiagnosticsSummarySeconds" /> seconds.
    ///         Logging every packet on this thread would itself be the delay being measured, so slow lines
    ///         are capped per interval and the aggregate is the primary signal.
    ///     </para>
    ///     <para>
    ///         The log lands in a <c>Logs</c> folder beside the plugin, as
    ///         <c>packet-latency-YYYYMMDD-HHMMSS.log</c>.
    ///     </para>
    /// </summary>
    internal static class PacketLatencyLog
    {
        private const int IdSlots = 256;

        // [0] = outgoing (send), [1] = incoming (receive).
        private const int Directions = 2;

        // A pathological session where every packet is slow must not turn the diagnostic itself into a
        // stall, so stop writing detail lines once this many have gone out for the current interval.
        private const int MaxSlowLinesPerInterval = 1000;

        // Cap the retained samples used for percentiles. At high packet rates this bounds memory without
        // meaningfully changing p50/p99.
        private const int MaxSamples = 200000;

        private static readonly object Sync = new object();
        private static readonly int[] IntervalDirection = new int[Directions];
        private static readonly int[][] IdCount = { new int[IdSlots], new int[IdSlots] };
        private static readonly long[][] IdTotalTicks = { new long[IdSlots], new long[IdSlots] };
        private static readonly long[][] IdMaxTicks = { new long[IdSlots], new long[IdSlots] };
        private static readonly List<long> Samples = new List<long>();

        private static StreamWriter _writer;
        private static Thread _summarizer;
        private static bool _started;
        private static int _summarySeconds;

        // Current interval accumulators, reset every summary.
        private static int _intervalCount;
        private static int _intervalSlow;
        private static int _intervalSlowLines;
        private static int _intervalSuppressed;
        private static long _intervalTotalTicks;
        private static long _intervalMaxTicks;

        // Whole-session totals, for the final line.
        private static long _totalCount;
        private static long _totalSlow;
        private static long _totalTicks;
        private static long _totalMaxTicks;

        // Process-scheduling readouts for the interval. A response that takes 300 ms to get back to a
        // fast handler looks like the plugin process not being scheduled - these say whether it was.
        private static long _stallMs;
#if !NETFRAMEWORK
        private static double _lastGcPauseMs;
#endif
        private static int _lastGen2;

        /// <summary>
        ///     Records one round trip. Cheap enough to call on the packet thread: no allocation beyond the
        ///     occasional buffered slow line, and never throws.
        /// </summary>
        public static void Record( bool outgoing, byte id, int length, long elapsedTicks, bool accept,
            bool rewritten, bool failed = false )
        {
            try
            {
                double ms = TicksToMs( elapsedTicks );
                int direction = outgoing ? 0 : 1;
                bool slow = ms >= PluginEngine.PacketDiagnosticsSlowThresholdMs;

                lock ( Sync )
                {
                    EnsureStarted();

                    _intervalCount++;
                    IntervalDirection[direction]++;
                    _intervalTotalTicks += elapsedTicks;

                    if ( elapsedTicks > _intervalMaxTicks )
                    {
                        _intervalMaxTicks = elapsedTicks;
                    }

                    _totalCount++;
                    _totalTicks += elapsedTicks;

                    if ( elapsedTicks > _totalMaxTicks )
                    {
                        _totalMaxTicks = elapsedTicks;
                    }

                    if ( Samples.Count < MaxSamples )
                    {
                        Samples.Add( elapsedTicks );
                    }

                    IdCount[direction][id]++;
                    IdTotalTicks[direction][id] += elapsedTicks;

                    if ( elapsedTicks > IdMaxTicks[direction][id] )
                    {
                        IdMaxTicks[direction][id] = elapsedTicks;
                    }

                    if ( !slow )
                    {
                        return;
                    }

                    _intervalSlow++;
                    _totalSlow++;

                    if ( _intervalSlowLines >= MaxSlowLinesPerInterval )
                    {
                        _intervalSuppressed++;

                        return;
                    }

                    _intervalSlowLines++;

                    string result = failed ? "rpc-error" : !accept ? "block" : rewritten ? "rewrite" : "accept";

                    Write( string.Format( CultureInfo.InvariantCulture,
                        "{0} SLOW {1} id=0x{2:X2} len={3} {4:0.###}ms {5}", Now(), outgoing ? "send" : "recv",
                        id, length, ms, result ) );
                }
            }
            catch ( Exception )
            {
                // A diagnostic must never take the client down.
            }
        }

        /// <summary>
        ///     Flushes the final summary and closes the log. Safe to call more than once.
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                lock ( Sync )
                {
                    if ( _writer == null )
                    {
                        return;
                    }

                    try
                    {
                        if ( _intervalCount > 0 )
                        {
                            WriteSummary();
                        }

                        double avg = _totalCount > 0 ? TicksToMs( _totalTicks ) / _totalCount : 0;

                        Write( string.Format( CultureInfo.InvariantCulture,
                            "# stopped {0}; session packets={1} slow={2} avg={3:0.###} max={4:0.###} ms", Now(),
                            _totalCount, _totalSlow, avg, TicksToMs( _totalMaxTicks ) ) );

                        _writer.Flush();
                        _writer.Dispose();
                    }
                    finally
                    {
                        _writer = null;
                    }
                }
            }
            catch ( Exception )
            {
                // Shutting down; a diagnostic that cannot be flushed is not worth dying on.
            }
        }

        private static void EnsureStarted()
        {
            if ( _started )
            {
                return;
            }

            _started = true;
            _summarySeconds = Math.Max( 1, PluginEngine.PacketDiagnosticsSummarySeconds );

            string root = PluginEngine.StartupPath ?? AppContext.BaseDirectory;
            string directory = Path.Combine( root, "Logs" );

            Directory.CreateDirectory( directory );

            string path = Path.Combine( directory, $"packet-latency-{DateTime.Now:yyyyMMdd-HHmmss}.log" );

            _writer = new StreamWriter(
                new FileStream( path, FileMode.Create, FileAccess.Write, FileShare.Read ) ) { AutoFlush = false };

            Write( "# ClassicAssist packet round-trip diagnostics" );
            Write( $"# started {DateTime.Now:yyyy-MM-dd HH:mm:ss}" );
            Write( string.Format( CultureInfo.InvariantCulture,
                "# per-packet lines at >= {0:0.###} ms (max {1} per interval); summary every {2} s",
                PluginEngine.PacketDiagnosticsSlowThresholdMs, MaxSlowLinesPerInterval, _summarySeconds ) );
            Write( "# columns: time level direction id length elapsed_ms result" );
            _writer.Flush();

            _lastGen2 = GC.CollectionCount( 2 );
#if !NETFRAMEWORK
            _lastGcPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds;
#endif

            _summarizer = new Thread( SummarizeLoop ) { IsBackground = true, Name = "ClassicAssist packet diagnostics" };
            _summarizer.Start();
        }

        private static void SummarizeLoop()
        {
            while ( true )
            {
                // Sleep in short steps and watch for oversleep. A normal step is ~50 ms; a much larger
                // gap means the whole process was not being scheduled (a GC pause or a suspend), which
                // is what a delayed response looks like from the plugin side.
                long last = Stopwatch.GetTimestamp();
                long maxGapTicks = 0;
                int target = _summarySeconds * 1000;

                for ( int slept = 0; slept < target; slept += 50 )
                {
                    Thread.Sleep( 50 );

                    long now = Stopwatch.GetTimestamp();
                    long gap = now - last;

                    if ( gap > maxGapTicks )
                    {
                        maxGapTicks = gap;
                    }

                    last = now;
                }

                _stallMs = (long) TicksToMs( maxGapTicks ) - 50;

                if ( _stallMs < 0 )
                {
                    _stallMs = 0;
                }

                try
                {
                    lock ( Sync )
                    {
                        if ( _writer == null )
                        {
                            return;
                        }

                        if ( _intervalCount > 0 )
                        {
                            WriteSummary();
                        }
                    }
                }
                catch ( Exception )
                {
                    // Keep the loop alive; a bad summary should not kill the diagnostic.
                }
            }
        }

        private static void WriteSummary()
        {
            double avg = TicksToMs( _intervalTotalTicks ) / _intervalCount;

            double p50 = 0;
            double p99 = 0;

            if ( Samples.Count > 0 )
            {
                long[] sorted = Samples.ToArray();

                Array.Sort( sorted );

                p50 = TicksToMs( sorted[sorted.Length / 2] );

                int index = (int) ( sorted.Length * 0.99 );

                if ( index >= sorted.Length )
                {
                    index = sorted.Length - 1;
                }

                p99 = TicksToMs( sorted[index] );
            }

            StringBuilder builder = new StringBuilder();

            builder.Append( Now() ).Append( " SUMMARY " ).Append( _intervalCount ).Append( " pkts/" )
                .Append( _summarySeconds ).Append( 's' );

            builder.Append( " avg=" ).Append( avg.ToString( "0.###", CultureInfo.InvariantCulture ) );
            builder.Append( " p50=" ).Append( p50.ToString( "0.###", CultureInfo.InvariantCulture ) );
            builder.Append( " p99=" ).Append( p99.ToString( "0.###", CultureInfo.InvariantCulture ) );
            builder.Append( " max=" ).Append( TicksToMs( _intervalMaxTicks ).ToString( "0.###", CultureInfo.InvariantCulture ) )
                .Append( "ms" );

            builder.Append( " slow=" ).Append( _intervalSlow );

            if ( _intervalSuppressed > 0 )
            {
                builder.Append( " suppressed=" ).Append( _intervalSuppressed );
            }

            builder.Append( " send=" ).Append( IntervalDirection[0] ).Append( " recv=" ).Append( IntervalDirection[1] );

            // Process-scheduling readouts: if a 300 ms round trip lands in an interval with gc_pause or
            // stall near zero, the response did not wait on this process being scheduled.
#if !NETFRAMEWORK
            double gcPause = GC.GetTotalPauseDuration().TotalMilliseconds;

            builder.Append( " gc_pause=" )
                .Append( ( gcPause - _lastGcPauseMs ).ToString( "0.###", CultureInfo.InvariantCulture ) ).Append( "ms" );

            _lastGcPauseMs = gcPause;
#endif
            int gen2 = GC.CollectionCount( 2 );

            builder.Append( " gen2=" ).Append( gen2 - _lastGen2 ).Append( " stall=" ).Append( _stallMs ).Append( "ms" );

            _lastGen2 = gen2;

            builder.AppendLine();

            AppendTopIds( builder, 0, "send" );
            AppendTopIds( builder, 1, "recv" );

            _writer.Write( builder.ToString() );
            _writer.Flush();

            ResetInterval();
        }

        private static void AppendTopIds( StringBuilder builder, int direction, string label )
        {
            int[] counts = IdCount[direction];
            List<int> ids = new List<int>();

            for ( int i = 0; i < IdSlots; i++ )
            {
                if ( counts[i] > 0 )
                {
                    ids.Add( i );
                }
            }

            if ( ids.Count == 0 )
            {
                return;
            }

            ids.Sort( ( a, b ) => counts[b].CompareTo( counts[a] ) );

            int take = Math.Min( 5, ids.Count );

            for ( int i = 0; i < take; i++ )
            {
                int id = ids[i];
                double avg = TicksToMs( IdTotalTicks[direction][id] ) / counts[id];

                builder.AppendFormat( CultureInfo.InvariantCulture, "#   {0} id=0x{1:X2} n={2} avg={3:0.###} max={4:0.###} ms",
                    label, id, counts[id], avg, TicksToMs( IdMaxTicks[direction][id] ) ).AppendLine();
            }
        }

        private static void ResetInterval()
        {
            _intervalCount = 0;
            _intervalSlow = 0;
            _intervalSlowLines = 0;
            _intervalSuppressed = 0;
            _intervalTotalTicks = 0;
            _intervalMaxTicks = 0;
            IntervalDirection[0] = 0;
            IntervalDirection[1] = 0;

            Samples.Clear();

            Array.Clear( IdCount[0], 0, IdSlots );
            Array.Clear( IdCount[1], 0, IdSlots );
            Array.Clear( IdTotalTicks[0], 0, IdSlots );
            Array.Clear( IdTotalTicks[1], 0, IdSlots );
            Array.Clear( IdMaxTicks[0], 0, IdSlots );
            Array.Clear( IdMaxTicks[1], 0, IdSlots );
        }

        private static void Write( string line )
        {
            _writer?.WriteLine( line );
        }

        private static string Now()
        {
            return DateTime.Now.ToString( "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture );
        }

        private static double TicksToMs( long ticks )
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }
}
