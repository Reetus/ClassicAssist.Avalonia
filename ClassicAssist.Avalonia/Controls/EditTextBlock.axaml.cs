using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ClassicAssist.Avalonia.Controls
{
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

        private readonly Button _pencilButton;
        private string _label;
        private bool _showIcon;

        private string _text;
        private TextDecorationCollection _textDecorations;
        private FontWeight _fontWeight = FontWeight.Normal;
        private FontStyle _fontStyle = FontStyle.Normal;
        private readonly TextBlock _textBlock;
        private readonly TextBox _textBox;

        public EditTextBlock()
        {
            InitializeComponent();

            _textBox = this.FindControl<TextBox>( "textBox" );
            _textBlock = this.FindControl<TextBlock>( "textBlock" );
            _pencilButton = this.FindControl<Button>( "pencilButton" );

            DoubleTapped += ( sender, args ) => HideTextBlock();

            _textBox.KeyDown += ( sender, args ) =>
            {
                if ( args.Key == Key.Enter || args.Key == Key.Escape )
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
            get => _showIcon;
            set => SetAndRaise( ShowIconProperty, ref _showIcon, value );
        }

        public string Label
        {
            get => _label;
            set => SetAndRaise( LabelProperty, ref _label, value );
        }

        public TextDecorationCollection TextDecorations
        {
            get => _textDecorations;
            set => SetAndRaise( TextDecorationsProperty, ref _textDecorations, value );
        }

        public new FontWeight FontWeight
        {
            get => _fontWeight;
            set => SetAndRaise( FontWeightProperty, ref _fontWeight, value );
        }

        public new FontStyle FontStyle
        {
            get => _fontStyle;
            set => SetAndRaise( FontStyleProperty, ref _fontStyle, value );
        }

        public string Text
        {
            get => _text;
            set => SetAndRaise( TextProperty, ref _text, value );
        }

        private void OnLostFocus( object sender, RoutedEventArgs args )
        {
            HideTextBox();
        }

        private void HideTextBlock()
        {
            _pencilButton.IsVisible = false;
            _textBlock.IsVisible = false;

            _textBox.IsVisible = true;
            _textBox.Focus();
            _textBox.SelectAll();
        }

        private void HideTextBox()
        {
            _textBlock.IsVisible = true;
            _pencilButton.IsVisible = ShowIcon;
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
}