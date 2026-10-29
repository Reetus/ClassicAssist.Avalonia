#region License

// Copyright (C) 2021 Reetus
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
using System.Linq;
using System.Threading;
using ClassicAssist.Shared;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Objects.Gumps;

namespace ClassicAssist.UO.Gumps;

public class SelectionPromptGump : Gump
{
    // Generous because this waits on a human noticing and clicking a dialog - the limit exists only
    // so a lost or unanswered gump cannot park the calling macro thread forever.
    private const int _defaultResponseTimeoutMS = 5 * 60 * 1000;

    public SelectionPromptGump( IEnumerable<string> options, string message, bool closable = false ) : base( 500,
        500 )
    {
        string[] optionsArray = [.. options];

        const int width = 400;
        int height = 150 + optionsArray.Length * 20;

        SetCenterPosition( width, height );
        Closable = closable;
        Resizable = false;
        Disposable = false;
        AddPage( 0 );
        AddBackground( 0, 0, width, height, 9200 );
        AddHtml( 10, 10, width - 20, 70, message, true, true );

        int y = 100;

        for ( int index = 0; index < optionsArray.Length; index++ )
        {
            AddRadio( 10, y, 208, 209, index == 0, index );
            AddLabel( 40, y, 0, optionsArray[index] );
            y += 20;
        }

        AddButton( width - 140, height - 30, 247, 248, 1, GumpButtonType.Reply, 0 );
        AddButton( width - 70, height - 30, 241, 242, 0, GumpButtonType.Reply, 0 );
    }

    public AutoResetEvent AutoResetEvent { get; set; } = new( false );
    public int Index { get; set; }
    public bool Result { get; set; }

    public override void OnResponse( int buttonID, int[] switches, List<(int Key, string Value)> textEntries = null )
    {
        Result = buttonID != 0;

        if ( switches != null && switches.Length > 0 )
        {
            Index = switches[0];
        }

        AutoResetEvent.Set();
    }

    /// <summary>
    ///     Shows the prompt and blocks until the user responds or <paramref name="timeoutMS" /> elapses.
    ///     A timeout is reported exactly like the user declining - <c>(false, 0)</c>, the same values
    ///     callers already treat as "nothing chosen" - so callers cannot tell the difference and a lost
    ///     or unanswered gump cannot block the calling thread forever.
    /// </summary>
    public static (bool Result, int Index) SelectionPrompt( IEnumerable<string> options, string message,
        bool closable = false, int timeoutMS = _defaultResponseTimeoutMS )
    {
        SelectionPromptGump gump = new( options, message, closable );

        PacketFilterInfo pfi = new( 0xB1,
            [PacketFilterConditions.UIntAtPositionCondition( gump.ID, 7 )] );

        Engine.AddSendPostFilter( pfi );

        gump.SendGump();

        bool answered = gump.AutoResetEvent.WaitOne( timeoutMS );

        Engine.RemoveSendPostFilter( pfi );

        if ( !answered )
        {
            // Nobody answered in time: close the abandoned prompt so a late click cannot send a
            // response nobody is waiting on any more, and report a decline.
            gump.CloseGump();

            return (false, 0);
        }

        return (gump.Result, gump.Index);
    }
}
