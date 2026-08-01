#region License

// Copyright (C) 2023 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY

#endregion

using System.Linq;
using ClassicAssist.Shared;
using ClassicAssist.UO.Objects.Gumps;

namespace ClassicAssist.UO.Gumps
{
    /// <summary>
    ///     A <see cref="RepositionableGump" /> that skips the manual slider-overlay window when the
    ///     current client's position can be read back via reflection - the gump is sent movable and the
    ///     user drags it in-client as normal, with the real position picked up in
    ///     <see cref="OnClosing" /> via <see cref="ReflectionCommands.GetGumpPosition" />.
    ///     <para>
    ///         The WPF original reflected directly into the (in-process) client. This fork's UI runs out
    ///         of process from the client - see <see cref="ClassicAssist.Shared.ReflectionCommands" /> -
    ///         so <see cref="Engine.ReflectionAvailable" /> stands in for the old direct type-lookup
    ///         check, and the actual reflection call is round-tripped through <see cref="Engine.Host" />.
    ///     </para>
    /// </summary>
    public abstract class ReflectionRepositionableGump : RepositionableGump
    {
        private readonly bool _canReflection;

        static ReflectionRepositionableGump()
        {
            Engine.DisconnectedEvent += CloseGumps;
            Engine.Shutdown += CloseGumps;
        }

        protected ReflectionRepositionableGump( int width, int height, int serial, uint gumpID ) : base( width,
            height, serial, gumpID )
        {
            _canReflection = Engine.ReflectionAvailable;
        }

        protected override bool UseManualReposition => !_canReflection;

        private static void CloseGumps()
        {
            if ( !Engine.Gumps.GetGumps( out Gump[] gumps ) )
            {
                return;
            }

            foreach ( ReflectionRepositionableGump gump in gumps.OfType<ReflectionRepositionableGump>() )
            {
                gump.OnClosing();
            }
        }

        /// <summary>
        ///     The gump's live position in the client, or (0, 0) when reflection isn't available or the
        ///     gump can't be found (already closed, or the client's internals didn't match the shape this
        ///     reflects against - see <see cref="ReflectionCommands.GetGumpPosition" />).
        /// </summary>
        protected (int, int) GetPosition()
        {
            if ( !_canReflection )
            {
                return ( 0, 0 );
            }

            (int x, int y) = ReflectionCommands.GetGumpPosition( ID );

            return x < 0 || y < 0 ? ( 0, 0 ) : ( x, y );
        }
    }
}
