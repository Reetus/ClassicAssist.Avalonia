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
using System.Windows.Input;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.Models;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json;

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

        public ObservableCollection<EntityCollectionData> Entities
        {
            get => _entities;
            set => SetProperty( ref _entities, value );
        }

        public ICommand ItemDoubleClickCommand =>
            _itemDoubleClickCommand ?? ( _itemDoubleClickCommand = new RelayCommand( ItemDoubleClick, o => true ) );

        public ICommand RefreshCommand =>
            _refreshCommand ?? ( _refreshCommand = new RelayCommand( o => Refresh(), o => true ) );

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
