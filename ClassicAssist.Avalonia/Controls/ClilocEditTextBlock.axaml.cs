#region License

// Copyright (C) 2026 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY

#endregion

using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UI.ViewModels.Autoloot;
using ClassicAssist.UO.Objects;
using Commands = ClassicAssist.Shared.UO.Commands;
using UOCliloc = ClassicAssist.UO.Data.Cliloc;

namespace ClassicAssist.Avalonia.Controls;

/// <summary>
///     A cliloc-valued <see cref="EditTextBlock" />: shows the resolved cliloc text (or "Any" for -1),
///     and lets it be set either by typing the numeric ID directly, searching by name (
///     <see cref="ClilocSelectionViewModel" />/<c>ClilocSelectionWindow</c>), or targeting an item and
///     reading its first property's cliloc. Ported from WPF's
///     <c>UI/Views/ECV/Settings/Controls/EditTextBlocks/ClilocEditTextBlock</c>, minus the WPF version's
///     cross-wiring to sibling ID/Hue columns - each field here is set independently.
/// </summary>
public partial class ClilocEditTextBlock : UserControl
{
    public static readonly DirectProperty<ClilocEditTextBlock, int> ClilocProperty =
        AvaloniaProperty.RegisterDirect<ClilocEditTextBlock, int>( nameof( Cliloc ), o => o.Cliloc,
            ( o, v ) => o.Cliloc = v, -1, BindingMode.TwoWay );

    public static readonly DirectProperty<ClilocEditTextBlock, string> LabelProperty =
        AvaloniaProperty.RegisterDirect<ClilocEditTextBlock, string>( nameof( Label ), o => o.Label );

    public ClilocEditTextBlock()
    {
        InitializeComponent();

        UpdateLabel();
    }

    public int Cliloc
    {
        get;
        set
        {
            SetAndRaise( ClilocProperty, ref field, value );
            UpdateLabel();
        }
    } = -1;

    public string Label
    {
        get;
        private set => SetAndRaise( LabelProperty, ref field, value );
    }

    private void UpdateLabel()
    {
        Label = Cliloc == -1 ? "Any" : UOCliloc.GetProperty( Cliloc );
    }

    private async void OnChooseClick( object sender, RoutedEventArgs e )
    {
        ClilocSelectionViewModel vm = new();

        // Must be awaited: InvokeDialog completes when the dialog closes, so without this the
        // DialogResult check below runs before the user has even seen the window and always takes
        // the early return.
        await Engine.UIInvoker.InvokeDialog( "ClilocSelectionWindow", dataContext: vm );

        if ( vm.DialogResult != MessageBoxResult.OK )
        {
            return;
        }

        Cliloc = vm.SelectedCliloc.Key;
    }

    private async void OnTargetClick( object sender, RoutedEventArgs e )
    {
        int serial = await Commands.GetTargetSerialAsync( Strings.Target_object___ );

        if ( serial == 0 )
        {
            return;
        }

        Item item = Engine.Items.GetItem( serial );

        Cliloc = item?.Properties?.Select( p => p.Cliloc ).FirstOrDefault() ?? -1;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}
