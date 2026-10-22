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
        Message = textEntries?[0].Value ?? string.Empty;
        Result = buttonID == 1;
        AutoResetEvent.Set();
    }

    public static (bool Result, string Message) MessagePrompt( string message, string initialText = "",
        bool closable = false )
    {
        MessagePromptGump gump = new( message, initialText, closable );

        PacketFilterInfo pfi = new( 0xB1,
            [PacketFilterConditions.UIntAtPositionCondition( gump.ID, 7 )] );

        Engine.AddSendPostFilter( pfi );

        gump.SendGump();

        gump.AutoResetEvent.WaitOne();

        Engine.RemoveSendPostFilter( pfi );

        return (gump.Result, gump.Message);
    }
}
