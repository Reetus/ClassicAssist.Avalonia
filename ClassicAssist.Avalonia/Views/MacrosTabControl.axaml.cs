using System;
using System.IO;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Search;

namespace ClassicAssist.Avalonia.Views;

public partial class MacrosTabControl : UserControl
{
    public MacrosTabControl()
    {
        InitializeComponent();

        TextEditor textEditor = this.FindControl<TextEditor>( "Editor" );
        textEditor.Background = Brushes.Transparent;
        textEditor.ShowLineNumbers = true;
        textEditor.Options.ConvertTabsToSpaces = true;

        Stream stream = AssetLoader.Open( new Uri( "avares://ClassicAssist.Avalonia/Assets/Python.Dark.xshd" ) );

        textEditor.SyntaxHighlighting = HighlightingLoader.Load(
            new XmlTextReader( stream ), HighlightingManager.Instance );

        // Wires up Ctrl+F (and Ctrl+H for replace) the same as the WPF build's AvalonEdit editor.
        SearchPanel.Install( textEditor );
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}