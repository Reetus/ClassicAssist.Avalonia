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
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Misc;
using ClassicAssist.Data.Organizer;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.Shared.UI.ViewModels.Autoloot;
using ClassicAssist.UI.Models;
using ClassicAssist.UO;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sentry;
using Commands = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.UI.ViewModels
{
    /// <summary>
    ///     Backs the entity collection viewer: the grid of item art behind "Show World Items" and behind
    ///     opening a container.
    ///     <para>
    ///         Ported from the WPF view model with one deliberate simplification and one deferral: the
    ///         filter is flat/AND-only rather than old's nested boolean-tree groups (see
    ///         <see cref="FilterProfile" />), and the Organizer panel is not ported at all - see
    ///         ECV_TODO.md for the full gap accounting. Browsing, sorting, refreshing, drilling into
    ///         containers, the filter editor + profiles, the queued move/loot actions, and the settings
    ///         window (<see cref="Options" />) are all here.
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

        private readonly string _propertiesFile =
            Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "Data", "Properties.json" );

        private readonly string _propertiesFileCustom =
            Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "Data", "Properties.Custom.json" );

        /// <summary>
        ///     The constraint entries this viewer loaded from <c>Properties.Custom.json</c>, tracked so
        ///     <see cref="ReloadCustomProperties" /> can drop and re-read just those (the rest of
        ///     <see cref="Constraints" /> comes from the bundled file plus the filter-only entries).
        /// </summary>
        private readonly List<PropertyEntry> _customPropertyEntries = new List<PropertyEntry>();

        private readonly string _filterProfilesFile =
            Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "FilterProfiles.json" );

        private readonly string _optionsFile =
            Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "EntityCollectionViewerOptions.json" );

        /// <summary>
        ///     Feeds the queue rows into <see cref="ThreadPriorityQueue{T}" />, one worker thread serializing
        ///     everything at <see cref="QueuePriority.Low" /> - mirrors WPF's EnqueueAction/ThreadQueue pair.
        /// </summary>
        private readonly ThreadPriorityQueue<QueueAction> _threadQueue;

        private ItemCollection _collection = new ItemCollection( 0 );
        private ObservableCollection<EntityCollectionData> _entities =
            new ObservableCollection<EntityCollectionData>();
        private ICommand _addFilterConditionCommand;
        private ICommand _addProfileCommand;
        private ICommand _applyFiltersCommand;
        private ICommand _changeSortStyleCommand;
        private ICommand _resetFiltersCommand;
        private ICommand _combineStacksCommand;
        private ICommand _configureCommand;
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
        private ICommand _hotkeyActionCommand;
        private bool _isFilterApplied;
        private ICommand _itemDoubleClickCommand;
        private ICommand _openAllContainersCommand;
        private ICommand _refreshCommand;
        private ICommand _removeFilterConditionCommand;
        private ICommand _removeProfileCommand;

        private ObservableCollection<EntityCollectionData> _selectedItems =
            new ObservableCollection<EntityCollectionData>();

        private EntityCollectionViewerOptions _options;
        private FilterProfile _selectedProfile;
        private bool _showFilter;
        private bool _showProperties;
        private string _statusLabel;
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

            _threadQueue = new ThreadPriorityQueue<QueueAction>( ProcessQueue );
            QueueActions.CollectionChanged += QueueActions_CollectionChanged;

            Options = LoadOptions();
            Options.PropertyChanged += OnOptionsChanged;

            LoadProperties();
            LoadCustomProperties();
            AutolootPropertyRegistration.LoadSpecialProperties( Constraints );
            RegisterFilterOnlyConstraints();
            LoadFilterProfiles();

            CustomPropertiesViewModel.Saved += OnCustomPropertiesSaved;

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

        public ICommand AddFilterConditionCommand =>
            _addFilterConditionCommand ?? ( _addFilterConditionCommand =
                new RelayCommand( AddFilterCondition, o => Constraints.Count > 0 ) );

        public ICommand AddProfileCommand =>
            _addProfileCommand ?? ( _addProfileCommand = new RelayCommand( AddProfile, o => true ) );

        public ICommand ApplyFiltersCommand =>
            _applyFiltersCommand ?? ( _applyFiltersCommand = new RelayCommand( o =>
            {
                IsFilterApplied = FilterConditions.Count > 0;
                Rebuild();
            }, o => true ) );

        public ICommand ChangeSortStyleCommand =>
            _changeSortStyleCommand ?? ( _changeSortStyleCommand = new RelayCommand( ChangeSortStyle, o => true ) );

        public ICommand ResetFiltersCommand =>
            _resetFiltersCommand ?? ( _resetFiltersCommand = new RelayCommand( o =>
            {
                IsFilterApplied = false;
                Rebuild();
            }, o => true ) );

        public ItemCollection Collection
        {
            get => _collection;
            set => SetProperty( ref _collection, value );
        }

        public ICommand CombineStacksCommand =>
            _combineStacksCommand ?? ( _combineStacksCommand = new RelayCommand( o => CombineStacks(), o => true ) );

        public ICommand ConfigureCommand =>
            _configureCommand ?? ( _configureCommand = new RelayCommandAsync( Configure, o => true ) );

        /// <summary>
        ///     The set of properties a filter condition can be built on - the same list Autoloot constraints
        ///     draw from (<c>Data/Properties.json</c> plus any <c>Properties.Custom.json</c> overrides), plus
        ///     the ECV-only ones from <see cref="RegisterFilterOnlyConstraints" />.
        /// </summary>
        public ObservableCollection<PropertyEntry> Constraints { get; } = new ObservableCollection<PropertyEntry>();

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

        /// <summary>
        ///     The active filter conditions, AND-combined - mirrored into <see cref="SelectedProfile" /> on
        ///     save.
        /// </summary>
        public ObservableCollection<AutolootConstraintEntry> FilterConditions { get; } =
            new ObservableCollection<AutolootConstraintEntry>();

        public ICommand HideItemCommand =>
            _hideItemCommand ?? ( _hideItemCommand = new RelayCommand( HideItem, o => SelectedItems.Count > 0 ) );

        /// <summary>
        ///     A single indirection point for the B/C/K/G/D key bindings, gated on
        ///     <see cref="EntityCollectionViewerOptions.EnableHotkeys" /> - distinct from the same-shaped
        ///     context-menu commands, which stay usable even with hotkeys disabled. Mirrors old's
        ///     <c>HotkeyActionCommand</c>.
        /// </summary>
        public ICommand HotkeyActionCommand =>
            _hotkeyActionCommand ?? ( _hotkeyActionCommand =
                new RelayCommandAsync( HotkeyAction, o => Options.EnableHotkeys && SelectedItems.Count > 0 ) );

        public bool IsFilterApplied
        {
            get => _isFilterApplied;
            set => SetProperty( ref _isFilterApplied, value );
        }

        public ICommand ItemDoubleClickCommand =>
            _itemDoubleClickCommand ?? ( _itemDoubleClickCommand = new RelayCommand( ItemDoubleClick, o => true ) );

        public ICommand OpenAllContainersCommand =>
            _openAllContainersCommand ?? ( _openAllContainersCommand = new RelayCommand( o => OpenAllContainers(), o => true ) );

        /// <summary>
        ///     Persisted to <c>EntityCollectionViewerOptions.json</c> in the same shape base ClassicAssist
        ///     uses - see <see cref="LoadOptions" />/<see cref="SaveOptions" />. Bound to directly from XAML
        ///     (<c>Options.AlwaysOnTop</c> etc.) rather than mirrored through VM-level properties.
        /// </summary>
        public EntityCollectionViewerOptions Options
        {
            get => _options;
            set => SetProperty( ref _options, value );
        }

        /// <summary>Saved, named filter condition sets - see <see cref="LoadFilterProfiles" />/<see cref="SaveFilterProfiles" />.</summary>
        public ObservableCollection<FilterProfile> Profiles { get; } = new ObservableCollection<FilterProfile>();

        /// <summary>Long-running move/loot/target actions, run one at a time with a cancel button each.</summary>
        public ObservableCollection<QueueAction> QueueActions { get; } = new ObservableCollection<QueueAction>();

        public ICommand RefreshCommand =>
            _refreshCommand ?? ( _refreshCommand = new RelayCommand( o => Refresh(), o => true ) );

        public ICommand RemoveFilterConditionCommand =>
            _removeFilterConditionCommand ?? ( _removeFilterConditionCommand =
                new RelayCommand( RemoveFilterCondition, o => o is AutolootConstraintEntry ) );

        public ICommand RemoveProfileCommand =>
            _removeProfileCommand ?? ( _removeProfileCommand = new RelayCommand( RemoveProfile, o => true ) );

        public bool SelectedItemsAllLocked => SelectedItems.Count > 0 && SelectedItems.All( e => e.IsLocked );

        public ObservableCollection<EntityCollectionData> SelectedItems
        {
            get => _selectedItems;
            set => SetProperty( ref _selectedItems, value );
        }

        /// <summary>
        ///     The profile currently being edited. Switching profiles swaps <see cref="FilterConditions" />'
        ///     contents to match and re-applies the filter if one is currently active.
        /// </summary>
        public FilterProfile SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                SetProperty( ref _selectedProfile, value );

                FilterConditions.Clear();

                if ( value == null )
                {
                    return;
                }

                foreach ( AutolootConstraintEntry condition in value.Conditions )
                {
                    FilterConditions.Add( condition );
                }

                if ( IsFilterApplied )
                {
                    Rebuild();
                }
            }
        }

        /// <summary>Whether the filter condition panel is visible - does not by itself apply the filter.</summary>
        public bool ShowFilter
        {
            get => _showFilter;
            set => SetProperty( ref _showFilter, value );
        }

        /// <summary>Label each tile with its full property list rather than just its name.</summary>
        public bool ShowProperties
        {
            get => _showProperties;
            set => SetProperty( ref _showProperties, value );
        }

        public string StatusLabel
        {
            get => _statusLabel;
            set => SetProperty( ref _statusLabel, value );
        }

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
            CustomPropertiesViewModel.Saved -= OnCustomPropertiesSaved;
            Collection.CollectionChanged -= OnCollectionChanged;
            SelectedItems.CollectionChanged -= OnSelectedItemsChanged;
            QueueActions.CollectionChanged -= QueueActions_CollectionChanged;
            _threadQueue?.Dispose();
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
            switch ( Options.SortStyle )
            {
                case EntityCollectionSortStyle.None:
                    return null;
                case EntityCollectionSortStyle.Name:
                    return new NameThenSerialComparer();
                case EntityCollectionSortStyle.Serial:
                    return new SerialComparer();
                case EntityCollectionSortStyle.Hue:
                    return new HueThenAmountComparer();
                case EntityCollectionSortStyle.Quantity:
                    return new QuantityThenSerialComparer();
                case EntityCollectionSortStyle.Weight:
                    return new WeightThenSerialComparer();
                default:
                    return new IDThenSerialComparer();
            }
        }

        private void ChangeSortStyle( object obj )
        {
            if ( !( obj is EntityCollectionSortStyle val ) )
            {
                return;
            }

            // Clicking the active sort style again clears it back to insertion order.
            Options.SortStyle = Options.SortStyle == val ? EntityCollectionSortStyle.None : val;
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
            ItemCollection source = Options.ShowChildItems
                ? new ItemCollection( Collection.Serial )
                {
                    ItemCollection.GetAllItems( Collection.GetItems() )
                }
                : Collection;

            source = ApplyFilter( source );

            IEnumerable<EntityCollectionData> entities = source.ToEntityCollectionData( GetSorter(), _nameOverrides );

            foreach ( EntityCollectionData ecd in entities )
            {
                ecd.IsLocked = Options.LockedItems.Contains( ecd.Entity.Serial );
            }

            if ( Options.HideLockedItems )
            {
                entities = entities.Where( ecd => !ecd.IsLocked );
            }

            Entities = new ObservableCollection<EntityCollectionData>( entities );

            UpdateStatusLabel();
        }

        private ItemCollection ApplyFilter( ItemCollection source )
        {
            if ( !IsFilterApplied || FilterConditions.Count == 0 )
            {
                return source;
            }

            List<Predicate<Item>> predicates = AutolootHelpers.ConstraintsToPredicates( FilterConditions ).ToList();

            if ( predicates.Count == 0 )
            {
                return source;
            }

            ItemCollection filtered = new ItemCollection( source.Serial );

            foreach ( Item item in source.GetItems() )
            {
                if ( predicates.All( p => p( item ) ) )
                {
                    filtered.Add( item );
                }
            }

            return filtered;
        }

        private void AddFilterCondition( object obj )
        {
            FilterConditions.Add( new AutolootConstraintEntry { Property = Constraints.FirstOrDefault() } );
        }

        private void RemoveFilterCondition( object obj )
        {
            if ( obj is AutolootConstraintEntry entry )
            {
                FilterConditions.Remove( entry );
            }
        }

        private void LoadCustomProperties()
        {
            if ( !File.Exists( _propertiesFileCustom ) )
            {
                return;
            }

            JsonSerializer serializer = new JsonSerializer();

            using ( StreamReader sr = new StreamReader( _propertiesFileCustom ) )
            using ( JsonTextReader reader = new JsonTextReader( sr ) )
            {
                PropertyEntry[] constraints = serializer.Deserialize<PropertyEntry[]>( reader );

                foreach ( PropertyEntry constraint in constraints ?? Array.Empty<PropertyEntry>() )
                {
                    _customPropertyEntries.Add( constraint );
                    Constraints.AddSorted( constraint );
                }
            }
        }

        /// <summary>
        ///     Drops the entries loaded from <c>Properties.Custom.json</c> and re-reads the file, so a
        ///     window left open across a save in the Custom Properties editor picks up added/removed
        ///     constraints instead of keeping the stale list it was constructed with.
        /// </summary>
        public void ReloadCustomProperties()
        {
            foreach ( PropertyEntry entry in _customPropertyEntries )
            {
                Constraints.Remove( entry );
            }

            _customPropertyEntries.Clear();

            LoadCustomProperties();
        }

        private void OnCustomPropertiesSaved( object sender, EventArgs e )
        {
            IDispatcher dispatcher = _dispatcher ?? Engine.Dispatcher;

            if ( dispatcher != null && !dispatcher.CheckAccess() )
            {
                dispatcher.Invoke( ReloadCustomProperties );
            }
            else
            {
                ReloadCustomProperties();
            }
        }

        private void LoadProperties()
        {
            if ( !File.Exists( _propertiesFile ) )
            {
                return;
            }

            JsonSerializer serializer = new JsonSerializer();

            using ( StreamReader sr = new StreamReader( _propertiesFile ) )
            using ( JsonTextReader reader = new JsonTextReader( sr ) )
            {
                PropertyEntry[] constraints = serializer.Deserialize<PropertyEntry[]>( reader );

                foreach ( PropertyEntry constraint in constraints ?? Array.Empty<PropertyEntry>() )
                {
                    Constraints.AddSorted( constraint );
                }
            }
        }

        /// <summary>
        ///     Constraints that only make sense for filtering an already-visible collection (rather than
        ///     Autoloot, which evaluates items as they're seen), so they're registered here rather than in
        ///     the shared Properties.json list. Ported from old's <c>EntityCollectionFilterViewModel</c>
        ///     constructor.
        /// </summary>
        private void RegisterFilterOnlyConstraints()
        {
            Constraints.AddSorted( new PropertyEntry
            {
                Name = Strings.Name,
                ConstraintType = PropertyType.PredicateWithValue,
                Predicate = ( item, entry ) =>
                {
                    string propString = item.Properties == null
                        ? item.Name
                        : item.Properties.Aggregate( string.Empty, ( current, property ) => current + property.Text );

                    if ( propString == null )
                    {
                        return false;
                    }

                    bool contains = propString.IndexOf( entry.Additional ?? string.Empty,
                        StringComparison.CurrentCultureIgnoreCase ) >= 0;

                    return entry.Operator == AutolootOperator.Equal ? contains : !contains;
                }
            } );

            Constraints.AddSorted( new PropertyEntry
            {
                Name = nameof( TileFlags ),
                ConstraintType = PropertyType.Predicate,
                Predicate = ( item, entry ) =>
                {
                    TileFlags flags = TileData.GetStaticTile( item.ID ).Flags;

                    switch ( entry.Operator )
                    {
                        case AutolootOperator.NotEqual:
                        case AutolootOperator.NotPresent:
                            return !flags.HasFlag( (TileFlags) entry.Value );
                        case AutolootOperator.Equal:
                            return flags.HasFlag( (TileFlags) entry.Value );
                        default:
                            return false;
                    }
                }
            } );

            Constraints.AddSorted( new PropertyEntry
            {
                Name = "Distance",
                ConstraintType = PropertyType.Predicate,
                Predicate = ( item, entry ) =>
                {
                    int distance = item.Distance;

                    switch ( entry.Operator )
                    {
                        case AutolootOperator.LessThan:
                            return distance < entry.Value;
                        case AutolootOperator.GreaterThan:
                            return distance > entry.Value;
                        case AutolootOperator.Equal:
                            return distance == entry.Value;
                        case AutolootOperator.NotEqual:
                            return distance != entry.Value;
                        default:
                            return false;
                    }
                }
            } );

            Constraints.AddSorted( new PropertyEntry
            {
                Name = Strings.Organizer_Match,
                ConstraintType = PropertyType.PredicateWithValue,
                Predicate = ( item, entry ) =>
                {
                    if ( entry.Additional == null )
                    {
                        return false;
                    }

                    OrganizerEntry organizer =
                        OrganizerManager.GetInstance().Items.FirstOrDefault( e => e.Name == entry.Additional );

                    if ( organizer == null )
                    {
                        return false;
                    }

                    bool match = organizer.Items.Any( e => e.ID == item.ID && ( e.Hue == -1 || e.Hue == item.Hue ) );

                    return entry.Operator == AutolootOperator.NotEqual ? !match : match;
                }
            } );

            Constraints.AddSorted( new PropertyEntry
            {
                Name = "Is Multi",
                ConstraintType = PropertyType.Predicate,
                Predicate = ( item, entry ) =>
                {
                    switch ( entry.Operator )
                    {
                        case AutolootOperator.Equal:
                            return item is Item i && i.ArtDataID == 2;
                        case AutolootOperator.NotEqual:
                            return item is Item i2 && i2.ArtDataID != 2;
                        default:
                            return false;
                    }
                }
            } );
        }

        public EntityCollectionViewerOptions LoadOptions()
        {
            if ( !File.Exists( _optionsFile ) )
            {
                return new EntityCollectionViewerOptions();
            }

            try
            {
                return EntityCollectionViewerOptions.Deserialize( JObject.Parse( File.ReadAllText( _optionsFile ) ) );
            }
            catch ( Exception ex )
            {
                SentrySdk.CaptureException( ex );

                return new EntityCollectionViewerOptions();
            }
        }

        public void SaveOptions()
        {
            try
            {
                JToken jObject = EntityCollectionViewerOptions.Serialize( Options );

                string hash = jObject.ToString().SHA1();

                if ( Options.Hash == hash )
                {
                    return;
                }

                Options.Hash = hash;

                File.WriteAllText( _optionsFile, jObject.ToString() );
            }
            catch ( Exception ex )
            {
                SentrySdk.CaptureException( ex );
            }
        }

        /// <summary>
        ///     Rebuilds the list when a display-affecting option changes, and always saves - collection
        ///     mutations inside <c>Options.LockedItems</c>/<c>CombineStacksIgnore</c>/etc. don't raise this
        ///     (only reassigning the property itself would), so those save explicitly at their own call
        ///     sites instead (<see cref="ContextToggleLock" />, the Settings window).
        /// </summary>
        private void OnOptionsChanged( object sender, PropertyChangedEventArgs e )
        {
            switch ( e.PropertyName )
            {
                case nameof( EntityCollectionViewerOptions.ShowChildItems ):
                case nameof( EntityCollectionViewerOptions.SortStyle ):
                case nameof( EntityCollectionViewerOptions.HideLockedItems ):
                    Rebuild();

                    break;
            }

            SaveOptions();
        }

        public void LoadFilterProfiles()
        {
            try
            {
                if ( !File.Exists( _filterProfilesFile ) )
                {
                    AddDefaultProfile();

                    return;
                }

                JObject obj = (JObject) JsonConvert.DeserializeObject( File.ReadAllText( _filterProfilesFile ) );

                Guid? lastProfileId = obj?["LastProfileID"]?.ToObject<Guid>();

                foreach ( JToken profileToken in obj?["Profiles"] ?? Enumerable.Empty<JToken>() )
                {
                    FilterProfile profile = new FilterProfile
                    {
                        ID = profileToken["ID"]?.ToObject<Guid>() ?? Guid.NewGuid(),
                        Name = profileToken["Name"]?.ToObject<string>() ?? "New Filter Profile"
                    };

                    foreach ( JToken conditionToken in GetConditionTokens( profileToken ) )
                    {
                        // "Property" is this port's own flat shape; "Constraint"."Name" is old WPF's
                        // nested Groups[].Items[].Constraint shape - see GetConditionTokens.
                        string propertyName = conditionToken["Property"]?.ToObject<string>() ??
                                               conditionToken["Constraint"]?["Name"]?.ToObject<string>();

                        PropertyEntry property = Constraints.FirstOrDefault( c => c.Name == propertyName ) ??
                                                  Constraints.FirstOrDefault();

                        if ( property == null )
                        {
                            continue;
                        }

                        profile.Conditions.Add( new AutolootConstraintEntry
                        {
                            Property = property,
                            Operator = conditionToken["Operator"]?.ToObject<AutolootOperator>() ??
                                       AutolootOperator.Equal,
                            Value = conditionToken["Value"]?.ToObject<int>() ?? 0,
                            Additional = conditionToken["Additional"]?.ToObject<string>()
                        } );
                    }

                    Profiles.Add( profile );
                }

                if ( Profiles.Count > 0 )
                {
                    SelectedProfile = Profiles.FirstOrDefault( p => p.ID == lastProfileId ) ?? Profiles[0];
                }
                else
                {
                    AddDefaultProfile();
                }
            }
            catch ( Exception ex )
            {
                SentrySdk.CaptureException( ex );

                if ( Profiles.Count == 0 )
                {
                    AddDefaultProfile();
                }
            }
        }

        /// <summary>
        ///     Reads a profile's condition tokens from either shape: this port's own flat "Conditions"
        ///     array, or old WPF's "Groups[].Items[]" - so a <c>FilterProfiles.json</c> written by WPF
        ///     loads here too, as long as it doesn't use the boolean-tree nesting this port doesn't have.
        ///     Only the top-level groups' items are read; nested "Children" sub-groups are silently
        ///     skipped rather than flattened, since there's no way to represent their Or/Not semantics in
        ///     a flat AND-only list without changing what the filter actually matches.
        /// </summary>
        private static IEnumerable<JToken> GetConditionTokens( JToken profileToken )
        {
            if ( profileToken["Conditions"] is JArray conditions )
            {
                foreach ( JToken condition in conditions )
                {
                    yield return condition;
                }

                yield break;
            }

            foreach ( JToken groupToken in profileToken["Groups"] ?? Enumerable.Empty<JToken>() )
            {
                foreach ( JToken itemToken in groupToken["Items"] ?? Enumerable.Empty<JToken>() )
                {
                    yield return itemToken;
                }
            }
        }

        private void AddDefaultProfile()
        {
            FilterProfile profile = new FilterProfile { Name = "Default" };

            Profiles.Add( profile );
            SelectedProfile = profile;
        }

        public void SaveFilterProfiles()
        {
            if ( SelectedProfile != null )
            {
                SelectedProfile.Conditions = new ObservableCollection<AutolootConstraintEntry>( FilterConditions );
            }

            try
            {
                JObject obj = new JObject { { "LastProfileID", SelectedProfile?.ID } };

                JArray profiles = new JArray();

                foreach ( FilterProfile profile in Profiles )
                {
                    JObject profileObj = new JObject { { "ID", profile.ID }, { "Name", profile.Name } };

                    JArray conditions = new JArray();

                    foreach ( AutolootConstraintEntry condition in profile.Conditions )
                    {
                        conditions.Add( new JObject
                        {
                            { "Property", condition.Property?.Name },
                            { "Operator", (int) condition.Operator },
                            { "Value", condition.Value },
                            { "Additional", condition.Additional }
                        } );
                    }

                    profileObj.Add( "Conditions", conditions );

                    profiles.Add( profileObj );
                }

                obj.Add( "Profiles", profiles );

                File.WriteAllText( _filterProfilesFile, JsonConvert.SerializeObject( obj, Formatting.Indented ) );
            }
            catch ( Exception ex )
            {
                SentrySdk.CaptureException( ex );
            }
        }

        private void AddProfile( object obj )
        {
            FilterProfile profile = new FilterProfile { Name = "New Filter Profile" };

            Profiles.Add( profile );
            SelectedProfile = profile;

            SaveFilterProfiles();
        }

        private void RemoveProfile( object obj )
        {
            FilterProfile profile = obj as FilterProfile ?? SelectedProfile;

            if ( profile == null )
            {
                return;
            }

            Profiles.Remove( profile );

            SelectedProfile = Profiles.FirstOrDefault();

            if ( SelectedProfile == null )
            {
                AddDefaultProfile();
            }

            SaveFilterProfiles();
        }

        private bool CombineStacksExcluded( Item item )
        {
            return Options.CombineStacksIgnore.Any( e =>
                ( e.ID == -1 || e.ID == item.ID ) && ( e.Hue == -1 || e.Hue == item.Hue ) &&
                ( e.Cliloc == -1 || item.Properties == null || item.Properties.Length == 0 ||
                  e.Cliloc == item.Properties[0].Cliloc ) );
        }

        private void CombineStacks()
        {
            EnqueueAction( async queueAction =>
            {
                try
                {
                    List<int> ignoreList = new List<int>();

                    while ( true )
                    {
                        if ( queueAction.CancellationTokenSource.IsCancellationRequested )
                        {
                            return false;
                        }

                        Item destStack = Collection.SelectEntity( i =>
                            i.Count < 60000 && TileData.GetStaticTile( i.ID ).Flags.HasFlag( TileFlags.Stackable ) &&
                            !ignoreList.Contains( i.Serial ) && !CombineStacksExcluded( i ) );

                        if ( destStack == null )
                        {
                            return true;
                        }

                        int needed = 60000 - destStack.Count;

                        Item sourceStack = Collection.SelectEntities( i =>
                                i.ID == destStack.ID && i.Hue == destStack.Hue && i.Serial != destStack.Serial &&
                                i.Count != 60000 &&
                                ( !Engine.TooltipsEnabled || StackNamesMatch( i, destStack ) ) )
                            ?.OrderBy( i => i.Count ).FirstOrDefault();

                        if ( sourceStack == null )
                        {
                            ignoreList.Add( destStack.Serial );

                            continue;
                        }

                        _dispatcher.Invoke( () => queueAction.Status = $"{sourceStack.Name} => {destStack.Name}" );

                        await ActionPacketQueue.EnqueueDragDrop( sourceStack.Serial,
                            needed > sourceStack.Count ? sourceStack.Count : needed, destStack.Serial,
                            checkExisting: true, delaySend: false );

                        await Task.Delay( TimeSpan.FromMilliseconds( ClassicAssist.Data.Options.CurrentOptions.ActionDelayMS ),
                            queueAction.CancellationTokenSource.Token );

                        Refresh();
                    }
                }
                catch ( TaskCanceledException )
                {
                    return false;
                }
            }, Strings.Combine_stacks );
        }

        private static string GetNameMinusAmount( Item item )
        {
            if ( item.Properties == null || item.Properties.Length == 0 )
            {
                return item.Name.Trim();
            }

            Property property = item.Properties.First();

            if ( property.Arguments == null || property.Arguments.Length == 0 )
            {
                return item.Name.Trim();
            }

            List<string> newArguments = ( from argument in property.Arguments
                select argument.Equals( item.Count.ToString() ) ? string.Empty : argument ).ToList();

            return newArguments.Count == 0
                ? item.Name.Trim()
                : Cliloc.GetLocalString( property.Cliloc, newArguments.ToArray() ).Trim();
        }

        private static bool StackNamesMatch( Item item, Item destStack )
        {
            string sourceStackName = GetNameMinusAmount( item );
            string destStackName = GetNameMinusAmount( destStack );

            if ( item.Count > 1 && sourceStackName.EndsWith( "s" ) && destStack.Count == 1 &&
                 !destStackName.EndsWith( "s" ) )
            {
                sourceStackName = sourceStackName.TrimEnd( 's' );
            }

            if ( destStack.Count > 1 && destStackName.EndsWith( "s" ) && item.Count == 1 &&
                 !sourceStackName.EndsWith( "s" ) )
            {
                destStackName = destStackName.TrimEnd( 's' );
            }

            return sourceStackName.Equals( destStackName );
        }

        private void OpenAllContainers()
        {
            Dictionary<int, int> containerGumpIds = null;

            if ( Options.OpenContainersOnlyKnownContainers )
            {
                string fileName = Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "Data",
                    "ContainerGumpIDs.json" );

                if ( File.Exists( fileName ) )
                {
                    containerGumpIds = JsonConvert.DeserializeObject<Dictionary<int, int>>( File.ReadAllText( fileName ) );
                }
            }

            bool Excluded( Item item )
            {
                return Options.OpenContainersIgnore.Any( e =>
                    ( e.ID == -1 || e.ID == item.ID ) &&
                    ( e.Cliloc == -1 || item.Properties == null || item.Properties.Any( p => p.Cliloc == e.Cliloc ) ) &&
                    ( e.Hue == -1 || item.Hue == e.Hue ) ) ||
                       Options.OpenContainersOnlyKnownContainers && containerGumpIds != null &&
                       !containerGumpIds.ContainsKey( item.ID );
            }

            Item[] containers = Collection.GetItems()
                .Where( i => TileData.GetStaticTile( i.ID ).Flags.HasFlag( TileFlags.Container ) && !Excluded( i ) )
                .ToArray();

            EnqueueAction( async queueAction =>
            {
                foreach ( var item in containers.Select( ( value, i ) => new { i, value } ) )
                {
                    if ( queueAction.CancellationTokenSource.IsCancellationRequested )
                    {
                        _dispatcher.Invoke( () => queueAction.Status = Strings.Cancel );

                        return false;
                    }

                    _dispatcher.Invoke( () => queueAction.Status =
                        string.Format( Strings.Opening_container__0_____1____, item.i, containers.Length ) );

                    Engine.SendPacketToServer( new UseObject( item.value.Serial ) );

                    try
                    {
                        await Task.Delay( TimeSpan.FromMilliseconds( ClassicAssist.Data.Options.CurrentOptions.ActionDelayMS ),
                            queueAction.CancellationTokenSource.Token );
                    }
                    catch ( TaskCanceledException )
                    {
                        return false;
                    }
                }

                Refresh();

                return true;
            }, Strings.Open_All_Containers );
        }

        private async Task Configure( object obj )
        {
            EntityCollectionViewerSettingsViewModel settingsViewModel =
                new EntityCollectionViewerSettingsViewModel { Options = Options };

            if ( Engine.UIInvoker != null )
            {
                await Engine.UIInvoker.InvokeDialog( "EntityCollectionViewerSettingsWindow", null,
                    settingsViewModel );
            }

            // The settings window mutates Options.CombineStacksIgnore/OpenContainersIgnore/ContainerSets
            // in place - none of those are property changes on Options itself, so nothing auto-saved them.
            SaveOptions();
        }

        private async Task HotkeyAction( object arg )
        {
            if ( !Options.EnableHotkeys || !( arg is string action ) )
            {
                return;
            }

            switch ( action )
            {
                case "Container":
                    await ContextMoveToContainer( null );

                    break;
                case "Backpack":
                    await ContextMoveToBackpack( null );

                    break;
                case "Bank":
                    await ContextMoveToBank( null );

                    break;
                case "Ground":
                    await ContextMoveToGround( null );

                    break;
                case "Drop":
                    await ContextDropToGround( null );

                    break;
            }
        }

        private void QueueActions_CollectionChanged( object sender, NotifyCollectionChangedEventArgs e )
        {
            if ( e?.NewItems == null )
            {
                return;
            }

            foreach ( object newItem in e.NewItems )
            {
                if ( newItem is QueueAction queueAction )
                {
                    _threadQueue?.Enqueue( queueAction, QueuePriority.Low );
                }
            }
        }

        private void ProcessQueue( QueueAction obj )
        {
            // ThreadPriorityQueue's worker loop has no try/catch around this call - an unhandled
            // exception here (including a plain cancellation) would kill the worker thread and silently
            // stop every future queued action for this window, not just this one.
            try
            {
                if ( obj != null && !obj.CancellationTokenSource.IsCancellationRequested )
                {
                    obj.Action.Invoke( obj ).Wait();
                }
            }
            catch ( Exception e )
            {
                Exception inner = e is AggregateException ae ? ae.Flatten().InnerException : e;

                if ( !( inner is OperationCanceledException ) )
                {
                    SentrySdk.CaptureException( inner ?? e );
                }
            }

            _dispatcher.Invoke( () => QueueActions.Remove( obj ) );
        }

        /// <summary>Queues a long-running action as a cancellable, status-tracked row.</summary>
        private void EnqueueAction( Func<QueueAction, Task<bool>> action, string message )
        {
            QueueActions.Add( new QueueAction
            {
                Action = action, CancellationTokenSource = new CancellationTokenSource(), Status = message
            } );
        }

        private void Refresh()
        {
            if ( _customRefresh != null )
            {
                Collection = _customRefresh.Invoke() ?? Collection;
            }

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

        private Task ContextDropToGround( object arg )
        {
            // Old-side probes the 8 tiles around the player for a free spot via MapInfo.ItemCanFit, which
            // this port doesn't have yet. Dropping at the player's own feet is a plain stand-in until it does.
            if ( Engine.Player == null )
            {
                return Task.CompletedTask;
            }

            Item[] items = SelectedItems.Where( i => !i.IsLocked ).Select( i => i.Entity ).OfType<Item>().ToArray();

            int x = Engine.Player.X, y = Engine.Player.Y, z = Engine.Player.Z;

            EnqueueAction( async queueAction =>
            {
                foreach ( var item in items.Select( ( value, i ) => new { i, value } ) )
                {
                    if ( queueAction.CancellationTokenSource.IsCancellationRequested )
                    {
                        _dispatcher.Invoke( () => queueAction.Status = Strings.Cancel );

                        return false;
                    }

                    _dispatcher.Invoke( () => queueAction.Status =
                        string.Format( Strings.Moving_item__0_____1_, item.i, items.Length ) );

                    await ActionPacketQueue.EnqueueDragDropGround( item.value.Serial, item.value.Count, x, y, z );
                }

                return true;
            }, string.Format( Strings.Moving_item__0_____1_, 0, items.Length ) );

            return Task.CompletedTask;
        }

        private void ContextMenuRequest( object obj )
        {
            int[] serials = SelectedItems.Select( ecd => ecd.Entity.Serial ).ToArray();

            EnqueueAction( queueAction =>
            {
                if ( queueAction.CancellationTokenSource.IsCancellationRequested )
                {
                    return Task.FromResult( false );
                }

                foreach ( int serial in serials )
                {
                    Engine.SendPacketToServer( new ContextMenuRequest( serial ) );
                }

                return Task.FromResult( true );
            }, Strings.Context_menu_request );
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
            Item[] items = SelectedItems.Where( i => !i.IsLocked ).Select( i => i.Entity ).OfType<Item>().ToArray();

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

            EnqueueAction( async queueAction =>
            {
                foreach ( var item in items.Select( ( value, i ) => new { i, value } ) )
                {
                    if ( queueAction.CancellationTokenSource.IsCancellationRequested )
                    {
                        _dispatcher.Invoke( () => queueAction.Status = Strings.Cancel );

                        return false;
                    }

                    _dispatcher.Invoke( () => queueAction.Status =
                        string.Format( Strings.Moving_item__0_____1_, item.i, items.Length ) );

                    await ActionPacketQueue.EnqueueDragDrop( item.value.Serial, item.value.Count, serial );
                }

                return true;
            }, string.Format( Strings.Moving_item__0_____1_, 0, items.Length ) );
        }

        private async Task ContextMoveToGround( object arg )
        {
            ( TargetType _, TargetFlags _, int _, int x, int y, int z, int _ ) =
                await Commands.GetTargetInfoAsync( Strings.Target_location___ );

            if ( x == -1 || y == -1 )
            {
                return;
            }

            Item[] items = SelectedItems.Where( i => !i.IsLocked ).Select( i => i.Entity ).OfType<Item>().ToArray();

            EnqueueAction( async queueAction =>
            {
                foreach ( var item in items.Select( ( value, i ) => new { i, value } ) )
                {
                    if ( queueAction.CancellationTokenSource.IsCancellationRequested )
                    {
                        _dispatcher.Invoke( () => queueAction.Status = Strings.Cancel );

                        return false;
                    }

                    _dispatcher.Invoke( () => queueAction.Status =
                        string.Format( Strings.Moving_item__0_____1_, item.i, items.Length ) );

                    await ActionPacketQueue.EnqueueDragDropGround( item.value.Serial, item.value.Count, x, y, z );
                }

                return true;
            }, string.Format( Strings.Moving_item__0_____1_, 0, items.Length ) );
        }

        private Task ContextOpenContainer( object arg )
        {
            int[] containerSerials = SelectedItems
                .Where( e => e.Entity is Item item && item.Owner != 0 && !UOMath.IsMobile( item.Owner ) )
                .Select( e => ( (Item) e.Entity ).Owner ).ToArray();

            EnqueueAction( async queueAction =>
            {
                if ( queueAction.CancellationTokenSource.IsCancellationRequested )
                {
                    return false;
                }

                await ActionPacketQueue.EnqueueActionPackets(
                    containerSerials.Select( s => (BasePacket) new UseObject( s ) ) );

                return true;
            }, Strings.Open_container );

            return Task.CompletedTask;
        }

        private async Task ContextTarget( object obj )
        {
            var items = SelectedItems.Select( ( value, i ) => new { i, value } ).ToList();

            if ( items.Count == 0 )
            {
                return;
            }

            EnqueueAction( async queueAction =>
            {
                foreach ( var item in items )
                {
                    if ( queueAction.CancellationTokenSource.IsCancellationRequested )
                    {
                        _dispatcher.Invoke( () => queueAction.Status = Strings.Cancel );

                        return false;
                    }

                    _dispatcher.Invoke( () => queueAction.Status =
                        string.Format( Strings.Targeting_item__0_____1_, item.i, items.Count ) );

                    if ( Engine.TargetExists || await Task.Run( () => Commands.WaitForTarget( 5000 ) ) )
                    {
                        TargetCommands.Target( item.value.Entity.Serial );
                    }
                }

                return true;
            }, string.Format( Strings.Targeting_item__0_____1_, 0, items.Count ) );
        }

        private void ContextTargetOwner( object obj )
        {
            if ( !( SelectedItems.FirstOrDefault()?.Entity is Item item ) )
            {
                return;
            }

            EnqueueAction( queueAction =>
            {
                if ( queueAction.CancellationTokenSource.IsCancellationRequested )
                {
                    _dispatcher.Invoke( () => queueAction.Status = Strings.Cancel );

                    return Task.FromResult( false );
                }

                TargetCommands.Target( item.Owner );

                return Task.FromResult( true );
            }, string.Format( Strings.Targeting_item__0_____1_, 0, 1 ) );
        }

        private void ContextToggleLock( object obj )
        {
            bool lockTarget = !SelectedItemsAllLocked;

            foreach ( EntityCollectionData ecd in SelectedItems.ToList() )
            {
                ecd.IsLocked = lockTarget;

                if ( lockTarget )
                {
                    if ( !Options.LockedItems.Contains( ecd.Entity.Serial ) )
                    {
                        Options.LockedItems.Add( ecd.Entity.Serial );
                    }
                }
                else
                {
                    Options.LockedItems.Remove( ecd.Entity.Serial );
                }
            }

            // Options.PropertyChanged doesn't fire for mutations inside LockedItems itself, so this
            // needs its own explicit save - see OnOptionsChanged.
            SaveOptions();

            NotifyPropertyChanged( nameof( SelectedItemsAllLocked ) );

            if ( Options.HideLockedItems )
            {
                Rebuild();
            }
        }

        private Task ContextUseItem( object arg )
        {
            int[] items = SelectedItems.Select( i => i.Entity.Serial ).ToArray();

            EnqueueAction( async queueAction =>
            {
                if ( queueAction.CancellationTokenSource.IsCancellationRequested )
                {
                    return false;
                }

                await ActionPacketQueue.EnqueueActionPackets( items.Select( s => (BasePacket) new UseObject( s ) ) );

                return true;
            }, Strings.Use_item );

            return Task.CompletedTask;
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

        private Task EquipItem( object obj )
        {
            Item[] items = SelectedItems.Select( i => i.Entity ).OfType<Item>()
                .Where( i => GetLayer( i.ID ) != Layer.Invalid ).ToArray();

            EnqueueAction( async queueAction =>
            {
                foreach ( var item in items.Select( ( value, i ) => new { i, value } ) )
                {
                    if ( queueAction.CancellationTokenSource.IsCancellationRequested )
                    {
                        _dispatcher.Invoke( () => queueAction.Status = Strings.Cancel );

                        return false;
                    }

                    _dispatcher.Invoke( () => queueAction.Status =
                        string.Format( Strings.Moving_item__0_____1_, item.i, items.Length ) );

                    await Commands.EquipItem( item.value, Layer.Invalid );
                }

                return true;
            }, Strings.Equip_Item );

            return Task.CompletedTask;
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

            Item[] items = itemCollection.GetItems();

            IEnumerable<Item> ordered = comparer == null ? items : items.OrderBy( i => i, comparer );

            return ordered.Select( item => item.ToEntityCollectionData( nameOverrides ) ).ToList();
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
