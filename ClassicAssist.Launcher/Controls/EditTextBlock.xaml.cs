using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ClassicAssist.Launcher.Controls
{
    /// <summary>
    ///     Interaction logic for EditTextBlock.xaml
    /// </summary>
    public partial class EditTextBlock
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register( "Text", typeof( string ),
            typeof( EditTextBlock ), new UIPropertyMetadata() );

        public static readonly DependencyProperty CanEditProperty = DependencyProperty.Register( "CanEdit",
            typeof( bool ), typeof( EditTextBlock ), new UIPropertyMetadata( true ) );

        public EditTextBlock()
        {
            InitializeComponent();
        }

        public bool CanEdit
        {
            get => (bool) GetValue( CanEditProperty );
            set => SetValue( CanEditProperty, value );
        }

        public string Text
        {
            get => (string) GetValue( TextProperty );
            set => SetValue( TextProperty, value );
        }

        private void TextBlock_OnMouseDown( object sender, MouseButtonEventArgs e )
        {
            if ( e.ClickCount <= 1 )
            {
                return;
            }

            if ( !CanEdit )
            {
                return;
            }

            ( (TextBlock) sender ).Visibility = Visibility.Collapsed;
            textBox.Visibility = Visibility.Visible;
            textBox.CaretIndex = textBox.Text.Length;
            textBox.SelectAll();

            Dispatcher?.BeginInvoke( (Action) ( () => Keyboard.Focus( textBox ) ), DispatcherPriority.Render );
        }

        private void TextBox_OnKeyDown( object sender, KeyEventArgs e )
        {
            if ( e.Key == Key.Return )
            {
                TextBox_LostFocus( sender, null );
            }
        }

        private void TextBox_LostFocus( object sender, RoutedEventArgs e )
        {
            textBlock.Visibility = Visibility.Visible;
            ( (TextBox) sender ).Visibility = Visibility.Collapsed;
        }
    }
}