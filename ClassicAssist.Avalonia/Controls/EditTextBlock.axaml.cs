using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ClassicAssist.Avalonia.Controls
{
    public partial class EditTextBlock : UserControl
    {
        public static readonly DirectProperty<EditTextBlock, string> TextProperty =
            AvaloniaProperty.RegisterDirect<EditTextBlock, string>( nameof( Text ), o => o.Text, ( o, v ) => o.Text = v,
                defaultBindingMode: BindingMode.TwoWay );

        public static readonly StyledProperty<bool> ShowIconProperty = AvaloniaProperty.Register<EditTextBlock, bool>(
            nameof( ShowIcon ),
            defaultBindingMode: BindingMode.TwoWay
        );

        private readonly Button _pencilButton;

        private string _text;
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
            get => GetValue( ShowIconProperty );
            set => SetValue( ShowIconProperty, value );
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
    }
}