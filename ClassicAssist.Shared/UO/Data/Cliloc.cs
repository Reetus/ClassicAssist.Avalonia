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
        if ( tokenizedString.IndexOf( "http://", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             tokenizedString.IndexOf( "https://", StringComparison.OrdinalIgnoreCase ) >= 0 )
        {
            return tokenizedString;
        }

        if ( tokenizedString.IndexOf( '#' ) < 0 )
        {
            return tokenizedString;
        }

        // One StringBuilder pass per round instead of a Substring + string.Replace per token, which
        // allocated a fresh copy of the whole string for every replacement. A round can insert a value
        // that itself contains a #token, so repeat while any remain - the original Replace loop did the
        // same. Progress is checked so a value equal to its own token cannot spin.
        string current = tokenizedString;

        while ( true )
        {
            ReadOnlySpan<char> source = current;
            StringBuilder builder = null;
            int position = 0;
            bool replaced = false;

            while ( position < source.Length )
            {
                int index = source[position..].IndexOf( '#' );

                if ( index < 0 )
                {
                    break;
                }

                index += position;

                // A '#' that starts no digit run is literal text. Returning the string as it stands
                // here is what the old pass did - and it is what stops a trailing '#' from hanging.
                if ( index + 1 >= source.Length || !char.IsNumber( source[index + 1] ) )
                {
                    break;
                }

                int end = index + 1;

                while ( end < source.Length && char.IsNumber( source[end] ) )
                {
                    end++;
                }

                if ( !int.TryParse( source.Slice( index + 1, end - index - 1 ), out int propertyNum ) )
                {
                    break;
                }

                builder ??= new StringBuilder( current.Length );
                builder.Append( source[position..index] );
                builder.Append( GetProperty( propertyNum ) );

                position = end;
                replaced = true;
            }

            if ( !replaced )
            {
                return current;
            }

            builder.Append( source[position..] );

            string result = builder.ToString();

            if ( result == current || result.IndexOf( '#' ) < 0 )
            {
                return result;
            }

            current = result;
        }
    }

    public static string GetLocalString( int property, string[] arguments )
    {
        string propertyString = GetProperty( property );

        if ( arguments == null || arguments.Length == 0 )
        {
            return propertyString;
        }

        ReadOnlySpan<char> source = propertyString;

        // Expand the # tokens in the arguments once, writing back as the old path did. This happens
        // even when the property text has no placeholders: callers read the expanded arguments off
        // Property.Arguments (autoloot's skill-bonus match reads Arguments[0]/[1] directly), and the
        // old loop expanded at least the first argument before giving up on the missing placeholder.
        for ( int i = 0; i < arguments.Length; i++ )
        {
            string expanded = GetLocalString( arguments[i] );

            // An argument that itself contains a '~' could be re-scanned as a placeholder by the old
            // Replace loop; leave that (rare) shape to it.
            if ( expanded.IndexOf( '~' ) >= 0 )
            {
                return GetLocalStringReplace( propertyString, arguments );
            }

            arguments[i] = expanded;
        }

        if ( source.IndexOf( '~' ) < 0 )
        {
            return propertyString;
        }

        // Distinct ~...~ tokens, in order of first appearance, each mapped to an argument. The old
        // implementation replaced every occurrence of a token before moving on, so a repeated token
        // shares one argument - reusing the index of a token already seen reproduces that.
        const int MaxTokens = 64;

        Span<int> tokenStarts = stackalloc int[MaxTokens];
        Span<int> tokenLengths = stackalloc int[MaxTokens];
        Span<int> tokenArguments = stackalloc int[MaxTokens];
        int tokenCount = 0;
        int nextArgument = 0;

        StringBuilder builder = null;
        int position = 0;

        while ( position < source.Length )
        {
            int open = source[position..].IndexOf( '~' );

            if ( open < 0 )
            {
                break;
            }

            open += position;

            int close = source[( open + 1 )..].IndexOf( '~' );

            if ( close < 0 )
            {
                break;
            }

            close += open + 1;

            ReadOnlySpan<char> token = source.Slice( open, close - open + 1 );
            int mapped = -1;

            for ( int t = 0; t < tokenCount; t++ )
            {
                if ( source.Slice( tokenStarts[t], tokenLengths[t] ).SequenceEqual( token ) )
                {
                    mapped = tokenArguments[t];
                    break;
                }
            }

            if ( mapped < 0 )
            {
                // Arguments exhausted: the old loop stopped here and left the rest untouched.
                if ( nextArgument >= arguments.Length )
                {
                    break;
                }

                if ( tokenCount >= MaxTokens )
                {
                    return GetLocalStringReplace( propertyString, arguments );
                }

                mapped = nextArgument++;
                tokenStarts[tokenCount] = open;
                tokenLengths[tokenCount] = close - open + 1;
                tokenArguments[tokenCount] = mapped;
                tokenCount++;
            }

            builder ??= new StringBuilder( propertyString.Length + 16 );
            builder.Append( source[position..open] );
            builder.Append( arguments[mapped] );
            position = close + 1;
        }

        if ( builder == null )
        {
            // Nothing was substituted (a lone '~', say); return the property text as-is.
            return propertyString;
        }

        builder.Append( source[position..] );

        return builder.ToString();
    }

    /// <summary>
    ///     The historical <c>Replace</c>-per-placeholder implementation, kept only as a fallback for
    ///     the rare shapes <see cref="GetLocalString( int, string[] )" /> cannot reproduce exactly.
    /// </summary>
    private static string GetLocalStringReplace( string propertyString, string[] arguments )
    {
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

    /// <summary>
    ///     Forces the cliloc list to load now rather than on the first lookup.
    ///     <para>
    ///         <see cref="LoadClilocs" /> reads the whole Cliloc file and, on clients from 7.0.104,
    ///         BWT-decompresses it before parsing every entry into the dictionary. Because the list is
    ///         behind a <see cref="Lazy{T}" />, the first <see cref="GetProperty" /> call pays all of
    ///         that - and the first lookup in a session is a localized message, which runs on the packet
    ///         path and blocks the client's own thread while the UI answers. That was measured at ~1.6s
    ///         on the first 0xC1 after login.
    ///     </para>
    ///     <para>
    ///         <see cref="Engine.InstallRPC" /> calls this before flipping <c>Installed</c>, so the cost
    ///         is paid once during startup (behind the splash) and no packet ever waits on it.
    ///     </para>
    /// </summary>
    public static void Preload()
    {
        try
        {
            _ = _lazyClilocList.Value;
        }
        catch ( Exception )
        {
            // A missing or unreadable Cliloc file already degrades to "Localized string N not found!"
            // per lookup; a startup that dies here would only be worse.
        }
    }

    internal static void Initialize( Func<Dictionary<int, string>> customInitializer )
    {
        // For use in unit tests
        if ( customInitializer != null )
        {
            _lazyClilocList = new Lazy<Dictionary<int, string>>( customInitializer );
        }
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