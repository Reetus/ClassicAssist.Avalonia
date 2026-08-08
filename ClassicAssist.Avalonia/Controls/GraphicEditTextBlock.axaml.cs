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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using Commands = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.Avalonia.Controls
{
    /// <summary>
    ///     A graphic-ID-valued <see cref="EditTextBlock" />: shows the hex ID plus a resolved tile name (or
    ///     "Any" for -1), and lets it be set either by typing the hex/decimal ID directly or targeting an
    ///     item/tile. Ported from WPF's
    ///     <c>UI/Views/ECV/Settings/Controls/EditTextBlocks/GraphicEditTextBlock</c>, minus the WPF
    ///     version's cross-wiring to sibling Cliloc/Hue columns - each field here is set independently.
    /// </summary>
    public partial class GraphicEditTextBlock : UserControl
    {
        public static readonly DirectProperty<GraphicEditTextBlock, int> IDProperty =
            AvaloniaProperty.RegisterDirect<GraphicEditTextBlock, int>( nameof( ID ), o => o.ID,
                ( o, v ) => o.ID = v, -1, BindingMode.TwoWay );

        public static readonly DirectProperty<GraphicEditTextBlock, string> LabelProperty =
            AvaloniaProperty.RegisterDirect<GraphicEditTextBlock, string>( nameof( Label ), o => o.Label );

        private int _id = -1;
        private string _label;

        public GraphicEditTextBlock()
        {
            InitializeComponent();

            UpdateLabel();
        }

        public int ID
        {
            get => _id;
            set
            {
                SetAndRaise( IDProperty, ref _id, value );
                UpdateLabel();
            }
        }

        public string Label
        {
            get => _label;
            private set => SetAndRaise( LabelProperty, ref _label, value );
        }

        private void UpdateLabel()
        {
            if ( _id == -1 )
            {
                Label = "Any";

                return;
            }

            string name = TileData.GetStaticTile( _id ).Name;

            Label = string.IsNullOrEmpty( name ) ? $"0x{_id:x8}" : $"0x{_id:x8} ({name})";
        }

        private async void OnTargetClick( object sender, RoutedEventArgs e )
        {
            ( TargetType _, TargetFlags _, int serial, int _, int _, int _, int itemId ) =
                await Commands.GetTargetInfoAsync( Strings.Target_object___ );

            if ( serial <= 0 )
            {
                return;
            }

            if ( itemId != 0 )
            {
                ID = itemId;

                return;
            }

            Item item = Engine.Items.GetItem( serial );

            if ( item != null )
            {
                ID = item.ID;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load( this );
        }
    }
}
