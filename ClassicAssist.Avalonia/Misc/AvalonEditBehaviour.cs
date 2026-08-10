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
using Avalonia;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;

namespace ClassicAssist.Avalonia.Misc;

/*
 * Feels a bit hacky, there must be a proper way to do this with Observables?
 */
public class AvalonEditBehaviour : Behavior<TextEditor>
{
    public static readonly DirectProperty<AvalonEditBehaviour, string> TextProperty =
        AvaloniaProperty.RegisterDirect<AvalonEditBehaviour, string>( nameof( Text ), o => o.Text,
            ( o, v ) => { o.Text = v; }, defaultBindingMode: BindingMode.TwoWay );

    public string Text
    {
        get;
        set
        {
            SetAndRaise( TextProperty, ref field, value );
            SetText( value );
        }
    }

    /// <summary>
    ///     True for the duration of a <see cref="SetText" /> full-document replace (i.e. a macro
    ///     switch pushing new content in) so other behaviours - notably the breakpoint margin, which
    ///     shifts breakpoint line numbers off <see cref="AvaloniaEdit.Document.TextDocument.Changed" />
    ///     - can tell that apart from a real, incremental user edit.
    /// </summary>
    public static bool IsProgrammaticTextChange { get; private set; }

    private void SetText( string value )
    {
        if ( AssociatedObject?.Document == null )
        {
            return;
        }

        string newValue = value ?? string.Empty;

        // AssociatedObjectOnTextChanged below round-trips typed text back through this setter to
        // keep Text in sync; without this guard that would replace the whole document with content
        // it already holds on every keystroke.
        if ( AssociatedObject.Document.Text.Equals( newValue ) )
        {
            return;
        }

        IsProgrammaticTextChange = true;

        try
        {
            AssociatedObject.Document.Text = newValue;
        }
        finally
        {
            IsProgrammaticTextChange = false;
        }
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if ( AssociatedObject != null )
        {
            AssociatedObject.TextChanged += AssociatedObjectOnTextChanged;
            AssociatedObject.LostFocus += AssociatedObjectOnLostFocus;
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
    }

    private void AssociatedObjectOnTextChanged( object sender, EventArgs eventArgs )
    {
        if ( sender is not TextEditor textEditor )
        {
            return;
        }

        if ( textEditor.Document == null )
        {
            return;
        }

        if ( Text == null || Text.Equals( textEditor.Document.Text ) )
        {
            return;
        }

        int carot = textEditor.CaretOffset;
        Text = textEditor.Document.Text;
        textEditor.CaretOffset = carot;
    }

    private void AssociatedObjectOnLostFocus( object sender, RoutedEventArgs routedEventArgs )
    {
        if ( sender is not TextEditor textEditor )
        {
            return;
        }

        if ( textEditor.Document != null )
        {
            Text = textEditor.Document.Text;
        }
    }
}