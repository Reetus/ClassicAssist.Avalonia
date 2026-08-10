#region License

// Copyright (C) 2020 Reetus
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System.Collections.Generic;
using ClassicAssist.Shared;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Objects.Gumps;

namespace ClassicAssist.UO.Gumps;

public abstract class RepositionableGump : Gump
{
    private const int REPOSITION_BUTTON_ID = 100;
    private readonly int _height;
    private readonly int _width;

    protected RepositionableGump( int width, int height, int serial, uint gumpID ) : base( 0, 0, serial,
        gumpID )
    {
        _width = width;
        _height = height;
    }

    public int GumpX { get; set; } = 100;
    public int GumpY { get; set; } = 100;

    /// <summary>
    ///     True to fall back to the slider-overlay reposition button/window. False lets the gump be
    ///     dragged in-client instead - see <see cref="ReflectionRepositionableGump" />, which turns
    ///     this off when it can read the gump's position back via reflection.
    /// </summary>
    protected virtual bool UseManualReposition => true;

    public override void SendGump()
    {
        X = GumpX;
        Y = GumpY;

        if ( UseManualReposition )
        {
            AddButton( _width - 15, 5, 0x82C, 0x82C, REPOSITION_BUTTON_ID, GumpButtonType.Reply, 0 );
        }
        else
        {
            Movable = true;
        }

        base.SendGump();
    }

    public override void OnResponse( int buttonID, int[] switches, List<(int Key, string Value)> textEntries = null )
    {
        if ( UseManualReposition && buttonID == REPOSITION_BUTTON_ID )
        {
            SetPosition( GumpX, GumpY );

            RepositionableGumpViewModel vm = new( this, GumpX, GumpY );

            _ = Engine.UIInvoker.InvokeDialog( "RepositionableGumpWindow", dataContext: vm );
        }

        base.OnResponse( buttonID, switches, textEntries );
    }

    public virtual void SetPosition( int x, int y )
    {
        GumpX = x;
        GumpY = y;
    }
}