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
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using ClassicAssist.Shared.Resources;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     One macro command in the editor's completion popup. Ported from the WPF tree's
///     <c>ClassicAssist.Data.Macros.PythonCompletionData</c>; lives in the Avalonia assembly rather
///     than Shared because ICompletionData comes from AvaloniaEdit, which Shared doesn't reference.
/// </summary>
public class PythonCompletionData : ICompletionData
{
    public PythonCompletionData( string name, string fullName, string description, string insertText )
    {
        MethodName = name;
        Name = fullName;
        Description = description;
        Text = insertText;

        Example = MacroCommandHelp.ResourceManager.GetString( $"{name.ToUpper()}_COMMAND_EXAMPLE" );
    }

    public string Example { get; set; }
    public string MethodName { get; set; }
    public string Name { get; set; }

    /// <summary>
    ///     Replaces the whole line from its first non-whitespace character, so completing over a
    ///     partially typed command doesn't leave the prefix behind.
    /// </summary>
    public void Complete( TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs )
    {
        DocumentLine line = textArea.Document.Lines[textArea.Caret.Line - 1];

        string text = textArea.Document.GetText( line );

        int offset = completionSegment.Offset;

        // Walk backwards decrementing the offset while the character isn't ' ' or '\t'.
        for ( int i = completionSegment.Offset; i > line.Offset; i-- )
        {
            int stringOffset = i - line.Offset - 1;

            if ( stringOffset >= 0 && stringOffset < text.Length && text[stringOffset] != ' ' &&
                 text[stringOffset] != '\t' )
            {
                offset--;
                continue;
            }

            break;
        }

        textArea.Document.Replace( new AnchorSegment( textArea.Document, offset, line.EndOffset - offset ), Text );
    }

    public IImage Image => null;

    public string Text { get; }

    public object Content { get; set; }
    public object Description { get; }
    public double Priority => 0;
}
