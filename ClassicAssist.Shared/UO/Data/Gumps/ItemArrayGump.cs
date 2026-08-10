#region License

// Copyright (C) 2022 Reetus
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Objects;
using ClassicAssist.UO.Objects.Gumps;

// ReSharper disable PossibleLossOfFraction

namespace ClassicAssist.UO.Gumps;

public class ItemArrayGump : Gump
{
    private const int _maxRows = 4;
    private const int _perPage = 5 * _maxRows;

    public ItemArrayGump( IReadOnlyCollection<Item> items, uint gumpId, bool multiSelect, int xpos, int ypos,
        bool fixedSize ) : base( xpos, ypos, 0, gumpId )
    {
        Closable = true;
        Resizable = false;
        Disposable = false;
        Movable = true;

        int cellWidth = multiSelect ? 130 : 90;
        const int cellHeight = 70;

        int width = 30 + Math.Min( fixedSize ? int.MaxValue : items.Count, _maxRows ) * cellWidth;
        int height = 60 + Math.Min( fixedSize ? int.MaxValue : items.Count / _maxRows + 1, _perPage / _maxRows ) *
            cellHeight;

        AddPage( 0 );
        AddBackground( 0, 0, width, height, 0x13BE );
        AddAlphaRegion( 10, 10, width - 20, height - 20 );

        int pages = (int) Math.Ceiling( (float) ( items.Count / _perPage ) ) + 1;

        for ( int p = 0; p < pages; p++ )
        {
            List<Item> pageItems = [.. items.Skip( p * _perPage ).Take( _perPage )];
            int rows = (int) Math.Ceiling( (float) ( pageItems.Count / _maxRows ) ) + 1;

            if ( pageItems.Count == 0 )
            {
                break;
            }

            AddPage( p + 1 );

            for ( int r = 0; r < rows; r++ )
            {
                List<Item> rowItems = [.. pageItems.Skip( r * _maxRows ).Take( _maxRows )];

                for ( int i = 0; i < rowItems.Count; i++ )
                {
                    int x = 20 + i * cellWidth;
                    int y = 20 + r * cellHeight;

                    int itemid = rowItems[i].ID;
                    Pixmap b = Art.GetStatic( itemid );
                    AddImageTiledButton( x, y, 0x918, 0x918, rowItems[i].Serial, GumpButtonType.Reply, 0, itemid,
                        rowItems[i].Hue, 40 - b.Width / 2, 30 - b.Height / 2 );

                    if ( Engine.CharacterListFlags.HasFlag( CharacterListFlags.PaladinNecromancerClassTooltips ) )
                    {
                        AddItemProperty( rowItems[i].Serial );
                    }

                    if ( multiSelect )
                    {
                        AddCheck( x + 90, y + 15, 2151, 2153, false, rowItems[i].Serial );
                    }
                }
            }

            if ( p != 0 )
            {
                AddButton( 20, height - 40, 0x26B5, 0x26B6, p + 2, GumpButtonType.Page, p + 1 - 1 );
            }

            if ( pages > p + 1 )
            {
                AddButton( width - 40, height - 40, 0x26AF, 0x26B0, p + 2, GumpButtonType.Page, p + 1 + 1 );
            }

            if ( multiSelect )
            {
                AddButton( width - 120, height - 40, 247, 248, 0, GumpButtonType.Reply, 0 );
            }
        }
    }

    public AutoResetEvent AutoResetEvent { get; set; } = new AutoResetEvent( false );
    public int[] Result { get; set; } = [];

    public override void OnResponse( int buttonID, int[] switches, List<(int Key, string Value)> textEntries = null )
    {
        base.OnResponse( buttonID, switches, textEntries );

        if ( switches != null && switches.Length > 0 )
        {
            Result = switches;
        }
        else if ( buttonID != 0 )
        {
            Result = [buttonID];
        }

        AutoResetEvent.Set();
    }

    public static int[] SendGump( object[] objects, bool multiSelect, int x, int y, bool fixedSize )
    {
        List<Item> items = [];

        foreach ( object item in objects )
        {
            int serial = AliasCommands.ResolveSerial( item );

            if ( serial != 0 && Engine.Items.GetItem( serial, out Item i ) )
            {
                items.Add( i );
            }
        }

        if ( items.Count == 0 )
        {
            Shared.UO.Commands.SystemMessage( Shared.Resources.Strings.No_items_to_display___,
                SystemMessageHues.Yellow, true, true );
            return [];
        }

        uint gumpId = (uint) ( ( 0xfd << 24 ) | ( GetChecksum( items, multiSelect ) >> 8 ) );

        Shared.UO.Commands.CloseClientGump( gumpId );

        ItemArrayGump gump = new( items, gumpId, multiSelect, x, y, fixedSize );

        PacketFilterInfo pfi = new( 0xB1,
            [PacketFilterConditions.UIntAtPositionCondition( gump.ID, 7 )] );

        Engine.AddSendPostFilter( pfi );

        gump.SendGump();

        gump.AutoResetEvent.WaitOne();

        Engine.RemoveSendPostFilter( pfi );

        return gump.Result;
    }

    /// <summary>
    ///     A stable FNV-1a hash of the item set/multiSelect flag, used to derive a gump ID that
    ///     stays consistent for the same set of items - unlike upstream, which pulls in
    ///     Force.Crc32 for the same purpose, this avoids adding a new dependency for a value whose
    ///     only requirement is to be deterministic and vary with its inputs.
    /// </summary>
    private static uint GetChecksum( List<Item> items, bool multiSelect )
    {
        unchecked
        {
            uint hash = 2166136261;

            hash = ( hash ^ (uint) multiSelect.GetHashCode() ) * 16777619;

            foreach ( Item item in items )
            {
                hash = ( hash ^ (uint) item.Serial ) * 16777619;
            }

            return hash;
        }
    }
}
