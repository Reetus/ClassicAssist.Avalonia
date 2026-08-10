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

using System.Drawing;
using ClassicAssist.Shared;
using ClassicAssist.UO.Gumps;

namespace ClassicAssist.UI.ViewModels;

public class RepositionableGumpViewModel : BaseViewModel
{
    private readonly RepositionableGump _gump;

    public RepositionableGumpViewModel()
    {
    }

    public RepositionableGumpViewModel( RepositionableGump gump, int initialX, int initialY )
    {
        _gump = gump;
        X = initialX;
        Y = initialY;
        HorizontalMax = 3840;
        VerticalMax = 2160;

        Size gameWindowSize = ReflectionCommands.GetGameWindowSize();

        if ( gameWindowSize == Size.Empty )
        {
            return;
        }

        HorizontalMax = gameWindowSize.Width;
        VerticalMax = gameWindowSize.Height;
    }

    public int HorizontalMax
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int VerticalMax
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int X
    {
        get;
        set
        {
            SetProperty( ref field, value );
            _gump?.SetPosition( field, Y );
        }
    }

    public int Y
    {
        get;
        set
        {
            SetProperty( ref field, value );
            _gump?.SetPosition( X, field );
        }
    }
}