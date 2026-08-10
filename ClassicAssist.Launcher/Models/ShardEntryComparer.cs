using System;
using System.Collections.Generic;

namespace ClassicAssist.Launcher.Models;

public class ShardEntryComparer : IComparer<ShardEntry>
{
    public int Compare( ShardEntry x, ShardEntry y )
    {
        if ( ReferenceEquals( x, y ) )
        {
            return 0;
        }

        if ( y is null )
        {
            return 1;
        }

        if ( x is null )
        {
            return -1;
        }

        int result = y.Encryption.CompareTo( x.Encryption );

        if ( result == 0 )
        {
            result = y.IsPreset.CompareTo( x.IsPreset );
        }

        return result == 0 ? string.Compare( x.Name, y.Name, StringComparison.Ordinal ) : result;
    }
}
