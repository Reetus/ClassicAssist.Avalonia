#region License

// Copyright (C) 2020 Reetus
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
using System.ComponentModel;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using ClassicAssist.Data.Macros;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     Keeps the editor's document and <see cref="MacroEntry.Macro" /> in sync.
///     <para>
///         Binds to the entry itself rather than to its text, so a switch to a different macro (load
///         its text, drop the undo history) can be told apart from the same macro's text changing
///         outside the editor - recording, MCP, a file reload - which is applied as an undoable edit of
///         just the changed region. Two separate bindings off <c>SelectedItem</c> would update in no
///         guaranteed order, which can't make that distinction reliably.
///     </para>
/// </summary>
public class AvalonEditBehaviour : Behavior<TextEditor>
{
    public static readonly DirectProperty<AvalonEditBehaviour, MacroEntry> MacroEntryProperty =
        AvaloniaProperty.RegisterDirect<AvalonEditBehaviour, MacroEntry>( nameof( MacroEntry ),
            o => o.MacroEntry, ( o, v ) => { o.MacroEntry = v; } );

    public MacroEntry MacroEntry
    {
        get;
        set
        {
            MacroEntry previous = field;

            if ( !SetAndRaise( MacroEntryProperty, ref field, value ) )
            {
                return;
            }

            previous?.PropertyChanged -= OnMacroEntryPropertyChanged;
            value?.PropertyChanged += OnMacroEntryPropertyChanged;

            LoadMacroEntry();
        }
    }

    /// <summary>
    ///     True for the duration of a <see cref="LoadMacroEntry" /> full-document replace (i.e. a macro
    ///     switch pushing new content in) so other behaviours - notably the breakpoint margin, which
    ///     shifts breakpoint line numbers off <see cref="TextDocument.Changed" /> - can tell that apart
    ///     from an incremental edit of the same macro.
    /// </summary>
    public static bool IsProgrammaticTextChange { get; private set; }

    private void LoadMacroEntry()
    {
        if ( AssociatedObject?.Document == null )
        {
            return;
        }

        TextDocument document = AssociatedObject.Document;
        string text = MacroEntry?.Macro ?? string.Empty;

        if ( !document.Text.Equals( text ) )
        {
            IsProgrammaticTextChange = true;

            try
            {
                document.Text = text;
            }
            finally
            {
                IsProgrammaticTextChange = false;
            }
        }

        // A different macro is being edited, don't allow undo back into the previous macro's text
        document.UndoStack.ClearAll();
        AssociatedObject.CaretOffset = 0;
    }

    private void OnMacroEntryPropertyChanged( object sender, PropertyChangedEventArgs e )
    {
        if ( e.PropertyName != nameof( MacroEntry.Macro ) || AssociatedObject?.Document == null )
        {
            return;
        }

        string text = MacroEntry?.Macro ?? string.Empty;

        // AssociatedObjectOnTextChanged below pushes typed text into the entry, which raises this
        // straight back; replacing the document with content it already holds would add an undo
        // step for every keystroke and clear the redo stack.
        if ( AssociatedObject.Document.Text.Equals( text ) )
        {
            return;
        }

        ReplaceChangedRegion( AssociatedObject.Document, text );
    }

    /// <summary>
    ///     Applies only the region that differs between the document and <paramref name="text" />, as
    ///     a single edit, so it can be undone, keeps the caret where it was, and shifts breakpoints by
    ///     the lines it inserts or removes.
    /// </summary>
    private static void ReplaceChangedRegion( TextDocument document, string text )
    {
        string current = document.Text;
        int maxLength = Math.Min( current.Length, text.Length );

        int prefix = 0;

        while ( prefix < maxLength && current[prefix] == text[prefix] )
        {
            prefix++;
        }

        int suffix = 0;

        while ( suffix < maxLength - prefix &&
                current[current.Length - 1 - suffix] == text[text.Length - 1 - suffix] )
        {
            suffix++;
        }

        document.Replace( prefix, current.Length - prefix - suffix,
            text.Substring( prefix, text.Length - prefix - suffix ) );
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if ( AssociatedObject != null )
        {
            AssociatedObject.TextChanged += AssociatedObjectOnTextChanged;
            AssociatedObject.LostFocus += AssociatedObjectOnLostFocus;
            LoadMacroEntry();
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if ( AssociatedObject != null )
        {
            AssociatedObject.TextChanged -= AssociatedObjectOnTextChanged;
            AssociatedObject.LostFocus -= AssociatedObjectOnLostFocus;
        }

        MacroEntry?.PropertyChanged -= OnMacroEntryPropertyChanged;
    }

    private void AssociatedObjectOnTextChanged( object sender, EventArgs eventArgs )
    {
        if ( sender is not TextEditor textEditor )
        {
            return;
        }

        PushText( textEditor );
    }

    private void AssociatedObjectOnLostFocus( object sender, RoutedEventArgs routedEventArgs )
    {
        if ( sender is not TextEditor textEditor )
        {
            return;
        }

        PushText( textEditor );
    }

    private void PushText( TextEditor textEditor )
    {
        if ( textEditor.Document == null || MacroEntry == null || MacroEntry.Macro == textEditor.Document.Text )
        {
            return;
        }

        MacroEntry.Macro = textEditor.Document.Text;
    }
}
