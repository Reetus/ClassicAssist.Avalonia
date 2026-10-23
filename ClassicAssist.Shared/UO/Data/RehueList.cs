using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ClassicAssist.Shared;
using ClassicAssist.Data;
using ClassicAssist.Shared.UO;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.UO.Data;

public enum RehueType
{
    Custom,
    Friends,
    Enemies
}

public class RehueEntry
{
    public int Hue { get; set; }
    public int Serial { get; set; }
    public RehueType Type { get; set; }
}

public class RehueList
{
    private readonly ConcurrentDictionary<int, RehueEntry> _rehueList = new();

    public void Add( int serial, int hue, RehueType type = RehueType.Custom )
    {
        RehueEntry entry = new() { Serial = serial, Hue = hue, Type = type };

        _rehueList.AddOrUpdate( serial, i => entry, ( i, rehueEntry ) => entry );

        // The plugin must wait on this serial's packets before the caller resyncs to see the new hue
        PacketWaitRegistry.PushNow();
    }

    public bool Remove( int serial )
    {
        bool removed = _rehueList.TryRemove( serial, out _ );

        if ( removed )
        {
            PacketWaitRegistry.Invalidate();
        }

        return removed;
    }

    /// <summary>Serials currently rehued, for <see cref="PacketWaitRegistry" />.</summary>
    public int[] GetSerials()
    {
        return [.. _rehueList.Keys];
    }

    public bool Contains( int serial )
    {
        return _rehueList.ContainsKey( serial );
    }

    public void RemoveByType( RehueType type )
    {
        IEnumerable<int> keys = _rehueList.Where( kvp => kvp.Value.Type == type ).Select( kvp => kvp.Key );

        foreach ( int key in keys )
        {
            Remove( key );
        }
    }

    public void CheckItem( Item item )
    {
        if ( !_rehueList.TryGetValue( item.Serial, out RehueEntry entry ) )
        {
            return;
        }

        if ( item.Owner != 0 && !UOMath.IsMobile( item.Serial ) )
        {
            Engine.SendPacketToClient( new ContainerContentUpdate( item.Serial, item.ID, item.Direction, item.Count,
                item.X, item.Y, item.Grid, item.Owner, entry.Hue ) );
        }
        else
        {
            // TODO
            Commands.Resync();
        }
    }

    public bool CheckSAWorldItem( ref byte[] packet, ref int length )
    {
        int serial = ( packet[4] << 24 ) | ( packet[5] << 16 ) | ( packet[6] << 8 ) | packet[7];

        if ( !_rehueList.TryGetValue( serial, out RehueEntry entry ) )
        {
            return false;
        }

        Engine.SendPacketToClient( new SAWorldItem( packet, length, entry.Hue ) );
        return true;
    }

    public bool CheckMobileIncoming( Mobile mobile, ItemCollection equipment )
    {
        bool result = _rehueList.TryGetValue( mobile.Serial, out RehueEntry entry );

        if ( result )
        {
            Engine.SendPacketToClient( new MobileIncoming( mobile, equipment, entry.Hue ) );
            return true;
        }

        if ( IsRehuedFriend( mobile.Serial ) )
        {
            Engine.SendPacketToClient( new MobileIncoming( mobile, equipment, Options.CurrentOptions.RehueFriendsHue,
                Options.CurrentOptions.RehueFriendsExcludedLayers, Options.CurrentOptions.RehueFriendsBody ) );
            return true;
        }

        return false;
    }

    public bool CheckMobileUpdate( Mobile mobile )
    {
        bool result = _rehueList.TryGetValue( mobile.Serial, out RehueEntry entry );

        if ( result )
        {
            Engine.SendPacketToClient( new MobileUpdate( mobile.Serial, mobile.ID,
                entry.Hue > 0 ? entry.Hue : mobile.Hue, mobile.Status, mobile.X, mobile.Y, mobile.Z,
                mobile.Direction ) );
            return true;
        }

        if ( !Options.CurrentOptions.RehueFriendsBody || !IsRehuedFriend( mobile.Serial ) )
        {
            return false;
        }

        Engine.SendPacketToClient( new MobileUpdate( mobile.Serial, mobile.ID,
            Options.CurrentOptions.RehueFriendsHue, mobile.Status, mobile.X, mobile.Y, mobile.Z,
            mobile.Direction ) );
        return true;
    }

    /// <summary>
    ///     0x2E: an item equipped on a friend after they came on screen gets the friend hue too, unless its
    ///     layer is excluded. The client gets a rehued copy; the original carries on to the handlers so the
    ///     item keeps its real hue, as equipment from 0x78 does.
    /// </summary>
    public bool CheckWornItem( byte[] packet, int length )
    {
        if ( !Options.CurrentOptions.RehueFriends || length < 15 ||
             Options.CurrentOptions.RehueFriendsExcludedLayers.Contains( (Layer) packet[8] ) )
        {
            return false;
        }

        int mobileSerial = ( packet[9] << 24 ) | ( packet[10] << 16 ) | ( packet[11] << 8 ) | packet[12];

        if ( !IsRehuedFriend( mobileSerial ) )
        {
            return false;
        }

        byte[] rehued = new byte[length];
        Array.Copy( packet, rehued, length );

        rehued[13] = (byte) ( Options.CurrentOptions.RehueFriendsHue >> 8 );
        rehued[14] = (byte) Options.CurrentOptions.RehueFriendsHue;

        Engine.SendPacketToClient( rehued, length );
        return true;
    }

    private static bool IsRehuedFriend( int serial )
    {
        return Options.CurrentOptions.RehueFriends &&
               Options.CurrentOptions.Friends.Any( e => e.Serial == serial && e.Serial != Engine.Player?.Serial );
    }

    public void CheckContainer( ItemCollection collection )
    {
        int backpack = Engine.Player?.Backpack?.Serial ?? 0;

        foreach ( Item item in collection.GetItems() )
        {
            if ( item.IsDescendantOf( backpack ) )
            {
                CheckItem( item );
            }
        }
    }

    public bool CheckMobileMoving( Mobile mobile )
    {
        bool result = _rehueList.TryGetValue( mobile.Serial, out RehueEntry entry );

        if ( result )
        {
            Engine.SendPacketToClient( new MobileMoving( mobile, entry.Hue ) );
            return true;
        }

        if ( !Options.CurrentOptions.RehueFriendsBody || !IsRehuedFriend( mobile.Serial ) )
        {
            return false;
        }

        Engine.SendPacketToClient( new MobileMoving( mobile, Options.CurrentOptions.RehueFriendsHue ) );
        return true;
    }
}