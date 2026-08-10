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
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     Macro command completion and hover documentation for the macro editor. Ported from the WPF
///     tree's <c>AvalonEditShowCompletionTooltipBehaviour</c>.
///     <para>
///         The command list is reflected out of the assembly that actually holds the commands
///         (ClassicAssist.Shared) rather than the executing one - upstream is a single assembly where
///         <c>Assembly.GetExecutingAssembly()</c> happened to be both.
///     </para>
/// </summary>
public class AvalonEditCompletionBehaviour : Behavior<TextEditor>
{
    private const int MIN_CHARS = 3;

    public static readonly StyledProperty<bool> IsPausedProperty =
        AvaloniaProperty.Register<AvalonEditCompletionBehaviour, bool>( nameof( IsPaused ) );

    public static readonly StyledProperty<Dictionary<string, object>> FrameVariablesProperty =
        AvaloniaProperty.Register<AvalonEditCompletionBehaviour, Dictionary<string, object>>(
            nameof( FrameVariables ) );

    private static List<PythonCompletionData> _completionData;

    private CompletionWindow _completionWindow;
    private TextEditor _textEditor;

    /// <summary>
    ///     Whether the macro currently bound to this editor is paused at a breakpoint - while true,
    ///     hovering an identifier that isn't a known command looks it up in
    ///     <see cref="FrameVariables" /> instead.
    /// </summary>
    public bool IsPaused
    {
        get => GetValue( IsPausedProperty );
        set => SetValue( IsPausedProperty, value );
    }

    public Dictionary<string, object> FrameVariables
    {
        get => GetValue( FrameVariablesProperty );
        set => SetValue( FrameVariablesProperty, value );
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        _textEditor = AssociatedObject;

        if ( _textEditor == null )
        {
            return;
        }

        _completionData ??= BuildCompletionData();

        _textEditor.TextArea.TextEntered += OnTextEntered;
        _textEditor.PointerHover += OnPointerHover;
        _textEditor.PointerHoverStopped += OnPointerHoverStopped;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if ( _textEditor == null )
        {
            return;
        }

        _textEditor.TextArea.TextEntered -= OnTextEntered;
        _textEditor.PointerHover -= OnPointerHover;
        _textEditor.PointerHoverStopped -= OnPointerHoverStopped;

        _completionWindow?.Close();
        _completionWindow = null;
    }

    /// <summary>
    ///     Reflects every <see cref="CommandsDisplayAttribute" />-decorated static method into a
    ///     completion entry labelled with its full signature. Static because the reflection pass is
    ///     the expensive part and the result never changes within a run.
    /// </summary>
    private static List<PythonCompletionData> BuildCompletionData()
    {
        List<PythonCompletionData> data = [];

        IEnumerable<Type> types = typeof( AliasCommands ).Assembly.GetTypes().Where( t =>
            t.Namespace != null && t.IsPublic && t.IsClass && t.Namespace.EndsWith( "Macros.Commands" ) );

        foreach ( Type type in types )
        {
            foreach ( MethodInfo methodInfo in type.GetMethods( BindingFlags.Public | BindingFlags.Static ) )
            {
                CommandsDisplayAttribute attr = methodInfo.GetCustomAttribute<CommandsDisplayAttribute>();

                if ( attr == null )
                {
                    continue;
                }

                string fullName = $"{methodInfo.Name}(";
                bool first = true;

                foreach ( ParameterInfo parameterInfo in methodInfo.GetParameters() )
                {
                    if ( first )
                    {
                        first = false;
                    }
                    else
                    {
                        fullName += ", ";
                    }

                    bool optional = parameterInfo.HasDefaultValue;

                    fullName +=
                        $"{( optional ? "[" : "" )}{parameterInfo.ParameterType.Name} {parameterInfo.Name}{( optional ? "]" : "" )}";
                }

                fullName += $"):{methodInfo.ReturnType.Name}";

                data.Add( new PythonCompletionData( methodInfo.Name, fullName, attr.Description,
                    attr.InsertText ) );
            }
        }

        return data;
    }

    private void OnTextEntered( object sender, TextInputEventArgs e )
    {
        if ( _textEditor?.Document == null )
        {
            return;
        }

        DocumentLine line = _textEditor.Document.Lines[_textEditor.TextArea.Caret.Line - 1];

        string trimmed = _textEditor.Document.GetText( line ).TrimStart( ' ', '\t' );

        if ( trimmed.Length < MIN_CHARS )
        {
            _completionWindow?.Close();

            return;
        }

        List<PythonCompletionData> data = [.. _completionData
            .Where( m => m.Name.StartsWith( trimmed, StringComparison.InvariantCultureIgnoreCase ) )
            .GroupBy( m => m.Name ).Select( g => g.First() )];

        if ( data.Count == 0 )
        {
            _completionWindow?.Close();

            return;
        }

        // Reuse the open window rather than stacking a new popup per keystroke.
        if ( _completionWindow == null )
        {
            _completionWindow = new CompletionWindow( _textEditor.TextArea )
            {
                CloseWhenCaretAtBeginning = true,
                CloseAutomatically = false,
                Width = 500,
                MaxHeight = 300
            };

            _completionWindow.Closed += ( _, _ ) => _completionWindow = null;
        }
        else
        {
            _completionWindow.CompletionList.CompletionData.Clear();
        }

        foreach ( PythonCompletionData item in data )
        {
            item.Content ??= BuildEntryContent( item );

            _completionWindow.CompletionList.CompletionData.Add( item );
        }

        _completionWindow.Show();
    }

