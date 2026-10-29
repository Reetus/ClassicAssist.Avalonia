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

        // HighlightingLoader.Load fully parses the definition before returning, so the reader (and
        // the asset stream it wraps) can be disposed immediately afterwards.
        using Stream stream = AssetLoader.Open( new Uri( "avares://ClassicAssist.Avalonia/Assets/Python.Dark.xshd" ) );
        using XmlTextReader reader = new( stream );

        textEditor.SyntaxHighlighting = HighlightingLoader.Load( reader, HighlightingManager.Instance );

        // Wires up Ctrl+F (and Ctrl+H for replace) the same as the WPF build's AvalonEdit editor.
        SearchPanel.Install( textEditor );
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}