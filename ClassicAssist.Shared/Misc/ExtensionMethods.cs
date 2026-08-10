using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Misc;

public static class ExtensionMethods
{
    public static string SHA1( this string str )
    {
        byte[] hash = System.Security.Cryptography.SHA1.HashData( Encoding.UTF8.GetBytes( str ) );

        StringBuilder formatted = new( 2 * hash.Length );

        foreach ( byte b in hash )
        {
            formatted.AppendFormat( "{0:X2}", b );
        }

        return formatted.ToString();
    }

    public static T ReadStruct<T>( this Stream stream ) where T : struct
    {
        int size = Marshal.SizeOf( typeof( T ) );

        byte[] buffer = new byte[size];

        stream.ReadExactly( buffer, 0, size );

        GCHandle pinnedBuffer = GCHandle.Alloc( buffer, GCHandleType.Pinned );

        T structure = (T) Marshal.PtrToStructure( pinnedBuffer.AddrOfPinnedObject(), typeof( T ) );

        pinnedBuffer.Free();

        return structure;
    }

    public static T GetPropertyAttribute<T>( this Type type, string propertyName )
    {
        if ( type == null )
        {
            return default;
        }

        T attr = default;

        PropertyInfo pi = type.GetProperty( propertyName );

        if ( pi != null )
        {
            attr = pi.GetCustomAttributes( false ).OfType<T>().SingleOrDefault();
        }

        return attr != null ? attr : default;
    }

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

    public static IEnumerable<IEnumerable<T>> Split<T>( this IEnumerable<T> source, int chunkSize )
    {
        IEnumerable<T> enumerable = source.ToList();

        return enumerable.Where( ( x, i ) => i % chunkSize == 0 ).Select( ( x, i ) => enumerable.Skip( i * chunkSize ).Take( chunkSize ) );
    }

    public static JArray ToJArray( this int[] arr )
    {
        JArray jArray = [.. arr];

        return jArray;
    }

    public static int[] ToIntArray( this JToken jToken )
    {
        return [.. jToken.Select( token => token.ToObject<int>() )];
    }

    // https://docs.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/interop-with-other-asynchronous-patterns-and-types?redirectedfrom=MSDN#WHToTap
    public static Task<bool> ToTask( this EventWaitHandle waitHandle, Func<bool> resultAction = null )
    {
        if ( waitHandle == null )
        {
            throw new ArgumentNullException( nameof( waitHandle ) );
        }

        TaskCompletionSource<bool> tcs = new();

        RegisteredWaitHandle rwh = ThreadPool.RegisterWaitForSingleObject( waitHandle, delegate { tcs.TrySetResult( resultAction?.Invoke() ?? true ); }, null, -1, true );

        Task<bool> t = tcs.Task;

        t.ContinueWith( antecedent => rwh.Unregister( null ) );

        return t;
    }

    public static Task ToTask( this IEnumerable<EventWaitHandle> waitHandles )
    {
        List<Task<bool>> tasks = [.. waitHandles.Select( waitHandle => waitHandle.ToTask() )];

        return Task.WhenAll( tasks );
    }
}