    /// <summary>
    ///     The row shown in the popup. WPF uses a CompletionEntry UserControl with an expander holding
    ///     a read-only editor for the example; this is the signature over the example text, which is
    ///     the part that actually helps while typing.
    /// </summary>
    private static Control BuildEntryContent( PythonCompletionData item )
    {
        StackPanel panel = new() { Orientation = Orientation.Vertical };

        panel.Children.Add( new TextBlock { Text = item.Name, FontWeight = FontWeight.Bold } );

        if ( !string.IsNullOrWhiteSpace( item.Example ) )
        {
            panel.Children.Add( new TextBlock
            {
                Text = item.Example,
                Opacity = 0.7,
                FontFamily = new FontFamily( "monospace" ),
                TextWrapping = TextWrapping.NoWrap
            } );
        }

        return panel;
    }

    private void OnPointerHoverStopped( object sender, PointerEventArgs e )
    {
        ToolTip.SetIsOpen( _textEditor, false );
    }

    private void OnPointerHover( object sender, PointerEventArgs e )
    {
        TextViewPosition? position = _textEditor.GetPositionFromPoint( e.GetPosition( _textEditor ) );

        if ( !position.HasValue )
        {
            return;
        }

        string word = GetWordAt( position.Value );

        if ( string.IsNullOrEmpty( word ) )
        {
            return;
        }

        PythonCompletionData[] matches =
            [.. _completionData.Where( i => i.MethodName.Equals( word ) )];

        Control panel;

        if ( matches.Length > 0 )
        {
            StackPanel stack = new() { Orientation = Orientation.Vertical, Margin = new Thickness( 5 ) };

            foreach ( PythonCompletionData match in matches )
            {
                stack.Children.Add( new TextBlock
                {
                    Text = match.Description?.ToString(),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 400,
                    Margin = new Thickness( 0, 2, 0, 5 )
                } );

                stack.Children.Add( new TextBlock
                {
                    Text = match.Name,
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness( 0, 5, 0, 0 )
                } );
            }

            panel = stack;
        }
        else if ( IsPaused && FrameVariables != null )
        {
            panel = BuildFrameVariablePanel( word );

            if ( panel == null )
            {
                return;
            }
        }
        else
        {
            return;
        }

        // IsOpen is an attached property in Avalonia - there is no instance ToolTip.IsOpen as in
        // WPF, and the tip has to be re-set before showing so the content refreshes.
        ToolTip.SetIsOpen( _textEditor, false );
        ToolTip.SetTip( _textEditor, panel );
        ToolTip.SetIsOpen( _textEditor, true );

        e.Handled = true;
    }

    /// <summary>
    ///     Local-variable hover while paused at a breakpoint - the frame-variable half of WPF's
    ///     <c>AvalonEditShowCompletionTooltipBehaviour</c>.
    /// </summary>
    private Control BuildFrameVariablePanel( string word )
    {
        KeyValuePair<string, object>[] frameVariables =
            [.. FrameVariables.Where( kvp => kvp.Key.Equals( word ) )];

        if ( frameVariables.Length == 0 )
        {
            return null;
        }

        StackPanel panel = new() { Orientation = Orientation.Vertical, Margin = new Thickness( 5 ) };

        foreach ( KeyValuePair<string, object> variable in frameVariables )
        {
            panel.Children.Add( new TextBlock
            {
                Text = $"{variable.Key} : {variable.Value?.GetType().Name ?? "null"}",
                FontWeight = FontWeight.Bold,
                Margin = new Thickness( 0, 5, 0, 0 )
            } );

            panel.Children.Add( new TextBlock
            {
                Text = MacroInvoker.GetDisplayValue( variable.Value ),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 400,
                Margin = new Thickness( 0, 2, 0, 5 )
            } );
        }

        return panel;
    }

    private string GetWordAt( TextViewPosition position )
    {
        try
        {
            DocumentLine line = _textEditor.Document.GetLineByNumber( position.Line );

            if ( line == null )
            {
                return null;
            }

            string text = _textEditor.Document.GetText( line.Offset, line.Length );

            // Column is 1-based and can sit one past the last character.
            int column = Math.Min( position.Column - 1, text.Length - 1 );

            if ( column < 0 )
            {
                return null;
            }

            int start = 0;
            int end = text.Length;

            for ( int i = column; i >= 0; i-- )
            {
                if ( IsWordChar( text[i] ) )
                {
                    continue;
                }

                start = i + 1;
                break;
            }

            for ( int i = column; i < text.Length; i++ )
            {
                if ( IsWordChar( text[i] ) )
                {
                    continue;
                }

                end = i;
                break;
            }

            return end <= start ? null : text[start..end];
        }
        catch ( Exception )
        {
            return null;
        }
    }

    private static bool IsWordChar( char c )
    {
        return char.IsLetterOrDigit( c ) || c == '_';
    }
}
