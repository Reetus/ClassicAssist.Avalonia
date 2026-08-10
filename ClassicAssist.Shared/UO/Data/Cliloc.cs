using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ClassicAssist.Data;
using ClassicAssist.Shared;

namespace ClassicAssist.UO.Data;

public static class Cliloc
{
    private static Lazy<Dictionary<int, string>> _lazyClilocList =
        new( LoadClilocs );

    private static string _dataPath;

    private static readonly Version _bwtClientVersion = new( 7, 0, 104, 0 );

    private static Dictionary<int, string> LoadClilocs()
    {
        string filename = Path.Combine( _dataPath, "Cliloc.enu" );

        if ( !File.Exists( filename ) )
        {
            throw new FileNotFoundException( "File not found.", filename );
        }

        byte[] rawBytes = File.ReadAllBytes( filename );

        // From 7.0.104 the cliloc files are BWT compressed. Read as-is they decode to garbage, which
        // is why every string comes out wrong on a modern client rather than merely missing.
        bool newFormat = Engine.ClientVersion != null && Engine.ClientVersion >= _bwtClientVersion;

        byte[] fileBytes = newFormat ? BwtDecompress.Decompress( rawBytes ) : rawBytes;

        Dictionary<int, string> clilocList = new( 100000 );

        ushort len;

        for ( int x = 6; x < fileBytes.Length; x += 7 + len )
        {
            len = BitConverter.ToUInt16( fileBytes, x + 5 );
            int cliloc = BitConverter.ToInt32( fileBytes, x );

            // A truncated file would otherwise read past the end of the buffer. Zero is a legitimate
            // length - plenty of clilocs are empty strings - so only a negative remainder, meaning
            // the header itself ran off the end, ends the loop.
            int readLen = fileBytes.Length < x + 7 + len ? fileBytes.Length - ( x + 7 ) : len;

            if ( readLen < 0 )
            {
                break;
            }

            string value = Encoding.UTF8.GetString( fileBytes, x + 7, readLen );

            // Duplicates do occur; first definition wins rather than throwing.
            clilocList.TryAdd( cliloc, value );
        }

        return clilocList;
    }

    public static string GetLocalString( string tokenizedString )
    {
        // Ordinal comparison rather than ToLower(), which allocated two lowercased copies of the string
        // on every call for a check that nearly always fails.
        if ( tokenizedString.Contains( "http://", StringComparison.OrdinalIgnoreCase ) ||
             tokenizedString.Contains( "https://", StringComparison.OrdinalIgnoreCase ) )
        {
            return tokenizedString;
        }

        // Tracks whether the pass below actually replaced anything. Without it a '#' that starts no
        // token - most obviously a trailing one, as in "you see: #" - satisfies the Contains check
        // forever while the pass does nothing, and the caller hangs. Journal text and gump text come
        // straight from the server, so that string is reachable from outside.
        bool replaced = true;

        while ( replaced && tokenizedString.Contains( "#" ) )
        {
            replaced = false;

            for ( int x = 0; x < tokenizedString.Length; x++ )
            {
                if ( tokenizedString[x] != '#' || x >= tokenizedString.Length - 1 )
                {
                    continue;
                }

                if ( !char.IsNumber( tokenizedString[x + 1] ) )
                {
                    return tokenizedString;
                }

                int y;

                for ( y = x + 1; y < tokenizedString.Length; y++ )
                {
                    if ( !char.IsNumber( tokenizedString[y] ) )
                    {
                        break;
                    }
                }

                string token = tokenizedString[x..y];
                string tokenNum = tokenizedString.Substring( x + 1, y - x - 1 );

                if ( tokenNum.Length <= 0 )
                {
                    continue;
                }

                if ( !int.TryParse( tokenNum, out int propertyNum ) )
                {
                    return tokenizedString;
                }

                string property = GetProperty( propertyNum );
                tokenizedString = tokenizedString.Replace( token, property );
                replaced = true;
            }
        }

        return tokenizedString;
    }

    public static string GetLocalString( int property, string[] arguments )
    {
        string propertyString = GetProperty( property );

        if ( arguments == null )
        {
            return propertyString;
        }

        //foreach (string s in arguments)
        for ( int x = 0; x < arguments.Length; x++ )
        {
            arguments[x] = GetLocalString( arguments[x] );
            bool found = false;
            int start = 0;
            int index = 0;

            foreach ( char c in propertyString )
            {
                if ( c == '~' )
                {
                    if ( found )
                    {
                        string subString = propertyString.Substring( start, index - start + 1 );
                        propertyString = propertyString.Replace( subString, arguments[x] );

                        break;
                    }

                    start = index;
                    found = true;
                }

                index++;
            }

            if ( !found )
            {
                return propertyString;
            }
        }

        return propertyString;
    }

    public static void Initialize( string dataPath )
    {
        _dataPath = dataPath;

        // Reset the cache: the list is keyed off both the path and the client version, and Initialize
        // runs after the version is known. Leaving a list loaded from an earlier call in place would
        // pin whatever was read first for the lifetime of the process.
        _lazyClilocList = new Lazy<Dictionary<int, string>>( LoadClilocs );
    }

    public static string GetProperty( int property )
    {
        return _lazyClilocList.Value.TryGetValue( property, out string propertyString )
            ? propertyString
            : $"Localized string {property} not found!";
    }

    public static Dictionary<int, string> GetItems()
    {
        return new Dictionary<int, string>( _lazyClilocList.Value );
    }
}