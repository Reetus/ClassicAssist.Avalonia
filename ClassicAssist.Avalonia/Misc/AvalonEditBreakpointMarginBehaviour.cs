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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     Attaches a <see cref="BreakpointMargin" /> to the macro editor and keeps its breakpoint line
///     numbers in sync with document edits (shifted up/down as lines are inserted/removed above
///     them). Ported from the WPF tree's <c>AvalonEditBreakpointMarginBehaviour</c>.
///     <para>
///         Unlike WPF - which swaps the whole <c>TextDocument</c> instance per macro - this repo's
///         <see cref="AvalonEditBehaviour" /> pushes a plain string into the same persistent
///         document, so a macro switch looks like a full-document <see cref="TextDocument.Changed" />
///         (offset 0, whole old text removed, whole new text inserted). That must not be read as
///         "every breakpoint shifted" - <see cref="AvalonEditBehaviour.IsProgrammaticTextChange" />
///         flags exactly that window so this behaviour can ignore it.
///     </para>
/// </summary>
public class AvalonEditBreakpointMarginBehaviour : Behavior<TextEditor>
{
    public static readonly StyledProperty<ObservableCollection<int>> BreakpointsProperty =
        AvaloniaProperty.Register<AvalonEditBreakpointMarginBehaviour, ObservableCollection<int>>(
            nameof( Breakpoints ) );

    private BreakpointMargin _breakpointMargin;
    private TextDocument _subscribedDocument;
    private TextEditor _textEditor;

    public ObservableCollection<int> Breakpoints
    {
        get => GetValue( BreakpointsProperty );
        set => SetValue( BreakpointsProperty, value );
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        _textEditor = AssociatedObject;

        if ( _textEditor == null )
        {
            return;
        }

        _textEditor.DocumentChanged += OnEditorDocumentChanged;
        SubscribeToDocument( _textEditor.Document );

        if ( Breakpoints != null )
        {
            AddBreakpointMargin( Breakpoints );
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if ( _textEditor != null )
        {
            _textEditor.DocumentChanged -= OnEditorDocumentChanged;
            UnsubscribeFromDocument();
        }

        RemoveBreakpointMargin();
    }

    protected override void OnPropertyChanged( AvaloniaPropertyChangedEventArgs change )
    {
        base.OnPropertyChanged( change );

        if ( change.Property == BreakpointsProperty )
        {
            OnBreakpointsChanged( (ObservableCollection<int>) change.OldValue,
                (ObservableCollection<int>) change.NewValue );
        }
    }

    private void OnBreakpointsChanged( ObservableCollection<int> oldValue, ObservableCollection<int> newValue )
    {
        if ( oldValue != null )
        {
            oldValue.CollectionChanged -= Breakpoints_CollectionChanged;
        }

        if ( _breakpointMargin == null )
        {
            if ( newValue != null )
            {
                AddBreakpointMargin( newValue );
            }

            return;
        }

        _breakpointMargin.Breakpoints = newValue;

        if ( newValue != null )
        {
            newValue.CollectionChanged += Breakpoints_CollectionChanged;
        }

        _breakpointMargin.InvalidateVisual();
    }

    private void OnEditorDocumentChanged( object sender, EventArgs e )
    {
        UnsubscribeFromDocument();
        SubscribeToDocument( _textEditor?.Document );
    }

    private void SubscribeToDocument( TextDocument document )
    {
        if ( document == null )
        {
            return;
        }

        _subscribedDocument = document;
        _subscribedDocument.Changed += Document_Changed;
    }

    private void UnsubscribeFromDocument()
    {
        if ( _subscribedDocument == null )
        {
            return;
        }

        _subscribedDocument.Changed -= Document_Changed;
        _subscribedDocument = null;
    }

    private void Document_Changed( object sender, DocumentChangeEventArgs e )
    {
        if ( AvalonEditBehaviour.IsProgrammaticTextChange )
        {
            return;
        }

        if ( _breakpointMargin?.Breakpoints == null || _breakpointMargin.Breakpoints.Count == 0 )
        {
            return;
        }

        TextDocument doc = _textEditor.Document;

        int startLine = doc.GetLineByOffset( e.Offset ).LineNumber;

        int insertedNewlines = e.InsertionLength > 0 ? e.InsertedText.Text.Count( c => c == '\n' ) : 0;
        int removedNewlines = e.RemovalLength > 0 ? e.RemovedText.Text.Count( c => c == '\n' ) : 0;

        int lineDelta = insertedNewlines - removedNewlines;

        if ( lineDelta != 0 )
        {
            ShiftBreakpoints( startLine, lineDelta );
        }
    }

    private void ShiftBreakpoints( int fromLine, int delta )
    {
        ObservableCollection<int> breakpoints = _breakpointMargin?.Breakpoints;

        if ( breakpoints == null || breakpoints.Count == 0 )
        {
            return;
        }

        List<int> shifted = new( breakpoints.Count );

        foreach ( int bp in breakpoints )
        {
            if ( bp > fromLine )
            {
                int newLine = bp + delta;

                if ( newLine > 0 )
                {
                    shifted.Add( newLine );
                }
            }
            else
            {
                shifted.Add( bp );
            }
        }

        shifted.Sort();

        breakpoints.Clear();

        foreach ( int bp in shifted )
        {
            breakpoints.Add( bp );
        }
    }

    private void AddBreakpointMargin( ObservableCollection<int> breakpoints )
    {
        if ( breakpoints == null || _textEditor == null )
        {
            return;
        }

        BreakpointMargin margin = new() { Breakpoints = breakpoints };
        _textEditor.TextArea.LeftMargins.Insert( 0, margin );
        _breakpointMargin = margin;

        breakpoints.CollectionChanged += Breakpoints_CollectionChanged;
    }

    private void Breakpoints_CollectionChanged( object sender, NotifyCollectionChangedEventArgs e )
    {
        _breakpointMargin?.InvalidateVisual();
    }

    private void RemoveBreakpointMargin()
    {
        if ( _textEditor != null )
        {
            for ( int i = _textEditor.TextArea.LeftMargins.Count - 1; i >= 0; i-- )
            {
                if ( _textEditor.TextArea.LeftMargins[i] is BreakpointMargin )
                {
                    _textEditor.TextArea.LeftMargins.RemoveAt( i );
                }
            }
        }

        if ( _breakpointMargin == null )
        {
            return;
        }

        if ( _breakpointMargin.Breakpoints != null )
        {
            _breakpointMargin.Breakpoints.CollectionChanged -= Breakpoints_CollectionChanged;
        }

        _breakpointMargin = null;
    }
}
