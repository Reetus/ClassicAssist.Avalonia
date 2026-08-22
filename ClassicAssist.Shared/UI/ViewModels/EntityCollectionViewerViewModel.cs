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
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sentry;
using Commands = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.UI.ViewModels;

/// <summary>
///     Backs the entity collection viewer: the grid of item art behind "Show World Items" and behind
///     opening a container.
///     <para>
///         Ported from the WPF view model with one deferral: the Organizer panel is not ported at all
///         - see ECV_TODO.md for the full gap accounting. The filter is a nested boolean-tree of
///         groups (see <see cref="FilterProfile" />/<see cref="EntityCollectionFilterGroup" />),
///         evaluated by <see cref="EvaluateGroups" /> and persisted to FilterProfiles.json in WPF's
///         shape, so the two sides can share profiles. Browsing, sorting, refreshing, drilling into
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
    private readonly Dictionary<int, string> _nameOverrides = [];

    private readonly string _propertiesFile =
        Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "Data", "Properties.json" );

    private readonly string _propertiesFileCustom =
        Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "Data", "Properties.Custom.json" );

    /// <summary>
    ///     The constraint entries this viewer loaded from <c>Properties.Custom.json</c>, tracked so
    ///     <see cref="ReloadCustomProperties" /> can drop and re-read just those (the rest of
    ///     <see cref="Constraints" /> comes from the bundled file plus the filter-only entries).
    /// </summary>
    private readonly List<PropertyEntry> _customPropertyEntries = [];

    private readonly string _filterProfilesFile =
        Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "FilterProfiles.json" );

    private readonly string _optionsFile =
        Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "EntityCollectionViewerOptions.json" );

    /// <summary>
    ///     Snapshot of the group tree taken at Apply time, so later edits to the profile don't change
    ///     the live filter until it's re-applied. Null when no filter is active - mirrors WPF's
    ///     <c>_filters</c>.
    /// </summary>
    private List<EntityCollectionFilterGroup> _filters;

    /// <summary>
    ///     Feeds the queue rows into <see cref="ThreadPriorityQueue{T}" />, one worker thread serializing
    ///     everything at <see cref="QueuePriority.Low" /> - mirrors WPF's EnqueueAction/ThreadQueue pair.
    /// </summary>
    private readonly ThreadPriorityQueue<QueueAction> _threadQueue;

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
        AutolootPropertyRegistration.LoadPluginProperties( Constraints );
        LoadFilterProfiles();

        CustomPropertiesViewModel.Saved += OnCustomPropertiesSaved;

        Collection = collection ?? new ItemCollection( 0 );

        Rebuild();

        SelectedItems.CollectionChanged += OnSelectedItemsChanged;
        Collection.CollectionChanged += OnCollectionChanged;

        // Item names/properties arrive in a separate OPL packet slightly after the item is
        // added, so refresh the displayed row when its properties are (re)populated.
        IncomingPacketHandlers.ItemPropertiesUpdatedEvent += OnItemPropertiesUpdated;
    }

    /// <summary>
    ///     Maps a mount's equipment item ID to the statue graphic the client draws for it. Empty when the
    ///     data file is absent, in which case mounts simply fall back to their own art.
    /// </summary>
    public static Lazy<Dictionary<int, int>> MountIDEntries { get; set; } =
        new Lazy<Dictionary<int, int>>( LoadMountIDEntries );

    public ICommand AddFilterConditionCommand => field ??=
            new RelayCommand( AddFilterCondition, o =>
                FilterConditions != null && ( SelectedGroup == null || !SelectedGroup.HasChildren ) );

    public ICommand AddGroupCommand => field ??= new RelayCommand( AddGroup, o => true );

    public ICommand AddProfileCommand => field ??= new RelayCommand( AddProfile, o => true );

    public ICommand AddSubGroupCommand => field ??= new RelayCommand( AddSubGroup, o => SelectedGroup != null );

    public ICommand ApplyFiltersCommand => field ??= new RelayCommand( o =>
        {
            _filters = BuildActiveGroups();
            IsFilterApplied = _filters != null;
            Rebuild();
        }, o => true );

    /// <summary>
    ///     The groups a filter application evaluates: the profile's group tree, or - when it has no
    ///     groups - a single synthesized And group carrying its flat <see cref="FilterProfile.Conditions" />,
    ///     so both modes flow through the one <see cref="EvaluateGroups" /> path.
    /// </summary>
    private List<EntityCollectionFilterGroup> BuildActiveGroups()
    {
        if ( SelectedProfile == null )
        {
            return null;
        }

        if ( SelectedProfile.Groups.Count > 0 )
        {
            return SelectedProfile.Groups.ToList();
        }

        return
        [
            new EntityCollectionFilterGroup
            {
                Items = new ObservableCollection<AutolootConstraintEntry>( SelectedProfile.Conditions )
            }
        ];
    }

    public ICommand ChangeSortStyleCommand => field ??= new RelayCommand( ChangeSortStyle, o => true );

    public ICommand RemoveGroupCommand => field ??= new RelayCommand( RemoveGroup, o => SelectedGroup != null );

    public ICommand ResetFiltersCommand => field ??= new RelayCommand( o =>
        {
            _filters = null;
            IsFilterApplied = false;
            Rebuild();
        }, o => true );

    public ItemCollection Collection
    {
        get;
        set => SetProperty( ref field, value );
    } = new ItemCollection( 0 );

    public ICommand CombineStacksCommand => field ??= new RelayCommand( o => CombineStacks(), o => true );

    public ICommand ConfigureCommand => field ??= new RelayCommandAsync( Configure, o => true );

    /// <summary>
    ///     The set of properties a filter condition can be built on - the same list Autoloot constraints
    ///     draw from (<c>Data/Properties.json</c> plus any <c>Properties.Custom.json</c> overrides), plus
    ///     the ECV-only ones from <see cref="RegisterFilterOnlyConstraints" />.
    /// </summary>
    public ObservableCollection<PropertyEntry> Constraints { get; } = [];

    public ICommand ContextContextMenuRequestCommand => field ??=
            new RelayCommand( ContextMenuRequest, o => SelectedItems.Count > 0 );

    /// <summary>
    ///     Sourced from <see cref="CustomContextActions" />, a thin, non-registry extension point old-side
    ///     (unlike the toolbar's <c>IEntityCollectionViewerAction</c> registry). Nothing populates it yet,
    ///     so the "Custom Actions" submenu stays empty/hidden until a caller does.
    /// </summary>
    public ICommand ContextCustomActionCommand => field ??=
            new RelayCommand( ContextCustomAction, o => SelectedItems.Count > 0 );

    public ICommand ContextDropToGroundCommand => field ??=
            new RelayCommandAsync( ContextDropToGround, o => SelectedItems.Count > 0 );

    public ICommand ContextMoveToBackpackCommand => field ??=
            new RelayCommandAsync( ContextMoveToBackpack, o => SelectedItems.Count > 0 );

    public ICommand ContextMoveToBankCommand => field ??=
            new RelayCommandAsync( ContextMoveToBank, o => SelectedItems.Count > 0 );

    public ICommand ContextMoveToContainerCommand => field ??=
            new RelayCommandAsync( ContextMoveToContainer, o => SelectedItems.Count > 0 );

    public ICommand ContextMoveToGroundCommand => field ??=
            new RelayCommandAsync( ContextMoveToGround, o => SelectedItems.Count > 0 );

    public ICommand ContextMoveToSetCommand => field ??=
            new RelayCommandAsync( ContextMoveToSet, o => SelectedItems.Count > 0 );

    public ICommand ContextOpenContainerCommand => field ??= new RelayCommandAsync(
            ContextOpenContainer,
            o => SelectedItems.Any( e =>
                e.Entity is Item item && item.Owner != 0 && !UOMath.IsMobile( item.Owner ) ) );

    public ICommand ContextTargetCommand => field ??=
            new RelayCommandAsync( ContextTarget, o => SelectedItems.Count > 0 );

    public ICommand ContextTargetOwnerCommand => field ??=
            new RelayCommand( ContextTargetOwner, o => SelectedItems.Count == 1 );

    public ICommand ContextToggleLockCommand => field ??=
            new RelayCommand( ContextToggleLock, o => SelectedItems.Count > 0 );

    public ICommand ContextUseItemCommand => field ??=
            new RelayCommandAsync( ContextUseItem, o => SelectedItems.Count > 0 );

    public ICommand CopyToClipboardCommand => field ??= new RelayCommand( o => CopyToClipboard(), o => true );

    /// <summary>
    ///     Old-side's ad-hoc, non-registry context-menu extension point: an owning window can add entries
    ///     directly (<c>CustomContextActions.Add(...)</c>) rather than through a public registration API
    ///     like the toolbar's. Nothing constructs this Avalonia view model with any yet, so the "Custom
    ///     Actions" submenu is present but empty.
    /// </summary>
    public ObservableCollection<KeyValuePair<string, Action<Item>>> CustomContextActions { get; } =
        [];

    public ObservableCollection<EntityCollectionData> Entities
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ICommand EquipItemCommand => field ??= new RelayCommandAsync( EquipItem, o => SelectedItems.Count > 0 );

    /// <summary>The operators a group can combine with its predecessor - the tree editor's
    /// operation dropdown draws from this.</summary>
    public IReadOnlyList<BooleanOperation> OperationOptions { get; } =
        [BooleanOperation.And, BooleanOperation.Or, BooleanOperation.Not];

    /// <summary>
    ///     The condition grid's items source. With any groups it's the selected group's items (tree
    ///     mode); with no groups it's the profile's flat <see cref="FilterProfile.Conditions" /> and
    ///     the group tree is hidden. Re-raised whenever <see cref="SelectedProfile" /> or
    ///     <see cref="SelectedGroup" /> changes so the grid follows whichever collection is active.
    /// </summary>
    public ObservableCollection<AutolootConstraintEntry> FilterConditions
    {
        get
        {
            if ( SelectedProfile == null )
            {
                return null;
            }

            return SelectedProfile.Groups.Count > 0 ? SelectedGroup?.Items : SelectedProfile.Conditions;
        }
    }

    public ICommand HideItemCommand => field ??= new RelayCommand( HideItem, o => SelectedItems.Count > 0 );

    /// <summary>
    ///     A single indirection point for the B/C/K/G/D key bindings, gated on
    ///     <see cref="EntityCollectionViewerOptions.EnableHotkeys" /> - distinct from the same-shaped
    ///     context-menu commands, which stay usable even with hotkeys disabled. Mirrors old's
    ///     <c>HotkeyActionCommand</c>.
    /// </summary>
    public ICommand HotkeyActionCommand => field ??=
            new RelayCommandAsync( HotkeyAction, o => Options.EnableHotkeys && SelectedItems.Count > 0 );

    public bool IsFilterApplied
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand ItemDoubleClickCommand => field ??= new RelayCommand( ItemDoubleClick, o => true );

    public ICommand OpenAllContainersCommand => field ??= new RelayCommand( o => OpenAllContainers(), o => true );

    /// <summary>
    ///     Persisted to <c>EntityCollectionViewerOptions.json</c> in the same shape base ClassicAssist
    ///     uses - see <see cref="LoadOptions" />/<see cref="SaveOptions" />. Bound to directly from XAML
    ///     (<c>Options.AlwaysOnTop</c> etc.) rather than mirrored through VM-level properties.
    /// </summary>
    public EntityCollectionViewerOptions Options
    {
        get;
        set => SetProperty( ref field, value );
    }

    /// <summary>Saved, named filter condition sets - see <see cref="LoadFilterProfiles" />/<see cref="SaveFilterProfiles" />.</summary>
    public ObservableCollection<FilterProfile> Profiles { get; } = [];

    /// <summary>Long-running move/loot/target actions, run one at a time with a cancel button each.</summary>
    public ObservableCollection<QueueAction> QueueActions { get; } = [];

    public ICommand RefreshCommand => field ??= new RelayCommand( o => Refresh(), o => true );

    public ICommand RemoveFilterConditionCommand => field ??=
            new RelayCommand( RemoveFilterCondition, o => o is AutolootConstraintEntry );

    public ICommand RemoveProfileCommand => field ??= new RelayCommand( RemoveProfile, o => true );

    public ICommand SaveProfilesCommand => field ??= new RelayCommand( o => SaveFilterProfiles(), o => true );

    public bool SelectedItemsAllLocked => SelectedItems.Count > 0 && SelectedItems.All( e => e.IsLocked );

    public ObservableCollection<EntityCollectionData> SelectedItems
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    /// <summary>
    ///     The profile currently being edited. Switching profiles points <see cref="SelectedGroup" />
    ///     at the new profile's first group and, if a filter is active, re-applies it from the new
    ///     profile (old's <c>SetActiveProfile</c>). Subscribes to the profile's group tree so
    ///     <see cref="ShowGroupTree" /> follows add/remove anywhere in it.
    /// </summary>
    public FilterProfile SelectedProfile
    {
        get;
        set
        {
            if ( field != null )
            {
                field.Groups.CollectionChanged -= OnProfileGroupsChanged;
                UnsubscribeGroupsRecursive( field.Groups );
            }

            SetProperty( ref field, value );

            if ( value != null )
            {
                value.Groups.CollectionChanged += OnProfileGroupsChanged;
                SubscribeGroupsRecursive( value.Groups );
            }

            SelectedGroup = value?.Groups.FirstOrDefault();

            OnPropertyChanged( nameof( FilterConditions ) );
            OnPropertyChanged( nameof( ShowGroupTree ) );

            if ( IsFilterApplied )
            {
                _filters = BuildActiveGroups();
                Rebuild();
            }
        }
    }

    /// <summary>
    ///     The group whose items the filter editor's condition grid is showing. Null in flat mode (no
    ///     groups) or with nothing selected - <see cref="FilterConditions" /> falls back to the
    ///     profile's flat conditions then.
    /// </summary>
    public EntityCollectionFilterGroup SelectedGroup
    {
        get;
        set
        {
            SetProperty( ref field, value );
            OnPropertyChanged( nameof( FilterConditions ) );
            OnPropertyChanged( nameof( SelectedGroupIsBranch ) );
        }
    }

    /// <summary>
    ///     Whether the selected group is a branch (has sub-groups) - its own conditions are ignored by
    ///     evaluation, so the editor shows a placeholder instead of a condition grid. False when
    ///     nothing is selected (flat mode), where the grid edits the profile's flat conditions.
    /// </summary>
    public bool SelectedGroupIsBranch => SelectedGroup is { HasChildren: true };

    /// <summary>
    ///     Whether the filter editor shows the group tree + condition grid split. True when there are
    ///     multiple top-level groups, or any group has sub-groups - a single branch group needs the
    ///     tree to reach its children, and a tree with nothing to navigate is just wasted space.
    ///     Mirrors WPF's <c>HasSubgroups</c> flat-vs-split decision, plus the multi-group case.
    /// </summary>
    public bool ShowGroupTree =>
        SelectedProfile != null &&
        ( SelectedProfile.Groups.Count > 1 || HasSubgroupsRecursive( SelectedProfile.Groups ) );

    private void OnProfileGroupsChanged( object sender, NotifyCollectionChangedEventArgs e )
    {
        if ( e.NewItems != null )
        {
            foreach ( object obj in e.NewItems )
            {
                if ( obj is EntityCollectionFilterGroup group )
                {
                    SubscribeGroupsRecursive( [group] );
                }
            }
        }

        if ( e.OldItems != null )
        {
            foreach ( object obj in e.OldItems )
            {
                if ( obj is EntityCollectionFilterGroup group )
                {
                    UnsubscribeGroupsRecursive( [group] );
                }
            }
        }

        OnPropertyChanged( nameof( ShowGroupTree ) );
        OnPropertyChanged( nameof( SelectedGroupIsBranch ) );
    }

    private void SubscribeGroupsRecursive( IEnumerable<EntityCollectionFilterGroup> groups )
    {
        foreach ( EntityCollectionFilterGroup group in groups )
        {
            group.Children.CollectionChanged += OnProfileGroupsChanged;
            SubscribeGroupsRecursive( group.Children );
        }
    }

    private void UnsubscribeGroupsRecursive( IEnumerable<EntityCollectionFilterGroup> groups )
    {
        foreach ( EntityCollectionFilterGroup group in groups )
        {
            group.Children.CollectionChanged -= OnProfileGroupsChanged;
            UnsubscribeGroupsRecursive( group.Children );
        }
    }

    private static bool HasSubgroupsRecursive( IEnumerable<EntityCollectionFilterGroup> groups )
    {
        foreach ( EntityCollectionFilterGroup group in groups )
        {
            if ( group.Children.Count > 0 )
            {
                return true;
            }

            if ( HasSubgroupsRecursive( group.Children ) )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the filter condition panel is visible - does not by itself apply the filter.</summary>
    public bool ShowFilter
    {
        get;
        set => SetProperty( ref field, value );
    }

    /// <summary>Label each tile with its full property list rather than just its name.</summary>
    public bool ShowProperties
    {
        get;
        set => SetProperty( ref field, value );
    }

    public string StatusLabel
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand TogglePropertiesCommand => field ??=
            new RelayCommand( o => ShowProperties = !ShowProperties, o => true );

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
        IncomingPacketHandlers.ItemPropertiesUpdatedEvent -= OnItemPropertiesUpdated;
        _threadQueue?.Dispose();
    }

    private static Dictionary<int, int> LoadMountIDEntries()
    {
        string fileName = Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "Data",
            "MountID.json" );

        if ( !File.Exists( fileName ) )
        {
            return [];
        }

        try
        {
            return JsonConvert.DeserializeObject<Dictionary<int, int>>( File.ReadAllText( fileName ) ) ??
                   [];
        }
        catch ( Exception )
        {
            return [];
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
        if ( obj is not EntityCollectionSortStyle val )
        {
            return;
        }

        // Clicking the active sort style again clears it back to insertion order.
        Options.SortStyle = Options.SortStyle == val ? EntityCollectionSortStyle.None : val;
    }

    private static void ItemDoubleClick( object obj )
    {
        if ( obj is not EntityCollectionData data )
        {
            return;
        }

        // A container opens as another viewer; anything else has nothing to browse into, so show what
        // the client knows about it instead.
        if ( data.Entity is Item item && item.Container != null )
        {
            Engine.UIInvoker?.Invoke( "EntityCollectionViewer", null,
                typeof( EntityCollectionViewerViewModel ), [item.Container] );

            return;
        }

        Engine.UIInvoker?.Invoke( "ObjectInspectorWindow", null, typeof( ObjectInspectorViewModel ),
            [data.Entity] );
    }

    private void OnCollectionChanged( int totalCount, bool added, Item[] entities )
    {
        IDispatcher dispatcher = _dispatcher ?? Engine.Dispatcher;

        if ( dispatcher != null && !dispatcher.CheckAccess() )
        {
            dispatcher.Invoke( () => ApplyCollectionChange( added, entities ) );
        }
        else
        {
            ApplyCollectionChange( added, entities );
        }
    }

    /// <summary>
    ///     Patches <see cref="Entities" /> in place for a live add/remove instead of routing through
    ///     <see cref="Rebuild" />, which swaps in a whole new <see cref="ObservableCollection{T}" /> of
    ///     brand new <see cref="EntityCollectionData" /> rows. <see cref="EntityCollectionData" /> has no
    ///     <c>Equals</c> override, so that wholesale replacement made every row a different reference
    ///     than whatever was in <see cref="SelectedItems" />, deselecting the entire grid on every
    ///     server update - including mid drag-and-drop, where the point is to keep selecting/dropping the
    ///     remaining items. Rows untouched by this particular add/remove keep their identity here, so
    ///     they stay selected. Mirrors the WPF build's <c>OnCollectionChanged</c>.
    /// </summary>
    private void ApplyCollectionChange( bool added, Item[] entities )
    {
        if ( added )
        {
            List<Item> newEntities = [.. entities
                .Where( e => Options.ShowChildItems || e.Owner == Collection.Serial )
                .Where( e => Entities.All( ecd => !ecd.Entity.Equals( e ) ) )];

            if ( newEntities.Count > 0 )
            {
                // A single-item evaluation against the applied group tree, so live adds get the same
                // boolean-tree treatment a full Rebuild would give them.
                List<Predicate<Item>> predicates = IsFilterApplied && _filters != null
                    ? [MatchesFilter]
                    : null;

                IComparer<Entity> sorter = GetSorter();

                foreach ( Item entity in newEntities )
                {
                    if ( predicates != null && !predicates.All( p => p( entity ) ) )
                    {
                        continue;
                    }

                    bool isLocked = Options.LockedItems.Contains( entity.Serial );

                    if ( Options.HideLockedItems && isLocked )
                    {
                        continue;
                    }

                    EntityCollectionData data = entity.ToEntityCollectionData( _nameOverrides );
                    data.IsLocked = isLocked;

                    if ( sorter == null )
                    {
                        Entities.Add( data );
                    }
                    else
                    {
                        int index = 0;

                        while ( index < Entities.Count &&
                                sorter.Compare( Entities[index].Entity, entity ) <= 0 )
                        {
                            index++;
                        }

                        Entities.Insert( index, data );
                    }
                }
            }
        }
        else
        {
            foreach ( EntityCollectionData ecd in entities
                         .Select( entity => Entities.FirstOrDefault( e => e.Entity.Equals( entity ) ) )
                         .Where( ecd => ecd != null ).ToList() )
            {
                Entities.Remove( ecd );
            }
        }

        UpdateStatusLabel();
    }

    /// <summary>
    ///     Refreshes an already-displayed row when the underlying item's name/properties/hue arrive
    ///     (or change) after the row was created - an OPL packet routinely lands after the item was
    ///     first added, so without this the tile is stuck showing whatever it had at insert time until
    ///     the next full <see cref="Rebuild" />. Mirrors the WPF build's <c>OnItemPropertiesUpdated</c>.
    /// </summary>
    private void OnItemPropertiesUpdated( Item item )
    {
        // Runs on the network thread. Filter cheaply against the (thread-safe) collection so we
        // only marshal to the UI thread for items this viewer actually displays.
        if ( item == null || !Collection.GetItem( item.Serial, out _ ) )
        {
            return;
        }

        IDispatcher dispatcher = _dispatcher ?? Engine.Dispatcher;

        void Apply()
        {
            EntityCollectionData ecd = Entities.FirstOrDefault( e => e.Entity.Serial == item.Serial );

            if ( ecd == null )
            {
                return;
            }

            // OnProperties overwrites Item.Name with the server value, which would clobber a
            // user-applied rename - re-apply the override before refreshing the row.
            if ( _nameOverrides.TryGetValue( item.Serial, out string nameOverride ) )
            {
                item.Name = nameOverride;
            }

            ecd.NotifyPropertiesUpdated();

            // Names/properties arrive after the row was inserted, so under a property-derived
            // sort (e.g. Name or Weight) the item may now be in the wrong place - move it.
            IComparer<Entity> sorter = GetSorter();

            if ( sorter == null )
            {
                return;
            }

            int oldIndex = Entities.IndexOf( ecd );

            if ( oldIndex < 0 )
            {
                return;
            }

            int newIndex = 0;

            for ( int i = 0; i < Entities.Count; i++ )
            {
                if ( i == oldIndex )
                {
                    continue;
                }

                if ( sorter.Compare( Entities[i].Entity, ecd.Entity ) <= 0 )
                {
                    newIndex++;
                }
                else
                {
                    break;
                }
            }

            if ( newIndex != oldIndex )
            {
                Entities.Move( oldIndex, newIndex );
            }
        }

        if ( dispatcher != null && !dispatcher.CheckAccess() )
        {
            dispatcher.Invoke( Apply );
        }
        else
        {
            Apply();
        }
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
        if ( !IsFilterApplied || _filters == null || _filters.Count == 0 )
        {
            return source;
        }

        return EvaluateGroups( _filters, source );
    }

    /// <summary>
    ///     Applies the snapshot <see cref="_filters" /> to a single item - used by the live-add path
    ///     (<see cref="ApplyCollectionChange" />), which patches rows in place rather than rebuilding.
    /// </summary>
    private bool MatchesFilter( Item item )
    {
        ItemCollection single = new( item.Owner ) { item };

        return EvaluateGroups( _filters, single ).GetItems().Contains( item );
    }

    private void AddFilterCondition( object obj )
    {
        FilterConditions?.Add( new AutolootConstraintEntry { Property = Constraints.FirstOrDefault() } );
    }

    private void RemoveFilterCondition( object obj )
    {
        if ( obj is AutolootConstraintEntry entry )
        {
            FilterConditions?.Remove( entry );
        }
    }

    // Combines top-level groups left-to-right using each group's Operation (first group's
    // operation is ignored - there is nothing before it to combine with). Ported line-for-line from
    // WPF's EntityCollectionViewerViewModel.EvaluateGroups.
    internal static ItemCollection EvaluateGroups( List<EntityCollectionFilterGroup> groups, ItemCollection source )
    {
        if ( groups == null || groups.Count == 0 )
        {
            return source;
        }

        ItemCollection items = EvaluateGroup( groups[0], source );

        for ( int i = 1; i < groups.Count; i++ )
        {
            EntityCollectionFilterGroup group = groups[i];

            ItemCollection groupItems = group.Operation == BooleanOperation.Or
                ? EvaluateGroup( group, source )
                : EvaluateGroup( group, items );

            switch ( group.Operation )
            {
                case BooleanOperation.And:
                    items = groupItems;

                    break;
                case BooleanOperation.Or:
                    items.Add( groupItems.GetItems() );

                    break;
                case BooleanOperation.Not:
                    items.Remove( groupItems.GetItems() );

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return items;
    }

    internal static ItemCollection EvaluateGroup( EntityCollectionFilterGroup group, ItemCollection source )
    {
        // Branch groups (with sub-groups) are pure boolean containers - their own filters are ignored
        // (hidden in the editor).
        ItemCollection result = group.Children.Count > 0 ? source : FilterItems( group.Items, source );

        if ( group.Children.Count == 0 )
        {
            return result;
        }

        ItemCollection childResult = EvaluateGroup( group.Children[0], result );

        for ( int i = 1; i < group.Children.Count; i++ )
        {
            EntityCollectionFilterGroup child = group.Children[i];

            ItemCollection childItems = child.Operation == BooleanOperation.Or
                ? EvaluateGroup( child, result )
                : EvaluateGroup( child, childResult );

            switch ( child.Operation )
            {
                case BooleanOperation.And:
                    childResult = childItems;

                    break;
                case BooleanOperation.Or:
                    childResult.Add( childItems.GetItems() );

                    break;
                case BooleanOperation.Not:
                    childResult.Remove( childItems.GetItems() );

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return childResult;
    }

    /// <summary>
    ///     Filters <paramref name="source" /> down to the items matching every enabled condition in
    ///     <paramref name="items" /> - WPF's <c>source.Filter(group.Items)</c>, expressed through the
    ///     shared Autoloot predicate system (the item type here is <see cref="AutolootConstraintEntry" />,
    ///     WPF's <c>EntityCollectionFilterItem</c>). An empty set matches everything, like WPF.
    /// </summary>
    private static ItemCollection FilterItems( IEnumerable<AutolootConstraintEntry> items, ItemCollection source )
    {
        IEnumerable<AutolootConstraintEntry> enabled =
            items?.Where( i => i != null && i.Enabled ) ?? Enumerable.Empty<AutolootConstraintEntry>();

        List<Predicate<Item>> predicates = [.. AutolootHelpers.ConstraintsToPredicates( enabled )];

        ItemCollection filtered = new( source.Serial );

        foreach ( Item item in source.GetItems() )
        {
            if ( predicates.All( p => p( item ) ) )
            {
                filtered.Add( item );
            }
        }

        return filtered;
    }

    private void LoadCustomProperties()
    {
        if ( !File.Exists( _propertiesFileCustom ) )
        {
            return;
        }

        JsonSerializer serializer = new();

        using StreamReader sr = new( _propertiesFileCustom );
        using JsonTextReader reader = new( sr );
        PropertyEntry[] constraints = serializer.Deserialize<PropertyEntry[]>( reader );

        foreach ( PropertyEntry constraint in constraints ?? [] )
        {
            _customPropertyEntries.Add( constraint );
            Constraints.AddSorted( constraint );
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

        JsonSerializer serializer = new();

        using StreamReader sr = new( _propertiesFile );
        using JsonTextReader reader = new( sr );
        PropertyEntry[] constraints = serializer.Deserialize<PropertyEntry[]>( reader );

        foreach ( PropertyEntry constraint in constraints ?? [] )
        {
            Constraints.AddSorted( constraint );
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
            },
            AllowedValuesEnum = typeof( TileFlags )
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
            },
            // Picks the organizer profile the predicate looks up by name. Old sets the same list on
            // "Is Multi" too, which only reads the operator - copied there by accident, and following
            // it would put an organizer dropdown on a yes/no constraint, so it stops here.
            Options = new ObservableCollection<string>(
                OrganizerManager.GetInstance().Items?.Select( o => o.Name ) ?? [] )
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
                FilterProfile profile = new()
                {
                    ID = profileToken["ID"]?.ToObject<Guid>() ?? Guid.NewGuid(),
                    Name = profileToken["Name"]?.ToObject<string>() ?? "New Filter Profile"
                };

                // "Groups" is WPF's shape (and this port's, now that they share it); "Conditions" is
                // this port's own earlier flat shape, which stays flat (no groups, tree hidden) until
                // a group is added.
                if ( profileToken["Groups"] is JArray groupArray )
                {
                    foreach ( JToken groupObj in groupArray )
                    {
                        profile.Groups.Add( DeserializeGroup( groupObj ) );
                    }
                }

                if ( profileToken["Conditions"] is JArray conditions )
                {
                    foreach ( JToken condition in conditions )
                    {
                        DeserializeCondition( condition, profile.Conditions );
                    }
                }

                profile.UpdateGroupsFirstFlags();

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

    private EntityCollectionFilterGroup DeserializeGroup( JToken groupObj )
    {
        EntityCollectionFilterGroup group = new()
        {
            Operation = groupObj["Operation"]?.ToObject<BooleanOperation>() ?? BooleanOperation.And
        };

        if ( groupObj["Items"] != null )
        {
            foreach ( JToken itemObj in groupObj["Items"] )
            {
                DeserializeCondition( itemObj, group.Items );
            }
        }

        if ( groupObj["Children"] != null )
        {
            foreach ( JToken childObj in groupObj["Children"] )
            {
                group.Children.Add( DeserializeGroup( childObj ) );
            }
        }

        return group;
    }

    private void DeserializeCondition( JToken conditionToken, ObservableCollection<AutolootConstraintEntry> target )
    {
        // "Constraint"."Name" is WPF's (and now this port's) shape; "Property" is this port's legacy
        // flat shape.
        string propertyName = conditionToken["Constraint"]?["Name"]?.ToObject<string>() ??
                               conditionToken["Property"]?.ToObject<string>();

        // Deliberately no fall back to the first constraint: a name that doesn't resolve means the
        // constraint isn't registered this session - a plugin that failed to load, or an old-side
        // property this port doesn't have - and silently adopting an unrelated property would change
        // what the filter matches with nothing to show it.
        PropertyEntry property = Constraints.FirstOrDefault( c => c.Name == propertyName );

        if ( property == null )
        {
            return;
        }

        AutolootConstraintEntry condition = new()
        {
            Property = property,
            Operator = conditionToken["Operator"]?.ToObject<AutolootOperator>() ?? AutolootOperator.Equal,
            Value = conditionToken["Value"]?.ToObject<int>() ?? 0,
            Additional = conditionToken["Additional"]?.ToObject<string>(),
            // Absent for every condition written before this was persisted - defaults to enabled,
            // matching AutolootConstraintEntry's own default.
            Enabled = conditionToken["Enabled"]?.ToObject<bool>() ?? true
        };

        // Absent for every condition written before this was persisted, and for the majority that
        // don't use it - left at its default rather than an empty set.
        if ( conditionToken["Values"] != null )
        {
            condition.Values = conditionToken["Values"].ToObject<ObservableCollection<int>>() ?? [];
        }

        target.Add( condition );
    }

    private void AddDefaultProfile()
    {
        FilterProfile profile = new() { Name = "Default" };

        Profiles.Add( profile );
        SelectedProfile = profile;
    }

    public void SaveFilterProfiles()
    {
        try
        {
            JObject obj = new() { { "LastProfileID", SelectedProfile?.ID } };

            JArray profiles = [];

            foreach ( FilterProfile profile in Profiles )
            {
                JObject profileObj = new() { { "ID", profile.ID }, { "Name", profile.Name } };

                JArray groups = [];

                // A flat profile (no groups) is written as a single And group so a WPF-loaded
                // FilterProfiles.json still carries its conditions.
                if ( profile.Groups.Count > 0 )
                {
                    foreach ( EntityCollectionFilterGroup group in profile.Groups )
                    {
                        groups.Add( SerializeGroup( group ) );
                    }
                }
                else if ( profile.Conditions.Count > 0 )
                {
                    groups.Add( SerializeGroup( new EntityCollectionFilterGroup
                    {
                        Items = new ObservableCollection<AutolootConstraintEntry>( profile.Conditions )
                    } ) );
                }

                profileObj.Add( "Groups", groups );

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

    private static JObject SerializeGroup( EntityCollectionFilterGroup group )
    {
        JObject groupObj = new() { { "Operation", (int) group.Operation } };

        JArray items = [];

        foreach ( AutolootConstraintEntry condition in group.Items )
        {
            // A condition with no property can't be evaluated, and writing "Constraint": null is worse
            // than dropping it - on load an unnamed condition can't be matched, so it would come back
            // as some unrelated property carrying this one's operator/value.
            if ( condition.Property == null )
            {
                continue;
            }

            JObject conditionObj = new()
            {
                { "Operator", (int) condition.Operator },
                { "Value", condition.Value },
                { "Additional", condition.Additional },
                { "Enabled", condition.Enabled }
            };

            // Only written when there's something in it, matching old. This is the multi-value set
            // behind ID (Multiple) / Cliloc (Multiple) - without it those conditions came back empty
            // after a restart and matched nothing.
            if ( condition.Values != null && condition.Values.Count > 0 )
            {
                conditionObj.Add( "Values", JArray.FromObject( condition.Values ) );
            }

            conditionObj.Add( "Constraint", new JObject { { "Name", condition.Property.Name } } );

            items.Add( conditionObj );
        }

        groupObj.Add( "Items", items );

        if ( group.Children.Count > 0 )
        {
            JArray children = [];

            foreach ( EntityCollectionFilterGroup child in group.Children )
            {
                children.Add( SerializeGroup( child ) );
            }

            groupObj.Add( "Children", children );
        }

        return groupObj;
    }

    private void AddProfile( object obj )
    {
        FilterProfile profile = new() { Name = "New Filter Profile" };

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

    private void AddGroup( object obj )
    {
        EntityCollectionFilterGroup group = new()
        {
            Items = new ObservableCollection<AutolootConstraintEntry>()
        };

        if ( SelectedProfile.Groups.Count == 0 )
        {
            // flat -> tree: the flat conditions become this first group's items so nothing is lost
            foreach ( AutolootConstraintEntry condition in SelectedProfile.Conditions )
            {
                group.Items.Add( condition );
            }

            SelectedProfile.Conditions.Clear();
        }

        // A new group starts empty - conditions are added via the grid's Add button, so nothing
        // depends on a constraint being available to seed it with.

        SelectedProfile.Groups.Add( group );
        SelectedProfile.UpdateGroupsFirstFlags();
        SelectedGroup = group;
    }

    private void AddSubGroup( object obj )
    {
        EntityCollectionFilterGroup group = new()
        {
            Items = new ObservableCollection<AutolootConstraintEntry>()
        };

        SelectedGroup?.Children.Add( group );
        SelectedGroup = group;
    }

    private void RemoveGroup( object obj )
    {
        EntityCollectionFilterGroup group = obj as EntityCollectionFilterGroup ?? SelectedGroup;

        if ( group == null || SelectedProfile == null )
        {
            return;
        }

        if ( SelectedProfile.Groups.Remove( group ) )
        {
            SelectedProfile.UpdateGroupsFirstFlags();
            SelectedGroup = SelectedProfile.Groups.FirstOrDefault();

            if ( SelectedGroup == null )
            {
                // tree -> flat: keep the removed group's conditions in the flat list
                foreach ( AutolootConstraintEntry condition in group.Items )
                {
                    SelectedProfile.Conditions.Add( condition );
                }
            }

            return;
        }

        foreach ( EntityCollectionFilterGroup parent in SelectedProfile.Groups )
        {
            if ( RemoveChildRecursive( parent, group ) )
            {
                SelectedGroup = parent;

                return;
            }
        }

        SelectedGroup = SelectedProfile.Groups.FirstOrDefault();
    }

    private static bool RemoveChildRecursive( EntityCollectionFilterGroup parent, EntityCollectionFilterGroup child )
    {
        if ( parent.Children.Remove( child ) )
        {
            return true;
        }

        foreach ( EntityCollectionFilterGroup subGroup in parent.Children )
        {
            if ( RemoveChildRecursive( subGroup, child ) )
            {
                return true;
            }
        }

        return false;
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
                List<int> ignoreList = [];

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
                        options: new DragDropOptions { CheckExisting = true, DelaySend = false } );

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

        List<string> newArguments = [.. ( from argument in property.Arguments
                                      select argument.Equals( item.Count.ToString() ) ? string.Empty : argument )];

        return newArguments.Count == 0
            ? item.Name.Trim()
            : Cliloc.GetLocalString( property.Cliloc, [.. newArguments] ).Trim();
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

        Item[] containers = [.. Collection.GetItems().Where( i => TileData.GetStaticTile( i.ID ).Flags.HasFlag( TileFlags.Container ) && !Excluded( i ) )];

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
            new()
            { Options = Options };

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
        if ( !Options.EnableHotkeys || arg is not string action )
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

            if ( inner is not OperationCanceledException )
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
            Action = action,
            CancellationTokenSource = new CancellationTokenSource(),
            Status = message
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
        if ( arg is not KeyValuePair<string, Action<Item>> action )
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
        if ( Engine.Player == null )
        {
            return Task.CompletedTask;
        }

        Item[] items = [.. SelectedItems.Where( i => !i.IsLocked ).Select( i => i.Entity ).OfType<Item>()];

        int map = (int) Engine.Player.Map;

        // 8 adjacent tiles, clockwise from north: N, NE, E, SE, S, SW, W, NW. The player's own tile is
        // deliberately not among them - a mobile occupies it, so a drop there gets rejected and bounces
        // the item back.
        int[][] offsets =
        [
            [0, -1], [1, -1], [1, 0], [1, 1], [0, 1], [-1, 1],
            [-1, 0], [-1, -1]
        ];

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

                foreach ( int[] offset in offsets )
                {
                    int tx = Engine.Player.X + offset[0];
                    int ty = Engine.Player.Y + offset[1];

                    if ( MapInfo.ItemCanFit( map, tx, ty, item.value.ID, out int dropZ ) )
                    {
                        await ActionPacketQueue.EnqueueDragDropGround( item.value.Serial, item.value.Count, tx,
                            ty, dropZ );

                        break;
                    }
                }
            }

            return true;
        }, string.Format( Strings.Moving_item__0_____1_, 0, items.Length ) );

        return Task.CompletedTask;
    }

    private void ContextMenuRequest( object obj )
    {
        int[] serials = [.. SelectedItems.Select( ecd => ecd.Entity.Serial )];

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
        Item[] items = [.. SelectedItems.Where( i => !i.IsLocked ).Select( i => i.Entity ).OfType<Item>()];

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
        (TargetType _, TargetFlags _, int _, int x, int y, int z, int _) =
            await Commands.GetTargetInfoAsync( Strings.Target_location___ );

        if ( x == -1 || y == -1 )
        {
            return;
        }

        Item[] items = [.. SelectedItems.Where( i => !i.IsLocked ).Select( i => i.Entity ).OfType<Item>()];

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

    /// <summary>
    ///     Moves the selection into whichever container in <paramref name="arg" /> (a
    ///     <see cref="ContainerSet" />'s <c>Items</c>) currently has room, cycling through the set as
    ///     containers fill up. Mirrors the WPF build's <c>ContextMoveToSet</c>.
    /// </summary>
    private async Task ContextMoveToSet( object arg )
    {
        if ( arg is not ObservableCollection<int> containers )
        {
            return;
        }

        List<int> usedContainers = [];

        Item[] items = [.. SelectedItems.Where( i => !i.IsLocked ).Select( i => i.Entity ).OfType<Item>()];

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

                for ( int attempts = 0; attempts < 5; attempts++ )
                {
                    int serial = await GetContainer();

                    if ( item.value.Owner == serial )
                    {
                        break;
                    }

                    PacketFilterInfo pfi = new( 0x25,
                        [
                            PacketFilterConditions.IntAtPositionCondition( item.value.Serial, 1 ),
                            PacketFilterConditions.IntAtPositionCondition( serial, 15 )
                        ] );
                    PacketWaitEntry waitEntry = Engine.PacketWaitEntries.Add( pfi, PacketDirection.Incoming, true );

                    if ( !await ActionPacketQueue.EnqueueDragDrop( item.value.Serial, -1, serial,
                            cancellationToken: queueAction.CancellationTokenSource.Token ) )
                    {
                        Commands.SystemMessage( $"Retrying 0x{item.value.Serial:x}..." );

                        continue;
                    }

                    bool result = waitEntry.Lock.WaitOne( 3000 );

                    if ( !result )
                    {
                        Commands.SystemMessage( $"Retrying 0x{item.value.Serial:x}..." );

                        continue;
                    }

                    break;
                }
            }

            return true;
        }, string.Format( Strings.Moving_item__0_____1_, 0, items.Length ) );

        return;

        async Task<int> GetContainer()
        {
            if ( !Engine.TooltipsEnabled )
            {
                return containers.FirstOrDefault();
            }

            int serial = containers.FirstOrDefault();

            foreach ( int container in containers )
            {
                Item item = Engine.Items.GetItem( container );

                if ( item == null )
                {
                    continue;
                }

                if ( item.Properties == null )
                {
                    await Commands.WaitForPropertiesAsync( [item], 5000 );
                }

                Property property = item.Properties?.FirstOrDefault( e => e.Cliloc == 1073841 );

                if ( property == null )
                {
                    continue;
                }

                if ( property.Arguments[0].Equals( property.Arguments[1] ) )
                {
                    continue;
                }

                serial = container;

                if ( !usedContainers.Contains( serial ) )
                {
                    Commands.WaitForContainerContentsUse( serial, 5000 );
                    await Task.Delay( ClassicAssist.Data.Options.CurrentOptions.ActionDelayMS );
                    usedContainers.Add( serial );
                }

                break;
            }

            return serial;
        }
    }

    private Task ContextOpenContainer( object arg )
    {
        int[] containerSerials = [.. SelectedItems
            .Where( e => e.Entity is Item item && item.Owner != 0 && !UOMath.IsMobile( item.Owner ) )
            .Select( e => ( (Item) e.Entity ).Owner )];

        EnqueueAction( async queueAction =>
        {
            if ( queueAction.CancellationTokenSource.IsCancellationRequested )
            {
                return false;
            }

            await ActionPacketQueue.EnqueuePackets(
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
        if ( SelectedItems.FirstOrDefault()?.Entity is not Item item )
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
        int[] items = [.. SelectedItems.Select( i => i.Entity.Serial )];

        EnqueueAction( async queueAction =>
        {
            if ( queueAction.CancellationTokenSource.IsCancellationRequested )
            {
                return false;
            }

            await ActionPacketQueue.EnqueuePackets( items.Select( s => (BasePacket) new UseObject( s ) ) );

            return true;
        }, Strings.Use_item );

        return Task.CompletedTask;
    }

    private void CopyToClipboard()
    {
        IEnumerable<EntityCollectionData> items = SelectedItems.Any() ? SelectedItems : (IEnumerable<EntityCollectionData>) Entities;

        StringBuilder stringBuilder = new();

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
        Item[] items = [.. SelectedItems.Select( i => i.Entity ).OfType<Item>().Where( i => GetLayer( i.ID ) != Layer.Invalid )];

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
            return [];
        }

        Item[] items = itemCollection.GetItems();

        IEnumerable<Item> ordered = comparer == null ? items : items.OrderBy( i => i, comparer );

        return [.. ordered.Select( item => item.ToEntityCollectionData( nameOverrides ) )];
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
