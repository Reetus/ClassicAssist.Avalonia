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
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace ClassicAssist.Avalonia.Misc
{
    /// <summary>
    ///     Gutter margin drawing/toggling breakpoints on click. Ported from the WPF tree's
    ///     <c>BreakpointMargin</c>; AvaloniaEdit's <c>AbstractMargin</c> mirrors AvalonEdit's shape
    ///     closely enough that this is mostly a 1:1 port onto Avalonia's Control overrides
    ///     (Render/OnPointerPressed/OnPointerMoved instead of OnRender/OnMouseDown/OnMouseMove).
    /// </summary>
    public class BreakpointMargin : AbstractMargin
    {
        private const double _radius = 5;
        private int? _hoveredLine;

        public BreakpointMargin()
        {
            ClipToBounds = true;
            IsHitTestVisible = true;
            Cursor = new Cursor( StandardCursorType.Hand );
        }

        public ObservableCollection<int> Breakpoints { get; set; }

        public event Action<int> BreakpointToggled;

        protected override Size MeasureOverride( Size availableSize )
        {
            return new Size( 20, 0 );
        }

        protected override void OnPointerPressed( PointerPressedEventArgs e )
        {
            base.OnPointerPressed( e );

            TextView textView = TextView;

            if ( textView == null || !textView.VisualLinesValid || Breakpoints == null )
            {
                return;
            }

            Point pos = e.GetPosition( textView );
            VisualLine visualLine = textView.GetVisualLineFromVisualTop( pos.Y + textView.VerticalOffset );

            if ( visualLine == null )
            {
                return;
            }

            int lineNumber = visualLine.FirstDocumentLine.LineNumber;

            if ( Breakpoints.Contains( lineNumber ) )
            {
                Breakpoints.Remove( lineNumber );
            }
            else
            {
                Breakpoints.Add( lineNumber );
            }

            BreakpointToggled?.Invoke( lineNumber );
            InvalidateVisual();

            e.Handled = true;
        }

        protected override void OnPointerMoved( PointerEventArgs e )
        {
            base.OnPointerMoved( e );

            TextView textView = TextView;

            if ( textView == null || !textView.VisualLinesValid )
            {
                return;
            }

            Point pos = e.GetPosition( textView );
            VisualLine visualLine = textView.GetVisualLineFromVisualTop( pos.Y + textView.VerticalOffset );
            int? newHoveredLine = visualLine?.FirstDocumentLine.LineNumber;

            if ( newHoveredLine != _hoveredLine )
            {
                _hoveredLine = newHoveredLine;
                InvalidateVisual();
            }
        }

        protected override void OnPointerExited( PointerEventArgs e )
        {
            base.OnPointerExited( e );

            _hoveredLine = null;
            InvalidateVisual();
        }

        protected override void OnTextViewChanged( TextView oldTextView, TextView newTextView )
        {
            base.OnTextViewChanged( oldTextView, newTextView );

            if ( oldTextView != null )
            {
                oldTextView.VisualLinesChanged -= TextView_VisualLinesChanged;
                oldTextView.ScrollOffsetChanged -= TextView_ScrollOffsetChanged;
            }

            if ( newTextView != null )
            {
                newTextView.VisualLinesChanged += TextView_VisualLinesChanged;
                newTextView.ScrollOffsetChanged += TextView_ScrollOffsetChanged;
            }
        }

        private void TextView_VisualLinesChanged( object sender, EventArgs e )
        {
            InvalidateVisual();
        }

        private void TextView_ScrollOffsetChanged( object sender, EventArgs e )
        {
            InvalidateVisual();
        }

        public override void Render( DrawingContext dc )
        {
            base.Render( dc );

            // Avalonia's hit-testing is content-based: a point only hits this control where something
            // was actually painted (this is what WPF's BreakpointMargin.HitTestCore override achieved
            // by forcing a hit regardless of paint). A margin with no breakpoints yet draws nothing at
            // all, so without this the gutter is entirely unclickable until a breakpoint already exists
            // - painting a transparent rect across the full bounds makes the whole strip hit-testable.
            dc.DrawRectangle( Brushes.Transparent, null, new Rect( Bounds.Size ) );

            TextView textView = TextView;

            if ( textView == null || !textView.VisualLinesValid || Breakpoints == null )
            {
                return;
            }

            foreach ( VisualLine visualLine in textView.VisualLines )
            {
                int lineNumber = visualLine.FirstDocumentLine.LineNumber;

                if ( !Breakpoints.Contains( lineNumber ) )
                {
                    continue;
                }

                double y = visualLine.GetTextLineVisualYPosition( visualLine.TextLines[0], VisualYPosition.TextTop ) -
                    textView.VerticalOffset;

                Point center = new Point( 10, y + visualLine.Height / 2 );
                dc.DrawEllipse( Brushes.Red, null, center, _radius, _radius );
            }
        }
    }
}
