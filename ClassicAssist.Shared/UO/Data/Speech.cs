using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ClassicAssist.UO.Data;

public static class Speech
{
    private static readonly Lazy<SpeechEntry[]> _entries = new( LoadEntries );

    private static string _dataPath;

    public static int[] GetKeywords( string input )
    {
        SpeechEntry[] entries = _entries.Value;
        List<int> results = null;

        for ( int i = 0; i < entries.Length; i++ )
        {
            if ( !entries[i].Pattern.IsMatch( input ) )
            {
                continue;
            }

            results ??= new List<int>( 4 );

            // Distinct, but over the handful of ids that actually match rather than over every entry.
            if ( !results.Contains( entries[i].Id ) )
            {
                results.Add( entries[i].Id );
            }
        }

        return results == null ? [] : [.. results];
    }

    public static void Initialize( string dataPath )
    {
        _dataPath = dataPath;
    }

    private static SpeechEntry[] LoadEntries()
    {
        string fullPath = Path.Combine( _dataPath, "speech.mul" );

        using FileStream reader =
            new( fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite );
        using BinaryReader binaryReader = new( reader );
        List<SpeechEntry> entries = [];

        while ( reader.Position < reader.Length )
        {
            int id = ( binaryReader.ReadByte() << 8 ) | binaryReader.ReadByte();
            int length = ( binaryReader.ReadByte() << 8 ) | binaryReader.ReadByte();

            byte[] buffer = new byte[length];

            reader.ReadExactly( buffer, 0, length );

            string text = Encoding.UTF8.GetString( buffer );

            // The pattern is built once here rather than per call in GetKeywords. speech.mul holds
            // thousands of entries and Regex's static cache only holds 15, so building them on the fly
            // meant re-parsing nearly every pattern on every line of speech. Left interpreted:
            // RegexOptions.Compiled would emit IL for each of the thousands of patterns, and four
            // fifths of them contain a wildcard so there is no cheap literal shortcut to take instead.
            entries.Add( new SpeechEntry
            {
                Id = id,
                Keywords = text,
                Pattern = new Regex( WildCardToRegular( text ), RegexOptions.CultureInvariant )
            } );
        }

        return [.. entries];
    }

    private static string WildCardToRegular( string value )
    {
        return "^" + Regex.Escape( value ).Replace( "\\*", ".*" ) + "$";
    }

    internal struct SpeechEntry
    {
        public int Id { get; set; }
        public string Keywords { get; set; }
        public Regex Pattern { get; set; }
    }
}