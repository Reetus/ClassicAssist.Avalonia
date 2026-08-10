using System;
using System.Threading;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared;
using ClassicAssist.UO;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;
using UOC = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.Data.Macros.Commands;

public static class MovementCommands
{
    private const int MOVEMENT_TIMEOUT = 500;
    private const int PATHFIND_MAX_DISTANCE = 32;
    private const int PATHFIND_START_TIMEOUT = 1000;
    private const int PATHFIND_START_POLL_INTERVAL = 25;
    private static bool _forceWalk;

    [CommandsDisplay( Category = nameof( Strings.Movement ),
        Parameters = new[] { nameof( ParameterType.Direction ) } )]
    public static bool Walk( string direction )
    {
        return Move( direction, false );
    }

    [CommandsDisplay( Category = nameof( Strings.Movement ) )]
    public static void SetForceWalk( bool force )
    {
        UOC.SetForceWalk( force );
        UOC.SystemMessage( force ? Strings.Force_Walk_On : Strings.Force_Walk_Off );
    }

    [CommandsDisplay( Category = nameof( Strings.Movement ) )]
    public static void ToggleForceWalk()
    {
        _forceWalk = !_forceWalk;

        UOC.SetForceWalk( _forceWalk );
        UOC.SystemMessage( _forceWalk ? Strings.Force_Walk_On : Strings.Force_Walk_Off );
    }

    [CommandsDisplay( Category = nameof( Strings.Movement ),
        Parameters = new[] { nameof( ParameterType.Direction ) } )]
    [CommandsDisplayStringSeeAlso( [nameof( Direction )] )]
    public static void Turn( string direction )
    {
        Direction directionEnum = Utility.GetEnumValueByName<Direction>( direction );

        if ( Engine.Player.Direction == directionEnum )
        {
            return;
        }

        Engine.Move( directionEnum, false );
        UOC.WaitForIncomingPacket( new PacketFilterInfo( 22 ), MOVEMENT_TIMEOUT );
    }

    [CommandsDisplay( Category = nameof( Strings.Movement ),
        Parameters = new[] { nameof( ParameterType.Direction ) } )]
    [CommandsDisplayStringSeeAlso( [nameof( Direction )] )]
    public static bool Run( string direction )
    {
        return Move( direction, true );
    }

    private static bool Move( string direction, bool run )
    {
        Direction directionEnum = Utility.GetEnumValueByName<Direction>( direction );

        if ( directionEnum == Direction.Invalid )
        {
            return false;
        }

        try
        {
            bool result = Engine.Move( directionEnum, run );
            UOC.WaitForIncomingPacket( new PacketFilterInfo( 22 ), MOVEMENT_TIMEOUT );
            return result;
        }
        catch ( IndexOutOfRangeException )
        {
        }

        return false;
    }

    [CommandsDisplay( Category = nameof( Strings.Movement ),
        Parameters = new[]
        {
            nameof( ParameterType.XCoordinate ), nameof( ParameterType.YCoordinate ),
            nameof( ParameterType.ZCoordinate )
        } )]
    public static bool Pathfind( int x, int y, int z, bool checkDistance = true, int desiredDistance = 0 )
    {
        int distance = Math.Max( Math.Abs( x - Engine.Player?.X ?? x ), Math.Abs( y - Engine.Player?.Y ?? y ) );

        if ( checkDistance && distance > PATHFIND_MAX_DISTANCE )
        {
            UOC.SystemMessage( Strings.Maximum_distance_exceeded_ );
            return false;
        }

        if ( Engine.Host != null && Engine.ReflectionAvailable )
        {
            // The client's own WalkTo result, carried back over the bridge - false means it found
            // no path at all, so there is nothing to wait for.
            if ( !ReflectionCommands.WalkTo( x, y, z, desiredDistance ) )
            {
                UOC.SystemMessage( Strings.Pathfind_failed_to_start_ );
                return false;
            }

            WaitForPathfindingToStart();

            return true;
        }

        // The injected packet is fire-and-forget, so the only answer available here is whether
        // the client visibly started walking.
        Engine.SendPacketToClient( new Pathfind( x, y, z ) );

        return WaitForPathfindingToStart();
    }

    /// <summary>
    ///     Blocks until <see cref="Pathfinding" /> reports true, returning whether it ever did.
    ///     <para>
    ///         Upstream ClassicAssist runs in the client's own process and calls
    ///         <c>Pathfinder.WalkTo</c> directly, so <c>AutoWalking</c> is already set by the time
    ///         <see cref="Pathfind(int,int,int,bool,int)" /> returns - which is why the idiomatic
    ///         <c>Pathfind(...)</c> / <c>while Pathfinding():</c> macro works there with no pause.
    ///         Here the call crosses to the plugin process and the client only picks the walk up on
    ///         a later tick, so without this a macro that checks immediately sees false and exits
    ///         straight away.
    ///     </para>
    /// </summary>
    private static bool WaitForPathfindingToStart()
    {
        DateTime timeout = DateTime.Now + TimeSpan.FromMilliseconds( PATHFIND_START_TIMEOUT );

        while ( DateTime.Now < timeout )
        {
            if ( Pathfinding() )
            {
                return true;
            }

            Thread.Sleep( PATHFIND_START_POLL_INTERVAL );
        }

        return false;
    }

    [CommandsDisplay( Category = nameof( Strings.Movement ),
        Parameters = new[]
        {
            nameof( ParameterType.SerialOrAlias )
        } )]
    public static bool Pathfind( object obj, bool checkDistance = true, int desiredDistance = 0 )
    {
        // Pathfind(-1) cancels a walk in progress - already documented in the shipped help.
        if ( obj is int i && i == -1 )
        {
            return ReflectionCommands.CancelPathfinding();
        }

        int serial = AliasCommands.ResolveSerial( obj );

        if ( serial == 0 )
        {
            UOC.SystemMessage( Strings.Entity_not_found___ );
            return false;
        }

        Entity entity = UOMath.IsMobile( serial )
            ? (Entity) Engine.Mobiles.GetMobile( serial )
            : Engine.Items.GetItem( serial );

        if ( entity == null )
        {
            UOC.SystemMessage( Strings.Entity_not_found___ );
            return false;
        }

        return Pathfind( entity.X, entity.Y, entity.Z, checkDistance, desiredDistance );
    }

    [CommandsDisplay( Category = nameof( Strings.Movement ) )]
    public static bool Pathfinding()
    {
        return ReflectionCommands.Pathfinding();
    }

    [CommandsDisplay( Category = nameof( Strings.Movement ) )]
    public static bool Following()
    {
        return ReflectionCommands.Following();
    }

    [CommandsDisplay( Category = nameof( Strings.Movement ),
        Parameters = new[] { nameof( ParameterType.SerialOrAlias ) } )]
    public static void Follow( object obj = null )
    {
        int serial = 0;

        if ( obj != null )
        {
            serial = AliasCommands.ResolveSerial( obj );
        }

        bool result = ReflectionCommands.Follow( serial );

        UOC.SystemMessage( result ? Strings.Activated_following : Strings.Deactivated_following );
    }
}