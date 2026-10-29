using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ClassicAssist.UO.Data;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     Binds a <see cref="LockStatus" /> onto its arrow/lock icon. The skills grid re-runs Convert on
///     every packet-driven skill update, so the three bitmaps are decoded once and shared - a per-call
///     Bitmap would leak native bitmaps (and the asset stream they were decoded from) as fast as
///     packets arrive.
/// </summary>
public class LockStatusValueConverter : IValueConverter
{
    private static readonly Bitmap _up = LoadBitmap( "arrow_up.png" );
    private static readonly Bitmap _down = LoadBitmap( "arrow_down.png" );
    private static readonly Bitmap _locked = LoadBitmap( "lock.png" );

    public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
    {
        if ( value is not LockStatus )
        {
            return value;
        }

        LockStatus lockStatus = (LockStatus) value;

        switch ( lockStatus )
        {
            case LockStatus.Up:

                return _up;
            case LockStatus.Down:

                return _down;

            case LockStatus.Locked:

                return _locked;

            default:

                throw new ArgumentOutOfRangeException();
        }
    }

    public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
    {
        throw new NotImplementedException();
    }

    private static Bitmap LoadBitmap( string fileName )
    {
        using Stream stream =
            AssetLoader.Open( new Uri( $"avares://ClassicAssist.Avalonia/Assets/{fileName}" ) );

        return new Bitmap( stream );
    }
}
