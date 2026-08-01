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

        public static readonly DirectProperty<EditTextBlock, bool> ShowIconProperty =
            AvaloniaProperty.RegisterDirect<EditTextBlock, bool>( nameof( ShowIcon ), o => o.ShowIcon,
                ( o, v ) => o.ShowIcon = v, defaultBindingMode: BindingMode.TwoWay );

        public static readonly DirectProperty<EditTextBlock, TextDecorationCollection> TextDecorationsProperty =
            AvaloniaProperty.RegisterDirect<EditTextBlock, TextDecorationCollection>( nameof( TextDecorations ),
                o => o.TextDecorations, ( o, v ) => o.TextDecorations = v );

        private readonly Button _pencilButton;
        private bool _showIcon;

        private string _text;
        private TextDecorationCollection _textDecorations;
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
                    HideTextBox();
                }
            };

            _pencilButton.Click += ( sender, args ) =>
            {
                _textBox.LostFocus -= OnLostFocus;
                HideTextBlock();
                _textBox.LostFocus += OnLostFocus;
            };

            _textBox.LostFocus += OnLostFocus;
        }

        public bool ShowIcon
        {
            get => _showIcon;
            set => SetAndRaise( ShowIconProperty, ref _showIcon, value );
        }

        public TextDecorationCollection TextDecorations
        {
            get => _textDecorations;
            set => SetAndRaise( TextDecorationsProperty, ref _textDecorations, value );
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

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load( this );
        }
    }
}