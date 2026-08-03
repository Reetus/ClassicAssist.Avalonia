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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ClassicAssist.Shared;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros;
using ClassicAssist.UO.Objects.Gumps;

namespace ClassicAssist.UO.Gumps
{
    public class MacrosGump : ReflectionRepositionableGump
    {
        private static Timer _timer;
        private static string _lastList;
        private static int _serial = 0x0fe00000;

        public MacrosGump( string html ) : base( Options.CurrentOptions.MacrosGumpWidth,
            Options.CurrentOptions.MacrosGumpHeight, _serial++, (uint) _serial++ )
        {
            int width = Options.CurrentOptions.MacrosGumpWidth;
            int height = Options.CurrentOptions.MacrosGumpHeight;

            GumpX = Options.CurrentOptions.MacrosGumpX;
            GumpY = Options.CurrentOptions.MacrosGumpY;

            Movable = false;
            Closable = false;
            Resizable = false;
            Disposable = false;
            AddPage( 0 );

            if ( Options.CurrentOptions.MacrosGumpTransparent )
            {
                AddHtml( 0, 0, width, height, string.Empty, false, false );
                AddAlphaRegion( 0, 0, width, height );
            }
            else
            {
                AddBackground( 0, 0, width, height, 3500 );
            }

            AddHtml( 20, 20, width - 40, height - 40, html, false, true );
        }

        public static void ResendGump( bool force = false )
        {
            try
            {
                MacroManager _macroManager = MacroManager.GetInstance();

                IEnumerable<MacroEntry> macro = _macroManager.Items.Where( e => e.IsRunning );

                string textColor = Options.CurrentOptions.MacrosGumpTextColor;

                string html = string.Empty;

                foreach ( MacroEntry entry in macro )
                {
                    if ( entry.IsBackground )
                    {
                        html += $"<BASEFONT COLOR={textColor}><I>{entry.Name}</I></BASEFONT>\n";
                    }
                    else
                    {
                        html += $"<BASEFONT COLOR={textColor}>{entry.Name}</BASEFONT>\n";
                    }
                }

                if ( html.Equals( _lastList ) && !force )
                {
                    return;
                }

                if ( Engine.Gumps.GetGumps( out Gump[] gumps ) )
                {
                    foreach ( Gump macrosGump in gumps.Where( g => g is MacrosGump ) )
                    {
                        Shared.UO.Commands.CloseClientGump( macrosGump.ID );
                    }
                }

                MacrosGump gump = new MacrosGump( html );
                gump.SendGump();

                _lastList = html;
            }
            catch ( InvalidOperationException e )
            {
                Console.WriteLine( e.ToString() );
            }
        }

        public static void Initialize()
        {
            _timer = new Timer( o => ResendGump(), null, 1000, 250 );
        }

        public override void SetPosition( int x, int y )
        {
            base.SetPosition( x, y );

            Options.CurrentOptions.MacrosGumpX = x;
            Options.CurrentOptions.MacrosGumpY = y;

            ResendGump( true );
        }

        public override void OnClosing()
        {
            base.OnClosing();

            ( int x, int y ) = GetPosition();

            if ( x == default || y == default )
            {
                return;
            }

            Options.CurrentOptions.MacrosGumpX = x;
            Options.CurrentOptions.MacrosGumpY = y;
        }
    }
}