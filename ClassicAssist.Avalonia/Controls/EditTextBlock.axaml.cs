using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ClassicAssist.Avalonia.Controls;

public partial class EditTextBlock : UserControl
{
    public static readonly DirectProperty<EditTextBlock, string> TextProperty =
        AvaloniaProperty.RegisterDirect<EditTextBlock, string>( nameof( Text ), o => o.Text, ( o, v ) => o.Text = v,
            defaultBindingMode: BindingMode.TwoWay );

    /// <summary>
    ///     Display text shown in place of <see cref="Text" /> when set (WPF parity: the autoloot tree
    ///     shows "Name - 0xID" while editing edits just the plain name).
    /// </summary>
    public static readonly DirectProperty<EditTextBlock, string> LabelProperty =
        AvaloniaProperty.RegisterDirect<EditTextBlock, string>( nameof( Label ), o => o.Label,
            ( o, v ) => o.Label = v );

    public static readonly DirectProperty<EditTextBlock, bool> ShowIconProperty =
        AvaloniaProperty.RegisterDirect<EditTextBlock, bool>( nameof( ShowIcon ), o => o.ShowIcon,
            ( o, v ) => o.ShowIcon = v, defaultBindingMode: BindingMode.TwoWay );

    public static readonly DirectProperty<EditTextBlock, TextDecorationCollection> TextDecorationsProperty =
        AvaloniaProperty.RegisterDirect<EditTextBlock, TextDecorationCollection>( nameof( TextDecorations ),
            o => o.TextDecorations, ( o, v ) => o.TextDecorations = v );

    // TemplatedControl already declares FontWeight/FontStyle as inherited styled properties, but
    // that inheritance doesn't reach across the UserControl's content presenter into textBlock (see
    // the ElementName bindings in EditTextBlock.axaml) - so these are deliberately new, direct
    // properties forwarded explicitly, the same as TextDecorations above.
    public static new readonly DirectProperty<EditTextBlock, FontWeight> FontWeightProperty =
        AvaloniaProperty.RegisterDirect<EditTextBlock, FontWeight>( nameof( FontWeight ), o => o.FontWeight,
            ( o, v ) => o.FontWeight = v );

    public static new readonly DirectProperty<EditTextBlock, FontStyle> FontStyleProperty =
        AvaloniaProperty.RegisterDirect<EditTextBlock, FontStyle>( nameof( FontStyle ), o => o.FontStyle,
            ( o, v ) => o.FontStyle = v );

    /// <summary>
    ///     Extra controls (e.g. a "choose" or "target" button) shown next to the pencil icon and
    ///     hidden together with it while editing. Ported from WPF's base EditTextBlock, which
    ///     ClilocEditTextBlock/GraphicEditTextBlock rely on for their picker/target buttons.
    /// </summary>
    public static readonly DirectProperty<EditTextBlock, object> ButtonsProperty =
        AvaloniaProperty.RegisterDirect<EditTextBlock, object>( nameof( Buttons ), o => o.Buttons,
            ( o, v ) => o.Buttons = v );

    private readonly Button _pencilButton;
    private readonly StackPanel _buttonsPanel;
    private readonly TextBlock _textBlock;
    private readonly TextBox _textBox;

    public EditTextBlock()
    {
        InitializeComponent();

        _textBox = this.FindControl<TextBox>( "textBox" );
        _textBlock = this.FindControl<TextBlock>( "textBlock" );
        _pencilButton = this.FindControl<Button>( "pencilButton" );
        _buttonsPanel = this.FindControl<StackPanel>( "buttonsPanel" );

        DoubleTapped += ( sender, args ) => HideTextBlock();

        _textBox.KeyDown += ( sender, args ) =>
        {
            if ( args.Key is Key.Enter or Key.Escape )
            {
                args.Handled = true;
                HideTextBox();
            }
            else if ( args.Key == Key.Space )
            {
                // A ComboBox hosting this control handles Key.Space to toggle its dropdown before
                // the TextBox can consume the key, so the space never reaches the text and the edit
                // loses focus - insert the character here and stop it from bubbling up.
                InsertText( " " );
                args.Handled = true;
            }
        };

        _pencilButton.Click += ( sender, args ) =>
        {
            _textBox.LostFocus -= OnLostFocus;
            HideTextBlock();
            _textBox.LostFocus += OnLostFocus;
        };

        _textBox.LostFocus += OnLostFocus;

        UpdateDisplay();
    }

    protected override void OnPropertyChanged( AvaloniaPropertyChangedEventArgs change )
    {
        base.OnPropertyChanged( change );

        if ( change.Property == LabelProperty || change.Property == TextProperty )
        {
            UpdateDisplay();
        }
    }

    /// <summary>
    ///     The read-only display shows <see cref="Label" /> when set (e.g. "Name - 0xID" in the autoloot
    ///     tree), otherwise the editable <see cref="Text" /> - WPF's Label-over-Text behavior.
    /// </summary>
    private void UpdateDisplay()
    {
        _textBlock.Text = string.IsNullOrEmpty( Label ) ? Text : Label;
    }

    public bool ShowIcon
    {
        get;
        set => SetAndRaise( ShowIconProperty, ref field, value );
    }

    public object Buttons
    {
        get;
        set => SetAndRaise( ButtonsProperty, ref field, value );
    }

    public string Label
    {
        get;
        set => SetAndRaise( LabelProperty, ref field, value );
    }

    public TextDecorationCollection TextDecorations
    {
        get;
        set => SetAndRaise( TextDecorationsProperty, ref field, value );
    }

    public new FontWeight FontWeight
    {
        get;
        set => SetAndRaise( FontWeightProperty, ref field, value );
    } = FontWeight.Normal;

    public new FontStyle FontStyle
    {
        get;
        set => SetAndRaise( FontStyleProperty, ref field, value );
    } = FontStyle.Normal;

    public string Text
    {
        get;
        set => SetAndRaise( TextProperty, ref field, value );
    }

    private void OnLostFocus( object sender, RoutedEventArgs args )
    {
        HideTextBox();
    }

    private void HideTextBlock()
    {
        _buttonsPanel.IsVisible = false;
        _textBlock.IsVisible = false;

        _textBox.IsVisible = true;
        _textBox.Focus();
        _textBox.SelectAll();
    }

    private void HideTextBox()
    {
        _textBlock.IsVisible = true;
        _buttonsPanel.IsVisible = true;
        _textBox.IsVisible = false;
        _textBox.SelectionStart = _textBox.SelectionEnd = 0;
    }

    private void InsertText( string text )
    {
        int start = Math.Min( _textBox.SelectionStart, _textBox.SelectionEnd );
        int end = Math.Max( _textBox.SelectionStart, _textBox.SelectionEnd );
        string current = _textBox.Text ?? string.Empty;

        _textBox.Text = current.Remove( start, end - start ).Insert( start, text );

        int caret = start + text.Length;
        _textBox.CaretIndex = caret;
        _textBox.SelectionStart = caret;
        _textBox.SelectionEnd = caret;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}