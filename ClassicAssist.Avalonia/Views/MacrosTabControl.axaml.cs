﻿using System;
using System.IO;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace ClassicAssist.Avalonia.Views
{
    public partial class MacrosTabControl : UserControl
    {
        public MacrosTabControl()
        {
            InitializeComponent();

            TextEditor textEditor = this.FindControl<TextEditor>( "Editor" );
            textEditor.Background = Brushes.Transparent;
            textEditor.ShowLineNumbers = true;
            textEditor.Options.ConvertTabsToSpaces = true;

            Stream stream = AssetLoader
                .Open( new Uri( "avares://ClassicAssist.Avalonia/Assets/Python.Dark.xshd" ) );

            textEditor.SyntaxHighlighting = HighlightingLoader.Load(
                new XmlTextReader( stream ), HighlightingManager.Instance );
        }
    }
}