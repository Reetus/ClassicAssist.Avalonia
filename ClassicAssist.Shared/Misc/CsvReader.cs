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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ClassicAssist.Misc
{
    /// <summary>
    ///     Minimal RFC-4180-ish CSV reader covering the shape the Autoloot CSV import files use: a header
    ///     row followed by data rows, with optional double-quoted fields (including embedded commas and
    ///     doubled quotes). Replaces CsvHelper, which the WPF tree uses but this port does not depend on.
    /// </summary>
    public class CsvReader
    {
        private readonly List<List<string>> _records;
        private int _position;

        public CsvReader( TextReader reader )
        {
            _records = Parse( reader.ReadToEnd() );
        }

        /// <summary>Names from the first row, if any.</summary>
        public string[] HeaderRecord { get; private set; } = Array.Empty<string>();

        public static List<List<string>> Parse( string text )
        {
            List<List<string>> records = new List<List<string>>();
            List<string> row = new List<string>();
            StringBuilder field = new StringBuilder();
            bool inQuotes = false;

            for ( int i = 0; i < text.Length; i++ )
            {
                char c = text[i];

                if ( inQuotes )
                {
                    if ( c == '"' )
                    {
                        if ( i + 1 < text.Length && text[i + 1] == '"' )
                        {
                            field.Append( '"' );
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append( c );
                    }

                    continue;
                }

                switch ( c )
                {
                    case '"':
                        inQuotes = true;

                        break;
                    case ',':
                        row.Add( field.ToString() );
                        field.Clear();

                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add( field.ToString() );
                        field.Clear();
                        records.Add( row );
                        row = new List<string>();

                        break;
                    default:
                        field.Append( c );

                        break;
                }
            }

            if ( field.Length > 0 || row.Count > 0 )
            {
                row.Add( field.ToString() );
                records.Add( row );
            }

            return records;
        }

        /// <summary>Advances to the first data row, using row zero as the header.</summary>
        public void ReadHeader()
        {
            if ( _records.Count > 0 )
            {
                HeaderRecord = _records[0].ToArray();
            }

            _position = 0;
        }

        /// <summary>Advances to the next data row, skipping the header row already consumed by
        /// <see cref="ReadHeader" />.</summary>
        public bool Read()
        {
            _position++;

            return _position < _records.Count;
        }

        public bool TryGetField( string name, out string value )
        {
            value = null;

            int index = Array.IndexOf( HeaderRecord, name );

            if ( index < 0 || _position >= _records.Count || index >= _records[_position].Count )
            {
                return false;
            }

            value = _records[_position][index];

            return true;
        }

        public bool TryGetField( int index, out string value )
        {
            value = null;

            if ( _position >= _records.Count || index >= _records[_position].Count )
            {
                return false;
            }

            value = _records[_position][index];

            return true;
        }

        private static int ConvertToInt( string value )
        {
            return value.StartsWith( "0x", StringComparison.CurrentCultureIgnoreCase )
                ? Convert.ToInt32( value.Substring( 2 ), 16 )
                : Convert.ToInt32( value, CultureInfo.InvariantCulture );
        }
    }
}
