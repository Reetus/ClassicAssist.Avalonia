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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Data.Commands;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Shared;
using ClassicAssist.UO.Network.PacketFilter;

namespace ClassicAssist.UO.Network;

/// <summary>
///     Tells the plugin which packets it must wait on the UI for: every packet something on the UI's
///     synchronous packet path might drop or rewrite. Everything else the plugin lets straight through
///     and delivers in batches (<see cref="Engine.PluginMethods.OnPacketBatch" />), so the client's
///     thread no longer pays a round trip per packet.
///     <para>
///         Sources: the runtime filters (<see cref="Engine.AddReceiveFilter" /> and friends), the
///         built-in <see cref="IncomingPacketFilters" /> and <see cref="OutgoingPacketFilters" />, the
///         option filters (<see cref="ClassicAssist.Data.Filters.DynamicFilterEntry" />) and speech
///         commands. Each errs towards waiting: a needless wait costs a round trip, a missing one
///         silently breaks a filter.
///     </para>
///     <para>
///         The full set is rebuilt and pushed whenever something changes. A runtime filter is pushed
///         synchronously, so a macro's filter is live before the macro carries on; everything else is
///         coalesced. A periodic check catches state with no change notification of its own (profile
///         switches, edits inside configured filter lists), and only pushes when the set differs.
///     </para>
/// </summary>
public static class PacketWaitRegistry
{
    private const int DEBOUNCE_MS = 20;
    private const int PERIODIC_CHECK_MS = 250;

    // A runtime filter blocks its caller until the plugin confirms; bounded so a stalled plugin
    // cannot hang a macro. Background pushes can afford longer.
    private static readonly TimeSpan _syncPushTimeout = TimeSpan.FromMilliseconds( 500 );
    private static readonly TimeSpan _backgroundPushTimeout = TimeSpan.FromSeconds( 2 );

    // Held across build, send and confirmation: the plugin may apply concurrent pushes in any order,
    // so only one is ever in flight.
    private static readonly Lock _pushLock = new();
    private static readonly Timer _debounce = new( _ => Push( _backgroundPushTimeout ) );
    private static string _lastPushed;
    private static long _nextPeriodicCheck;

    static PacketWaitRegistry()
    {
        foreach ( PacketFilter.PacketFilter filter in Engine.RuntimePacketFilters )
        {
            filter.Changed += Invalidate;
        }
    }

    /// <summary>The rules last confirmed by the plugin, for diagnostics and tests.</summary>
    public static PacketWaitRule[] LastPushedRules { get; private set; } = [];

    /// <summary>Something a rule depends on changed: push a fresh set shortly.</summary>
    public static void Invalidate()
    {
        if ( Engine.Host == null )
        {
            return;
        }

        _debounce.Change( DEBOUNCE_MS, Timeout.Infinite );
    }

    /// <summary>
    ///     Pushes the current set now and waits for the plugin to switch to it. Used when a runtime
    ///     filter is added, so it is live before the caller relies on it.
    /// </summary>
    public static bool PushNow( bool force = false )
    {
        return Push( _syncPushTimeout, force );
    }

    /// <summary>Called from the tick: re-pushes if any untracked state changed the set.</summary>
    public static void CheckPeriodic()
    {
        long now = Environment.TickCount64;

        if ( now < Volatile.Read( ref _nextPeriodicCheck ) )
        {
            return;
        }

        Volatile.Write( ref _nextPeriodicCheck, now + PERIODIC_CHECK_MS );

        Push( _backgroundPushTimeout );
    }

    /// <summary>Every packet the UI might drop or rewrite, in both directions.</summary>
    public static PacketWaitRule[] BuildRules()
    {
        return
        [
            .. Engine.GetRuntimeFilterWaitRules(), .. IncomingPacketFilters.GetWaitRules(), .. OutgoingPacketFilters.GetWaitRules(),
            .. CommandsManager.GetWaitRules()
        ];
    }

