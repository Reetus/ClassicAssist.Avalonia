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

using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     Highlights the currently-paused line in the macro editor (translucent yellow) and scrolls it
///     into view. Ported from the WPF tree's <c>AvalonEditPausedLineBehaviour</c>.
/// </summary>
public class AvalonEditPausedLineBehaviour : Behavior<TextEditor>
{
    public static readonly StyledProperty<bool> IsPausedProperty =
        AvaloniaProperty.Register<AvalonEditPausedLineBehaviour, bool>( nameof( IsPaused ) );

    public static readonly StyledProperty<int> PausedLineNumberProperty =
        AvaloniaProperty.Register<AvalonEditPausedLineBehaviour, int>( nameof( PausedLineNumber ) );

    private TextEditor _textEditor;

    public bool IsPaused
    {
        get => GetValue( IsPausedProperty );
        set => SetValue( IsPausedProperty, value );
    }

    public int PausedLineNumber
    {
        get => GetValue( PausedLineNumberProperty );
        set => SetValue( PausedLineNumberProperty, value );
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        _textEditor = AssociatedObject;
    }

    protected override void OnPropertyChanged( AvaloniaPropertyChangedEventArgs change )
    {
        base.OnPropertyChanged( change );

        if ( change.Property != IsPausedProperty && change.Property != PausedLineNumberProperty )
        {
            return;
        }

        if ( _textEditor == null )
        {
            return;
        }

        if ( IsPaused )
        {
            AddHighlighter();
        }
        else
        {
            RemoveHighlighter();
        }
    }

    private void RemoveHighlighter()
    {
        PausedLineHighlighter existing =
            _textEditor.TextArea.TextView.BackgroundRenderers.OfType<PausedLineHighlighter>().FirstOrDefault();

        if ( existing == null )
        {
            return;
        }

        _textEditor.TextArea.TextView.BackgroundRenderers.Remove( existing );
    }

    private void AddHighlighter()
    {
        PausedLineHighlighter highlighter = GetOrCreateHighlighter( _textEditor );
        highlighter.IsPaused = true;
        highlighter.PausedLine = PausedLineNumber;
    }

    private static PausedLineHighlighter GetOrCreateHighlighter( TextEditor editor )
    {
        PausedLineHighlighter existing =
            editor.TextArea.TextView.BackgroundRenderers.OfType<PausedLineHighlighter>().FirstOrDefault();

        if ( existing != null )
        {
            return existing;
        }

        PausedLineHighlighter h = new( editor );
        editor.TextArea.TextView.BackgroundRenderers.Add( h );
        return h;
    }
}

internal class PausedLineHighlighter : IBackgroundRenderer
{
    private readonly TextEditor _editor;

    public PausedLineHighlighter( TextEditor editor )
    {
        _editor = editor;
    }

    public bool IsPaused
    {
        get;
        set
        {
            if ( field == value )
            {
                return;
            }

            field = value;
            _editor.TextArea.TextView.InvalidateLayer( KnownLayer.Background );
            ScrollToLineIfPaused();
        }
    }

    public int PausedLine
    {
        get;
        set
        {
            if ( field == value )
            {
                return;
            }

            field = value;
            _editor.TextArea.TextView.InvalidateLayer( KnownLayer.Background );
            ScrollToLineIfPaused();
        }
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw( TextView textView, DrawingContext drawingContext )
    {
        if ( !IsPaused || PausedLine <= 0 || PausedLine > _editor.Document.LineCount )
        {
            return;
        }

        textView.EnsureVisualLines();
        DocumentLine line = _editor.Document.GetLineByNumber( PausedLine );
        IEnumerable<Rect> rects = BackgroundGeometryBuilder.GetRectsForSegment( textView, line );

        foreach ( Rect r in rects )
        {
            drawingContext.DrawRectangle( new SolidColorBrush( Color.FromArgb( 80, 255, 255, 0 ) ),
                null, new Rect( r.Position, new Size( textView.Bounds.Width, r.Height ) ) );
        }
    }

    private void ScrollToLineIfPaused()
    {
        if ( IsPaused && PausedLine > 0 && PausedLine <= _editor.Document.LineCount )
        {
            _editor.ScrollTo( PausedLine, 0 );
        }
    }
}
