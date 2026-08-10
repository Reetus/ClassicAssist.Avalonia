using System.Collections.Generic;

namespace ClassicAssist.Launcher.Models;

public static class ExtensionMethods
{
    public static void AddSorted<T>( this IList<T> list, T item, IComparer<T> comparer = null )
    {
        comparer ??= Comparer<T>.Default;

        int i = 0;

        while ( i < list.Count && comparer.Compare( list[i], item ) < 0 )
        {
            i++;
        }

        list.Insert( i, item );
    }
}