    /// <summary>
    ///     A runtime filter's rules. <see cref="PacketFilter.PacketFilter.MatchFilterAll" /> claims a
    ///     packet when any one of its conditions holds, so each condition becomes a rule of its own; a
    ///     filter without conditions claims every packet with its id, and one with an empty set never
    ///     matches anything.
    /// </summary>
    public static IEnumerable<PacketWaitRule> FromFilter( PacketFilterInfo pfi, bool outgoing )
    {
        if ( pfi == null )
        {
            yield break;
        }

        byte packetId = (byte) pfi.PacketID;
        PacketFilterCondition[] conditions = pfi.GetConditions();

        if ( conditions == null )
        {
            yield return new PacketWaitRule( packetId, outgoing );

            yield break;
        }

        foreach ( PacketFilterCondition condition in conditions )
        {
            if ( condition == null )
            {
                continue;
            }

            byte[] bytes = condition.GetBytes() ?? [];

            if ( bytes.Length > condition.Length )
            {
                bytes = [.. bytes.Take( condition.Length )];
            }

            yield return new PacketWaitRule( packetId, outgoing, new PacketWaitCondition( condition.Position, bytes, condition.Negate ) );
        }
    }

    /// <summary>A rule per id, for filters that can act on any packet with that id.</summary>
    public static IEnumerable<PacketWaitRule> AllOf( bool outgoing, params byte[] packetIds )
    {
        return packetIds.Select( id => new PacketWaitRule( id, outgoing ) );
    }

    /// <summary>A 4-byte big-endian value at <paramref name="position" />, as a serial check.</summary>
    public static PacketWaitRule IntAt( byte packetId, int position, int value, bool outgoing = false )
    {
        return new PacketWaitRule( packetId, outgoing,
            new PacketWaitCondition( position, [(byte) ( value >> 24 ), (byte) ( value >> 16 ), (byte) ( value >> 8 ), (byte) value] ) );
    }

    /// <summary>A 2-byte big-endian value at <paramref name="position" />, as an id check.</summary>
    public static PacketWaitRule ShortAt( byte packetId, int position, int value, bool outgoing = false )
    {
        return new PacketWaitRule( packetId, outgoing, new PacketWaitCondition( position, [(byte) ( value >> 8 ), (byte) value] ) );
    }

    /// <summary>Forget what was pushed, so the next push goes out even if the set looks unchanged.</summary>
    internal static void Reset()
    {
        lock ( _pushLock )
        {
            _lastPushed = null;
            LastPushedRules = [];
        }
    }

    private static bool Push( TimeSpan timeout, bool force = false )
    {
        IHostMethods host = Engine.Host;

        if ( host == null || !Engine.Installed )
        {
            return false;
        }

        lock ( _pushLock )
        {
            PacketWaitRule[] rules;

            try
            {
                rules = BuildRules();
            }
            catch ( Exception e )
            {
                // A provider tripping over half-built state must not leave the plugin on stale rules
                // that are too narrow; waiting on everything is always correct.
                Console.Error.WriteLine( $"ClassicAssist: couldn't build packet wait rules, waiting on every packet: {e.Message}" );
                rules = AllPackets();
            }

            string signature = string.Join( "\n", rules.Select( r => r.ToString() ) );

            if ( !force && signature == _lastPushed )
            {
                return true;
            }

            try
            {
                Task<bool> task = host.SetPacketWaitRules( rules );

                if ( !task.Wait( timeout ) || !task.Result )
                {
                    // Unconfirmed: the plugin may still apply it, or an older set, so push again next time
                    _lastPushed = null;
                    Console.Error.WriteLine( $"ClassicAssist: plugin didn't confirm packet wait rules within {timeout.TotalMilliseconds:F0}ms" );

                    return false;
                }
            }
            catch ( Exception e )
            {
                _lastPushed = null;
                Console.Error.WriteLine( $"ClassicAssist: couldn't send packet wait rules: {e.GetBaseException().Message}" );

                return false;
            }

            _lastPushed = signature;
            LastPushedRules = rules;

            Debug.WriteLine( $"Packet wait rules ({rules.Length}):\n{signature}" );

            return true;
        }
    }

    private static PacketWaitRule[] AllPackets()
    {
        return [.. Enumerable.Range( 0, 0x100 ).SelectMany( id => new[] { new PacketWaitRule( (byte) id ), new PacketWaitRule( (byte) id, true ) } )];
    }
}
