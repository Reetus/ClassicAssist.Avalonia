#region License

// Copyright (C) 2026 Reetus
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
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using ClassicAssist.UO.Data;

namespace ClassicAssist.Avalonia.Misc
{
    /// <summary>
    ///     Binds a <see cref="Pixmap" /> straight onto an Image.
    /// </summary>
    public class PixmapValueConverter : IValueConverter
    {
        /// <summary>
        ///     Bitmaps keyed on the pixel buffer they were built from, so that scrolling a virtualised list
        ///     back and forth does not re-upload the same tile over and over. The table holds neither key nor
        ///     value alive, so a bitmap goes away with the pixmap that produced it.
        /// </summary>
        private static readonly ConditionalWeakTable<uint[], Bitmap> _cache =
            new ConditionalWeakTable<uint[], Bitmap>();

        public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
        {
            if ( !( value is Pixmap pixmap ) || pixmap.IsEmpty )
            {
                return null;
            }

            return _cache.GetValue( pixmap.Pixels, _ => pixmap.ToBitmap() );
        }

        public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
        {
            throw new NotImplementedException();
        }
    }
}
