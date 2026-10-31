using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClassicAssist.Shared;

namespace ClassicAssist.Avalonia.Misc;

public class StringToResourceConverter : IValueConverter
{
    private static readonly Dictionary<string, Bitmap> _bitmaps = [];

    public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
    {
        if ( value is string key )
        {
            if ( targetType.Equals( typeof( IImage ) ) )
            {
                if ( _bitmaps.TryGetValue( key, out Bitmap bitmap ) )
                {
                    return bitmap;
                }

                // The bitmap keeps its decoded pixels; the resource stream is only the source.
                using ( Stream stream = Engine.GetResourceStream( key ) )
                {
                    bitmap = new Bitmap( stream );
                }

                _bitmaps.Add( key, bitmap );
                return bitmap;
            }
        }

        return value;
    }

    public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
    {
        throw new NotImplementedException();
    }
}