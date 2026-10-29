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
using System.Threading;
using ClassicAssist.Shared;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Objects.Gumps;

namespace ClassicAssist.UO.Gumps;

public class MessagePromptGump : Gump
{
    // Generous because this waits on a human noticing and clicking a dialog - the limit exists only
    // so a lost or unanswered gump cannot park the calling macro thread forever.
    private const int _defaultResponseTimeoutMS = 5 * 60 * 1000;

    public MessagePromptGump( string message, string initialText = "", bool closable = false ) : base( 500, 500 )
    {
        const int width = 300;
        const int height = 200;

        SetCenterPosition( width, height );
        Closable = closable;
        Resizable = false;
        Disposable = false;
        Movable = true;
        AddPage( 0 );
        AddBackground( 0, 0, width, height, 9200 );
        AddHtml( 10, 10, width - 20, height - 90, message, true, true );
        AddBackground( 10, height - 70, width - 20, 29, 9350 );
        AddTextEntry( 13, height - 65, width - 20, 29, 0, 1, initialText );
        AddButton( width - 140, height - 30, 247, 248, 1, GumpButtonType.Reply, 0 );
        AddButton( width - 70, height - 30, 241, 242, 2, GumpButtonType.Reply, 0 );
    }

    public AutoResetEvent AutoResetEvent { get; set; } = new( false );

    public string Message { get; set; }

    public bool Result { get; set; }

    public override void OnResponse( int buttonID, int[] switches, List<(int Key, string Value)> textEntries = null )
    {
        // Same guard as SelectionPromptGump: a null or empty list (malformed/cancelled response)
        // must not index [0].
        if ( textEntries != null && textEntries.Count > 0 )
        {
            Message = textEntries[0].Value ?? string.Empty;
        }
        else
        {
            Message = string.Empty;
        }

        Result = buttonID == 1;
        AutoResetEvent.Set();
    }

    /// <summary>
    ///     Shows the prompt and blocks until the user responds or <paramref name="timeoutMS" /> elapses.
    ///     A timeout is reported exactly like declining an empty prompt - <c>(false, string.Empty)</c>,
    ///     the same values the cancel button produces when nothing was typed - so callers cannot tell
    ///     the difference and a lost or unanswered gump cannot block the calling thread forever.
    /// </summary>
    public static (bool Result, string Message) MessagePrompt( string message, string initialText = "",
        bool closable = false, int timeoutMS = _defaultResponseTimeoutMS )
    {
        MessagePromptGump gump = new( message, initialText, closable );

        PacketFilterInfo pfi = new( 0xB1,
            [PacketFilterConditions.UIntAtPositionCondition( gump.ID, 7 )] );

        Engine.AddSendPostFilter( pfi );

        gump.SendGump();

        bool answered = gump.AutoResetEvent.WaitOne( timeoutMS );

        Engine.RemoveSendPostFilter( pfi );

        if ( !answered )
        {
            // Nobody answered in time: close the abandoned prompt so a late click cannot send a
            // response nobody is waiting on any more, and report a decline of an empty message.
            gump.CloseGump();

            return (false, string.Empty);
        }

        return (gump.Result, gump.Message);
    }
}
