using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ClassicAssist.UI.ViewModels;

namespace ClassicAssist.Avalonia.Views
{
    public partial class MacrosGumpTextColorWindow : Window
    {
        private static readonly (string Hex, string Name)[] Palette =
        {
            ( "#FFFFFFFF", "White" ),
            ( "#FF000000", "Black" ),
            ( "#FFFF0000", "Red" ),
            ( "#FF00FF00", "Green" ),
            ( "#FF0000FF", "Blue" ),
            ( "#FFFFFF00", "Yellow" ),
            ( "#FF00FFFF", "Cyan" ),
            ( "#FFFF00FF", "Magenta" ),
            ( "#FF808080", "Gray" ),
            ( "#FFFFA500", "Orange" ),
            ( "#FFA52A2A", "Brown" ),
            ( "#FF800080", "Purple" ),
            ( "#FF000080", "Navy" ),
            ( "#FF008000", "Dark Green" ),
            ( "#FF808000", "Olive" ),
            ( "#FFC0C0C0", "Silver" )
        };

        public MacrosGumpTextColorWindow()
        {
            InitializeComponent();

            BuildPalette();
        }

        private void BuildPalette()
        {
            Panel panel = this.FindControl<Panel>( "palette" );

            foreach ( ( string hex, string name ) in Palette )
            {
                Button button = new Button
                {
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness( 0, 0, 5, 5 ),
                    Padding = new Thickness( 0 ),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    Background = new SolidColorBrush( Color.Parse( hex ) )
                };

                ToolTip.SetTip( button, name );
                button.Click += ( _, _ ) => SetColor( hex );

                panel.Children.Add( button );
            }
        }

        private void SetColor( string hex )
        {
            if ( DataContext is MacrosGumpTextColorSelectorViewModel vm )
            {
                vm.SelectedColor = hex;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load( this );
        }
    }
}
