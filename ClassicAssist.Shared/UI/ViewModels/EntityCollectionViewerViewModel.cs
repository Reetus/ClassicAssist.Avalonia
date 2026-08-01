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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UI.Models;
using ClassicAssist.UO;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json;
using Commands = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.UI.ViewModels
{
    /// <summary>
    ///     Backs the entity collection viewer: the grid of item art behind "Show World Items" and behind
    ///     opening a container.
    ///     <para>
    ///         This is a subset of the WPF view model. Browsing, sorting, refreshing and drilling into
    ///         containers are here; the filter editor, the organizer, the queued move/loot actions and the
    ///         settings window are not yet ported, and neither are the toolbar commands that drive them.
    ///     </para>
    /// </summary>
    public class EntityCollectionViewerViewModel : BaseViewModel
    {
        private readonly Func<ItemCollection> _customRefresh;

        /// <summary>
        ///     Names discovered from the server that the tile data does not carry. Kept per viewer so that a
        ///     container whose contents have not been queried yet still shows something useful.
        /// </summary>
        private readonly Dictionary<int, string> _nameOverrides = new Dictionary<int, string>();

        private ItemCollection _collection = new ItemCollection( 0 );
        private ObservableCollection<EntityCollectionData> _entities =
            new ObservableCollection<EntityCollectionData>();
        private ICommand _contextContextMenuRequestCommand;
        private ICommand _contextCustomActionCommand;
        private ICommand _contextDropToGroundCommand;
        private ICommand _contextMoveToBackpackCommand;
        private ICommand _contextMoveToBankCommand;
        private ICommand _contextMoveToContainerCommand;
        private ICommand _contextMoveToGroundCommand;
        private ICommand _contextOpenContainerCommand;
        private ICommand _contextTargetCommand;
        private ICommand _contextTargetOwnerCommand;
        private ICommand _contextToggleLockCommand;
        private ICommand _contextUseItemCommand;
        private ICommand _copyToClipboardCommand;
        private ICommand _equipItemCommand;
        private ICommand _hideItemCommand;
        private ICommand _itemDoubleClickCommand;
        private ICommand _refreshCommand;

        private ObservableCollection<EntityCollectionData> _selectedItems =
            new ObservableCollection<EntityCollectionData>();

        private bool _showChildItems;
        private bool _showProperties;
        private EntityCollectionSortStyle _sortStyle = EntityCollectionSortStyle.ID;
        private string _statusLabel;
        private ICommand _toggleChildItemsCommand;
        private ICommand _togglePropertiesCommand;

        public EntityCollectionViewerViewModel() : this( new ItemCollection( 0 ) )
        {
        }

        // Spelled out rather than given a default argument: the UI invoker constructs view models through
        // Activator.CreateInstance, which does not fill optional parameters in.
        public EntityCollectionViewerViewModel( ItemCollection collection ) : this( collection, null )
        {
        }

        public EntityCollectionViewerViewModel( ItemCollection collection, Func<ItemCollection> customRefresh )
        {
            _customRefresh = customRefresh;

            Collection = collection ?? new ItemCollection( 0 );

            Rebuild();

            SelectedItems.CollectionChanged += OnSelectedItemsChanged;
            Collection.CollectionChanged += OnCollectionChanged;
        }

        /// <summary>
        ///     Maps a mount's equipment item ID to the statue graphic the client draws for it. Empty when the
        ///     data file is absent, in which case mounts simply fall back to their own art.
        /// </summary>
        public static Lazy<Dictionary<int, int>> MountIDEntries { get; set; } =
            new Lazy<Dictionary<int, int>>( LoadMountIDEntries );

        public ItemCollection Collection
        {
            get => _collection;
            set => SetProperty( ref _collection, value );
        }

        public ICommand ContextContextMenuRequestCommand =>
            _contextContextMenuRequestCommand ?? ( _contextContextMenuRequestCommand =
                new RelayCommand( ContextMenuRequest, o => SelectedItems.Count > 0 ) );

        /// <summary>
        ///     Sourced from <see cref="CustomContextActions" />, a thin, non-registry extension point old-side
        ///     (unlike the toolbar's <c>IEntityCollectionViewerAction</c> registry). Nothing populates it yet,
        ///     so the "Custom Actions" submenu stays empty/hidden until a caller does.
        /// </summary>
        public ICommand ContextCustomActionCommand =>
            _contextCustomActionCommand ?? ( _contextCustomActionCommand =
                new RelayCommand( ContextCustomAction, o => SelectedItems.Count > 0 ) );

        public ICommand ContextDropToGroundCommand =>
            _contextDropToGroundCommand ?? ( _contextDropToGroundCommand =
                new RelayCommandAsync( ContextDropToGround, o => SelectedItems.Count > 0 ) );

        public ICommand ContextMoveToBackpackCommand =>
            _contextMoveToBackpackCommand ?? ( _contextMoveToBackpackCommand =
                new RelayCommandAsync( ContextMoveToBackpack, o => SelectedItems.Count > 0 ) );

        public ICommand ContextMoveToBankCommand =>
            _contextMoveToBankCommand ?? ( _contextMoveToBankCommand =
                new RelayCommandAsync( ContextMoveToBank, o => SelectedItems.Count > 0 ) );

        public ICommand ContextMoveToContainerCommand =>
            _contextMoveToContainerCommand ?? ( _contextMoveToContainerCommand =
                new RelayCommandAsync( ContextMoveToContainer, o => SelectedItems.Count > 0 ) );

        public ICommand ContextMoveToGroundCommand =>
            _contextMoveToGroundCommand ?? ( _contextMoveToGroundCommand =
                new RelayCommandAsync( ContextMoveToGround, o => SelectedItems.Count > 0 ) );

        public ICommand ContextOpenContainerCommand =>
            _contextOpenContainerCommand ?? ( _contextOpenContainerCommand = new RelayCommandAsync(
                ContextOpenContainer,
                o => SelectedItems.Any( e =>
                    e.Entity is Item item && item.Owner != 0 && !UOMath.IsMobile( item.Owner ) ) ) );

        public ICommand ContextTargetCommand =>
            _contextTargetCommand ?? ( _contextTargetCommand =
                new RelayCommandAsync( ContextTarget, o => SelectedItems.Count > 0 ) );

        public ICommand ContextTargetOwnerCommand =>
            _contextTargetOwnerCommand ?? ( _contextTargetOwnerCommand =
                new RelayCommand( ContextTargetOwner, o => SelectedItems.Count == 1 ) );

        public ICommand ContextToggleLockCommand =>
            _contextToggleLockCommand ?? ( _contextToggleLockCommand =
                new RelayCommand( ContextToggleLock, o => SelectedItems.Count > 0 ) );

        public ICommand ContextUseItemCommand =>
            _contextUseItemCommand ?? ( _contextUseItemCommand =
                new RelayCommandAsync( ContextUseItem, o => SelectedItems.Count > 0 ) );

        public ICommand CopyToClipboardCommand =>
            _copyToClipboardCommand ?? ( _copyToClipboardCommand = new RelayCommand( o => CopyToClipboard(), o => true ) );

        /// <summary>
        ///     Old-side's ad-hoc, non-registry context-menu extension point: an owning window can add entries
        ///     directly (<c>CustomContextActions.Add(...)</c>) rather than through a public registration API
        ///     like the toolbar's. Nothing constructs this Avalonia view model with any yet, so the "Custom
        ///     Actions" submenu is present but empty.
        /// </summary>
        public ObservableCollection<KeyValuePair<string, Action<Item>>> CustomContextActions { get; } =
            new ObservableCollection<KeyValuePair<string, Action<Item>>>();

        public ObservableCollection<EntityCollectionData> Entities
        {
            get => _entities;
            set => SetProperty( ref _entities, value );
        }

        public ICommand EquipItemCommand =>
            _equipItemCommand ?? ( _equipItemCommand = new RelayCommandAsync( EquipItem, o => SelectedItems.Count > 0 ) );

        public ICommand HideItemCommand =>
            _hideItemCommand ?? ( _hideItemCommand = new RelayCommand( HideItem, o => SelectedItems.Count > 0 ) );

        public ICommand ItemDoubleClickCommand =>
            _itemDoubleClickCommand ?? ( _itemDoubleClickCommand = new RelayCommand( ItemDoubleClick, o => true ) );

        public ICommand RefreshCommand =>
            _refreshCommand ?? ( _refreshCommand = new RelayCommand( o => Refresh(), o => true ) );

        public bool SelectedItemsAllLocked => SelectedItems.Count > 0 && SelectedItems.All( e => e.IsLocked );

        public ObservableCollection<EntityCollectionData> SelectedItems
        {
            get => _selectedItems;
            set => SetProperty( ref _selectedItems, value );
        }

        /// <summary>Include the contents of every container in the collection, recursively.</summary>
        public bool ShowChildItems
        {
            get => _showChildItems;
            set => SetProperty( ref _showChildItems, value );
        }

        /// <summary>Label each tile with its full property list rather than just its name.</summary>
        public bool ShowProperties
        {
            get => _showProperties;
            set => SetProperty( ref _showProperties, value );
        }

        public EntityCollectionSortStyle SortStyle
        {
            get => _sortStyle;
            set
            {
                SetProperty( ref _sortStyle, value );

                Rebuild();
            }
        }

        public string StatusLabel
        {
            get => _statusLabel;
            set => SetProperty( ref _statusLabel, value );
        }

        public ICommand ToggleChildItemsCommand =>
            _toggleChildItemsCommand ?? ( _toggleChildItemsCommand =
                new RelayCommand( o => ToggleChildItems(), o => true ) );

        public ICommand TogglePropertiesCommand =>
            _togglePropertiesCommand ?? ( _togglePropertiesCommand =
                new RelayCommand( o => ShowProperties = !ShowProperties, o => true ) );

        /// <summary>
        ///     Stops tracking the underlying collection. The viewer holds a handler on a collection that
        ///     outlives it, so without this a closed window keeps rebuilding itself for every item the server
        ///     sends.
        /// </summary>
        public void Cleanup()
        {
            Collection.CollectionChanged -= OnCollectionChanged;
            SelectedItems.CollectionChanged -= OnSelectedItemsChanged;
        }

        private static Dictionary<int, int> LoadMountIDEntries()
        {
            string fileName = Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "Data",
                "MountID.json" );

            if ( !File.Exists( fileName ) )
            {
                return new Dictionary<int, int>();
            }

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<int, int>>( File.ReadAllText( fileName ) ) ??
                       new Dictionary<int, int>();
            }
            catch ( Exception )
            {
                return new Dictionary<int, int>();
            }
        }

        private IComparer<Entity> GetSorter()
        {
            switch ( SortStyle )
            {
                case EntityCollectionSortStyle.Name:
                    return new NameThenSerialComparer();
                case EntityCollectionSortStyle.Serial:
                    return new SerialComparer();
                case EntityCollectionSortStyle.Hue:
                    return new HueThenAmountComparer();
                case EntityCollectionSortStyle.Quantity:
                    return new QuantityThenSerialComparer();
                default:
                    return new IDThenSerialComparer();
            }
        }

        private static void ItemDoubleClick( object obj )
        {
            if ( !( obj is EntityCollectionData data ) )
            {
                return;
            }

            // A container opens as another viewer; anything else has nothing to browse into, so show what
            // the client knows about it instead.
            if ( data.Entity is Item item && item.Container != null )
            {
                Engine.UIInvoker?.Invoke( "EntityCollectionViewer", null,
                    typeof( EntityCollectionViewerViewModel ), new object[] { item.Container } );

                return;
            }

            Engine.UIInvoker?.Invoke( "ObjectInspectorWindow", null, typeof( ObjectInspectorViewModel ),
                new object[] { data.Entity } );
        }

        private void OnCollectionChanged( int totalCount, bool added, Item[] entities )
        {
            Rebuild();
        }

        private void OnSelectedItemsChanged( object sender, NotifyCollectionChangedEventArgs e )
        {
            UpdateStatusLabel();

            NotifyPropertyChanged( nameof( SelectedItemsAllLocked ) );
        }

        private void Rebuild()
        {
            ItemCollection source = ShowChildItems
                ? new ItemCollection( Collection.Serial )
                {
                    ItemCollection.GetAllItems( Collection.GetItems() )
                }
                : Collection;

            Entities = new ObservableCollection<EntityCollectionData>(
                source.ToEntityCollectionData( GetSorter(), _nameOverrides ) );

            UpdateStatusLabel();
        }

        private void Refresh()
        {
            if ( _customRefresh != null )
            {
                Collection = _customRefresh.Invoke() ?? Collection;
            }

            Rebuild();
        }

        private void ToggleChildItems()
        {
            ShowChildItems = !ShowChildItems;

            Rebuild();
        }

        private void ContextCustomAction( object arg )
        {
            if ( !( arg is KeyValuePair<string, Action<Item>> action ) )
            {
                return;
            }

            foreach ( Item item in SelectedItems.Select( e => e.Entity ).OfType<Item>().ToList() )
            {
                action.Value?.Invoke( item );
            }
        }

        private async Task ContextDropToGround( object arg )
        {
            // Old-side probes the 8 tiles around the player for a free spot via MapInfo.ItemCanFit, which
            // this port doesn't have yet. Dropping at the player's own feet is a plain stand-in until it does.
            if ( Engine.Player == null )
            {
                return;
            }

            foreach ( Item item in SelectedItems.Where( i => !i.IsLocked ).Select( i => i.Entity ).OfType<Item>()
                         .ToList() )
            {
                await ActionPacketQueue.EnqueueDragDropGround( item.Serial, item.Count, Engine.Player.X,
                    Engine.Player.Y, Engine.Player.Z );
            }
        }

        private void ContextMenuRequest( object obj )
        {
            foreach ( EntityCollectionData ecd in SelectedItems.ToList() )
            {
                Engine.SendPacketToServer( new ContextMenuRequest( ecd.Entity.Serial ) );
            }
        }

        private async Task ContextMoveToBackpack( object arg )
        {
            if ( Engine.Player?.Backpack == null )
            {
                return;
            }

            await ContextMoveToContainer( Engine.Player.Backpack.Serial );
        }

        private async Task ContextMoveToBank( object arg )
        {
            Item bankBox = Engine.Player?.GetEquippedItems().FirstOrDefault( i => i.Layer == Layer.Bank );

            if ( bankBox != null )
            {
                await ContextMoveToContainer( bankBox.Serial );
            }
        }

        private async Task ContextMoveToContainer( object arg )
        {
            List<Item> items = SelectedItems.Where( i => !i.IsLocked ).Select( i => i.Entity ).OfType<Item>()
                .ToList();

            int serial = arg is int s ? s : 0;

            if ( serial == 0 )
            {
                serial = await Commands.GetTargetSerialAsync( Strings.Target_container___ );
            }

            if ( serial == 0 )
            {
                Commands.SystemMessage( Strings.Invalid_container___ );

                return;
            }

            foreach ( Item item in items )
            {
                await ActionPacketQueue.EnqueueDragDrop( item.Serial, item.Count, serial );
            }
        }

        private async Task ContextMoveToGround( object arg )
        {
            ( TargetType _, TargetFlags _, int _, int x, int y, int z, int _ ) =
                await Commands.GetTargetInfoAsync( Strings.Target_location___ );

            if ( x == -1 || y == -1 )
            {
                return;
            }

            foreach ( Item item in SelectedItems.Where( i => !i.IsLocked ).Select( i => i.Entity ).OfType<Item>()
                         .ToList() )
            {
                await ActionPacketQueue.EnqueueDragDropGround( item.Serial, item.Count, x, y, z );
            }
        }

        private Task ContextOpenContainer( object arg )
        {
            int[] containerSerials = SelectedItems
                .Where( e => e.Entity is Item item && item.Owner != 0 && !UOMath.IsMobile( item.Owner ) )
                .Select( e => ( (Item) e.Entity ).Owner ).ToArray();

            return ActionPacketQueue.EnqueueActionPackets(
                containerSerials.Select( s => (BasePacket) new UseObject( s ) ) );
        }

        private async Task ContextTarget( object obj )
        {
            foreach ( EntityCollectionData ecd in SelectedItems.ToList() )
            {
                if ( Engine.TargetExists || await Task.Run( () => Commands.WaitForTarget( 5000 ) ) )
                {
                    TargetCommands.Target( ecd.Entity.Serial );
                }
            }
        }

        private void ContextTargetOwner( object obj )
        {
            if ( !( SelectedItems.FirstOrDefault()?.Entity is Item item ) )
            {
                return;
            }

            TargetCommands.Target( item.Owner );
        }

        private void ContextToggleLock( object obj )
        {
            bool lockTarget = !SelectedItemsAllLocked;

            foreach ( EntityCollectionData ecd in SelectedItems.ToList() )
            {
                ecd.IsLocked = lockTarget;
            }

            NotifyPropertyChanged( nameof( SelectedItemsAllLocked ) );
        }

        private Task ContextUseItem( object arg )
        {
            return ActionPacketQueue.EnqueueActionPackets(
                SelectedItems.Select( i => (BasePacket) new UseObject( i.Entity.Serial ) ) );
        }

        private void CopyToClipboard()
        {
            IEnumerable<EntityCollectionData> items = SelectedItems.Any() ? SelectedItems : (IEnumerable<EntityCollectionData>) Entities;

            StringBuilder stringBuilder = new StringBuilder();

            foreach ( EntityCollectionData item in items )
            {
                stringBuilder.AppendLine( $"Serial: 0x{item.Entity.Serial:x8}" );
                stringBuilder.AppendLine( "Properties:" );
                stringBuilder.Append( item.FullName );

                Layer layer = GetLayer( item.Entity.ID );

                if ( layer != Layer.Invalid )
                {
                    stringBuilder.AppendLine();
                    stringBuilder.AppendLine( $"Layer: {layer}" );
                }

                stringBuilder.AppendLine();
                stringBuilder.AppendLine();
            }

            Engine.UIInvoker?.SetClipboardText( stringBuilder.ToString() );
        }

        private async Task EquipItem( object obj )
        {
            foreach ( Item item in SelectedItems.Select( i => i.Entity ).OfType<Item>().ToList() )
            {
                if ( GetLayer( item.ID ) == Layer.Invalid )
                {
                    continue;
                }

                await Commands.EquipItem( item, Layer.Invalid );
            }
        }

        /// <summary>
        ///     Mirrors the WPF-side <c>TileData.GetLayer</c>, which this port doesn't have - the Avalonia
        ///     <see cref="StaticTile" /> exposes the same tiledata.mul byte as <see cref="StaticTile.Quality" />
        ///     rather than a dedicated <c>Layer</c> field.
        /// </summary>
        private static Layer GetLayer( int id )
        {
            StaticTile tileData = TileData.GetStaticTile( id );

            return tileData.Flags.HasFlag( TileFlags.Wearable ) ? (Layer) tileData.Quality : Layer.Invalid;
        }

        private void HideItem( object obj )
        {
            foreach ( Entity entity in SelectedItems.Select( e => e.Entity ).ToList() )
            {
                Commands.RemoveObject( entity.Serial );
            }
        }

        private void UpdateStatusLabel()
        {
            StatusLabel = string.Format( Strings._0__items___1__selected___2__total_amount, Entities.Count,
                SelectedItems?.Count ?? 0,
                SelectedItems?.Select( i => i.Entity ).OfType<Item>().Sum( i => i.Count ) ?? 0 );
        }
    }

    public static class EntityCollectionDataExtensions
    {
        public static List<EntityCollectionData> ToEntityCollectionData( this ItemCollection itemCollection,
            IComparer<Entity> comparer, Dictionary<int, string> nameOverrides )
        {
            if ( itemCollection == null )
            {
                return new List<EntityCollectionData>();
            }

            return itemCollection.GetItems().OrderBy( i => i, comparer )
                .Select( item => item.ToEntityCollectionData( nameOverrides ) ).ToList();
        }

        public static EntityCollectionData ToEntityCollectionData( this Item item,
            Dictionary<int, string> nameOverrides )
        {
            if ( string.IsNullOrEmpty( item.Name ) )
            {
                StaticTile tileData = TileData.GetStaticTile( item.ID );

                item.Name = nameOverrides.TryGetValue( item.Serial, out string fallback ) ? fallback :
                    tileData.ID != 0 ? tileData.Name : $"0x{item.Serial:x8}";
            }

            if ( nameOverrides.TryGetValue( item.Serial, out string nameOverride ) )
            {
                item.Name = nameOverride;
            }

            return new EntityCollectionData { Entity = item };
        }
    }
}
