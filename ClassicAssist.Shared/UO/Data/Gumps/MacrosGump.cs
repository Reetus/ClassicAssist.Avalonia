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
        private static int _serial = 0x0fe00000;
        private static IEnumerable<MacroEntry> _lastMacros;
        private readonly MacroEntry[] _macros;

        public MacrosGump( IEnumerable<MacroEntry> macros ) : base( Options.CurrentOptions.MacrosGumpWidth,
            Options.CurrentOptions.MacrosGumpHeight, _serial++, (uint) _serial++ )
        {
            _macros = macros.ToArray();

            int width = Options.CurrentOptions.MacrosGumpWidth;
            int height = Options.CurrentOptions.MacrosGumpHeight;

            GumpX = Options.CurrentOptions.MacrosGumpX;
            GumpY = Options.CurrentOptions.MacrosGumpY;

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

            int y = 20;
            int i = 10;

            string textColor = Options.CurrentOptions.MacrosGumpTextColor;

            foreach ( MacroEntry macro in _macros )
            {
                if ( i > 17 )
                {
                    return;
                }

                string html = $"<BASEFONT face=Arial color={textColor}>{macro.Name}</BASEFONT>\n";

                if ( macro.IsBackground )
                {
                    html = $"<BASEFONT face=Arial color={textColor}><I>{macro.Name}</I></BASEFONT>\n";
                }

                AddHtml( 20, y, width - 40, height - 40, html, false, false );
                AddButton( width - 30, y + 3, 2104, 2103, i++, GumpButtonType.Reply, 0 );

                y += 20;
            }
        }

        public static void ResendGump( bool force = false )
        {
            if ( !Engine.Connected || Engine.Player == null )
            {
                return;
            }

            try
            {
                if ( !Options.CurrentOptions.MacrosGump )
                {
                    Shared.UO.Commands.CloseClientGump( typeof( MacrosGump ) );

                    return;
                }

                MacroManager _macroManager = MacroManager.GetInstance();

                IEnumerable<MacroEntry> macros = _macroManager?.Items.Where( e => e.IsRunning )
                    .OrderByDescending( e => e.StartedOn ).ToArray();

                if ( _lastMacros != null && macros != null && macros.SequenceEqual( _lastMacros ) && !force )
                {
                    return;
                }

                Shared.UO.Commands.CloseClientGump( typeof( MacrosGump ) );

                MacrosGump gump = new MacrosGump( macros );
                gump.SendGump();

                if ( macros != null )
                {
                    _lastMacros = macros.ToArray();
                }
            }
            catch ( InvalidOperationException e )
            {
                Console.WriteLine( e.ToString() );
            }
        }

        public static void Initialize()
        {
            Shared.UO.Commands.CloseClientGump( typeof( MacrosGump ) );

            _timer?.Dispose();
            _timer = new Timer( o => ResendGump(), null, 1000, 250 );
        }

        public override void SetPosition( int x, int y )
        {
            base.SetPosition( x, y );

            Options.CurrentOptions.MacrosGumpX = x;
            Options.CurrentOptions.MacrosGumpY = y;

            ResendGump( true );
        }

        public override void OnResponse( int buttonID, int[] switches,
            List<(int Key, string Value)> textEntries = null )
        {
            if ( buttonID >= 10 && buttonID < 10 + _macros?.Length )
            {
                MacroEntry macro = _macros[buttonID - 10];
                macro?.Stop();
            }
            else
            {
                base.OnResponse( buttonID, switches, textEntries );
            }
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
