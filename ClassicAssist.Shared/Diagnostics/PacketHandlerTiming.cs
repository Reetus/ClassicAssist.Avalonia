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

namespace ClassicAssist.Shared.Diagnostics;

/// <summary>
///     UI-side counterpart to the plugin's packet latency log: how long the UI's
///     <c>PluginMethods.OnPacketReceive</c> / <c>OnPacketSend</c> handler itself took.
///     <para>
///         The plugin log gives the whole round trip but cannot say where the time went. Comparing
///         the two logs by timestamp tells a slow handler apart from a request that merely sat in
///         the UI's dispatch queue, a GC pause, or the transport: a 239 ms round trip with a 0.1 ms
///         handler line beside it is not the handler.
///     </para>
///     <para>
///         Same shape as the plugin's log: one line per handler at or above
///         <see cref="SlowThresholdMs" />, plus a summary every <see cref="SummarySeconds" /> seconds
///         carrying the interval max and which packet it was. Written to <c>Logs</c> beside the UI.
///     </para>
/// </summary>
internal static class PacketHandlerTiming
{
    private const int MaxSamples = 200000;

    private static readonly Lock Sync = new();
    private static readonly List<long> Samples = [];

    private static StreamWriter _writer;
    private static Thread _summarizer;
    private static bool _started;
    private static int _summarySeconds;

    private static int _intervalCount;
    private static long _intervalTotalTicks;
    private static long _intervalMaxTicks;
    private static byte _intervalMaxId;
    private static bool _intervalMaxOutgoing;
    private static long _intervalMaxRequestMs;
    private static byte _intervalMaxRequestId;
    private static bool _intervalMaxRequestOutgoing;

    private static long _totalCount;
    private static long _totalTicks;
    private static long _totalMaxTicks;

    private static double _lastGcPauseMs;
    private static int _lastGen2;

    /// <summary>
    ///     Code switch, like the plugin's <c>PacketDiagnostics</c>. Off by default; set true to
    ///     investigate, then leave false.
    /// </summary>
    public static bool Enabled { get; set; } = false;

    public static double SlowThresholdMs { get; set; } = 0.5;

    public static int SummarySeconds { get; set; } = 5;

    public static void Record( bool outgoing, byte id, long elapsedTicks, long requestLatencyMs )
    {
        if ( !Enabled )
        {
            return;
        }

        try
        {
            lock ( Sync )
            {
                EnsureStarted();

                double ms = TicksToMs( elapsedTicks );

                _intervalCount++;
                _intervalTotalTicks += elapsedTicks;
                _totalCount++;
                _totalTicks += elapsedTicks;

                if ( elapsedTicks > _intervalMaxTicks )
                {
                    _intervalMaxTicks = elapsedTicks;
                    _intervalMaxId = id;
                    _intervalMaxOutgoing = outgoing;
                }

                if ( elapsedTicks > _totalMaxTicks )
                {
                    _totalMaxTicks = elapsedTicks;
                }

                if ( requestLatencyMs > _intervalMaxRequestMs )
                {
                    _intervalMaxRequestMs = requestLatencyMs;
                    _intervalMaxRequestId = id;
                    _intervalMaxRequestOutgoing = outgoing;
                }

                if ( Samples.Count < MaxSamples )
                {
                    Samples.Add( elapsedTicks );
                }

                if ( ms >= SlowThresholdMs )
                {
                    string request = requestLatencyMs >= 0 ? $" req={requestLatencyMs}ms" : string.Empty;

                    Write( string.Format( CultureInfo.InvariantCulture, "{0} SLOW {1} id=0x{2:X2} {3:0.###}ms{4}",
                        Now(), outgoing ? "send" : "recv", id, ms, request ) );
                }
            }
        }
        catch ( Exception )
        {
            // Diagnostics must never take the UI down.
        }
    }

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
                        "# stopped {0}; session handlers={1} avg={2:0.###} max={3:0.###} ms", Now(), _totalCount,
                        avg, TicksToMs( _totalMaxTicks ) ) );

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
        _summarySeconds = Math.Max( 1, SummarySeconds );

        string directory = Path.Combine( AppContext.BaseDirectory, "Logs" );

        Directory.CreateDirectory( directory );

        string path = Path.Combine( directory, $"packet-handler-{DateTime.Now:yyyyMMdd-HHmmss}.log" );

        _writer = new StreamWriter(
            new FileStream( path, FileMode.Create, FileAccess.Write, FileShare.Read ) )
        { AutoFlush = false };

        Write( "# ClassicAssist UI packet-handler timing" );
        Write( $"# started {DateTime.Now:yyyy-MM-dd HH:mm:ss}" );
        Write( string.Format( CultureInfo.InvariantCulture,
            "# per-handler lines at >= {0:0.###} ms; summary every {1} s", SlowThresholdMs, _summarySeconds ) );
        Write( "# columns: time level direction id handler_ms" );
        _writer.Flush();

        _lastGcPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds;
        _lastGen2 = GC.CollectionCount( 2 );

        _summarizer = new Thread( SummarizeLoop ) { IsBackground = true, Name = "ClassicAssist handler timing" };
        _summarizer.Start();
    }

    private static void SummarizeLoop()
    {
        while ( true )
        {
            Thread.Sleep( _summarySeconds * 1000 );

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
                // Keep the loop alive.
            }
        }
    }

    private static void WriteSummary()
    {
        double avg = TicksToMs( _intervalTotalTicks ) / _intervalCount;
        double p99 = 0;

        if ( Samples.Count > 0 )
        {
            long[] sorted = [.. Samples];

            Array.Sort( sorted );

            int index = (int) ( sorted.Length * 0.99 );

            if ( index >= sorted.Length )
            {
                index = sorted.Length - 1;
            }

            p99 = TicksToMs( sorted[index] );
        }

        StringBuilder builder = new();

        builder.Append( Now() ).Append( " SUMMARY " ).Append( _intervalCount ).Append( " handlers/" )
            .Append( _summarySeconds ).Append( 's' );
        builder.Append( " avg=" ).Append( avg.ToString( "0.###", CultureInfo.InvariantCulture ) );
        builder.Append( " p99=" ).Append( p99.ToString( "0.###", CultureInfo.InvariantCulture ) );
        builder.Append( " max=" ).Append( TicksToMs( _intervalMaxTicks ).ToString( "0.###", CultureInfo.InvariantCulture ) )
            .Append( "ms (" ).Append( _intervalMaxOutgoing ? "send" : "recv" ).Append( " id=0x" )
            .Append( _intervalMaxId.ToString( "X2" ) ).Append( ')' );

        // Largest request latency in the interval. If this is near the round-trip max, the delay is
        // before the handler (queue/transport); if it is ~0, the wait was on the response.
        builder.Append( " max_req=" ).Append( _intervalMaxRequestMs ).Append( "ms (" )
            .Append( _intervalMaxRequestOutgoing ? "send" : "recv" ).Append( " id=0x" )
            .Append( _intervalMaxRequestId.ToString( "X2" ) ).Append( ')' );

        // GC pause time for the interval. If a 300 ms round trip lands in an interval whose
        // gc_pause is near zero, the stall is not collection - it is dispatch/transport.
        double gcPause = GC.GetTotalPauseDuration().TotalMilliseconds;
        int gen2 = GC.CollectionCount( 2 );

        builder.Append( " gc_pause=" )
            .Append( ( gcPause - _lastGcPauseMs ).ToString( "0.###", CultureInfo.InvariantCulture ) )
            .Append( "ms gen2=" ).Append( gen2 - _lastGen2 );

        _lastGcPauseMs = gcPause;
        _lastGen2 = gen2;

        _writer.WriteLine( builder.ToString() );
        _writer.Flush();

        _intervalCount = 0;
        _intervalTotalTicks = 0;
        _intervalMaxTicks = 0;
        _intervalMaxId = 0;
        _intervalMaxOutgoing = false;
        _intervalMaxRequestMs = 0;
        _intervalMaxRequestId = 0;
        _intervalMaxRequestOutgoing = false;
        Samples.Clear();
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
