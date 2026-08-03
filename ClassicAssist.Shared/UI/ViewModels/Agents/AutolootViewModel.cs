using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Data.Regions;
using ClassicAssist.Data.Targeting;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UI.ViewModels.Autoloot;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UI.Misc;
using ClassicAssist.UI.Misc.DraggableTreeView;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;
using Microsoft.Scripting.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UOC = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.Shared.UI.ViewModels.Agents;

public class AutolootViewModel : BaseViewModel, ISettingProvider
{
    private const int LOOT_TIMEOUT = 5000;
    private readonly object _autolootLock = new();

    private readonly string _propertiesFile = Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "Data", "Properties.json" );

    private readonly string _propertiesFileCustom = Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "Data", "Properties.Custom.json" );

    private ObservableCollectionEx<AutolootEntry> _items = new();

    private ObservableCollection<IDraggable> _draggables = new();

    private bool _lootHumanoids;
    private bool _requeueFailedItems;
    private AutolootGroup _selectedGroup;

    private RelayCommand _resetContainerCommand;

    public AutolootViewModel()
    {
        // Sync the groups-only view used by the Move-to-group menu before anything can populate
        // Draggables, so the menu never lists the ungrouped entries that also live at the root.
        Draggables.CollectionChanged += OnDraggablesChanged;

        if ( !File.Exists( _propertiesFile ) )
        {
            return;
        }

        LoadProperties();
        LoadCustomProperties();
        AutolootPropertyRegistration.LoadSpecialProperties( Constraints );

        AutolootHelpers.SetAutolootContainer = serial => ContainerSerial = serial;
        IncomingPacketHandlers.CorpseContainerDisplayEvent += OnCorpseEvent;
        AutolootManager manager = AutolootManager.GetInstance();
        manager.GetEntries = () => _items.ToList();
        manager.CheckContainer = OnCorpseEvent;
        manager.IsEnabled = () => Enabled;
        manager.SetEnabled = enabled => Enabled = enabled;
        manager.IsRunning = () => false;
        manager.MatchTextValue = () => MatchTextValue;

        Items.CollectionChanged += UpdateDraggables;
    }

    /// <summary>
    ///     The <see cref="AutolootGroup" /> entries in <see cref="Draggables" />, for menus that must
    ///     offer only groups as targets (e.g. Move to group).
    /// </summary>
    public ObservableCollection<AutolootGroup> Groups { get; } = new();

    private void OnDraggablesChanged( object sender, NotifyCollectionChangedEventArgs e )
    {
        if ( e.Action != NotifyCollectionChangedAction.Reset &&
             e.NewItems?.OfType<AutolootGroup>().Any() != true &&
             e.OldItems?.OfType<AutolootGroup>().Any() != true )
        {
            return;
        }

        Groups.Clear();

        foreach ( AutolootGroup group in Draggables.OfType<AutolootGroup>() )
        {
            Groups.Add( group );
        }
    }

    public ICommand ClipboardCopyCommand => field ??= new RelayCommand( ClipboardCopy, o => true );

    public ICommand ClipboardPasteCommand => field ??= new RelayCommand( ClipboardPaste, o => true );

    public ObservableCollection<PropertyEntry> Constraints
    {
        get;
        set => SetProperty( ref field, value );
    } = new();

    public int ContainerSerial
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand CSVImportCommand => field ??= new RelayCommandAsync( CSVImport, o => true );

    public ICommand DefineCustomPropertiesCommand => field ??= new RelayCommand( DefineCustomProperties, o => true );

    public bool DisableInGuardzone
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ObservableCollection<IDraggable> Draggables
    {
        get => _draggables;
        set => SetProperty( ref _draggables, value );
    }

    public bool Enabled
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand InsertCommand => field ??= new RelayCommandAsync( Insert, o => true );

    public ICommand InsertConstraintCommand => field ??= new RelayCommand( InsertConstraint, o => SelectedItem != null );

    public ICommand InsertMatchAnyCommand => field ??= new RelayCommand( InsertMatchAny, o => true );

    public ObservableCollectionEx<AutolootEntry> Items
    {
        get => _items;
        set => SetProperty( ref _items, value );
    }

    public bool LootHumanoids
    {
        get => _lootHumanoids;
        set => SetProperty( ref _lootHumanoids, value );
    }

    public bool MatchTextValue
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand MoveToGroupCommand => field ??= new RelayCommand( MoveToGroup, o => SelectedItem != null );

    public ICommand NewGroupCommand => field ??= new RelayCommand( NewGroup, o => true );

    public ICommand RemoveCommand => field ??= new RelayCommandAsync( Remove, o => SelectedItem != null );

    public ICommand RemoveConstraintCommand => field ??= new RelayCommand( RemoveConstraint, o => SelectedProperty != null );

    public ICommand RemoveGroupCommand => field ??= new RelayCommand( RemoveGroup, o => o is IDraggableGroup );

    public ICommand RemoveSingleConstraintCommand =>
        field ??= new RelayCommand( o => RemoveSingleConstraint( ( AutolootConstraintEntry )o ), o => SelectedProperty != null );

    public bool RequeueFailedItems
    {
        get => _requeueFailedItems;
        set => SetProperty( ref _requeueFailedItems, value );
    }

    public ICommand ResetContainerCommand => _resetContainerCommand = new RelayCommand( ResetContainer, o => true );

    public AutolootGroup SelectedGroup
    {
        get => _selectedGroup;
        set => SetProperty( ref _selectedGroup, value );
    }

    public AutolootEntry SelectedItem
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ObservableCollection<AutolootConstraintEntry> SelectedProperties
    {
        get;
        set => SetProperty( ref field, value );
    } = new();

    public AutolootConstraintEntry SelectedProperty
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand SelectHueCommand => field ??= new RelayCommandAsync( SelectHue, o => SelectedItem != null );

    public ICommand SetContainerCommand => field ??= new RelayCommandAsync( SetContainer, o => true );

    public void Serialize( JObject json )
    {
        if ( json == null )
        {
            return;
        }

        JArray groupArray = new();

        foreach ( AutolootGroup draggableGroup in Draggables.Where( i => i is AutolootGroup )
                     .OrderBy( e => DraggableTreeViewHelpers.GetIndex( e, Draggables ) )
                     .Cast<AutolootGroup>() )
        {
            JObject groupEntry = new() { { "Name", draggableGroup.Name }, { "Enabled", draggableGroup.Enabled } };

            groupArray.Add( groupEntry );
        }

        JObject autolootObj = new()
        {
            { "Enabled", Enabled },
            { "DisableInGuardzone", DisableInGuardzone },
            { "Container", ContainerSerial },
            { "RequeueFailedItems", RequeueFailedItems },
            { "LootHumanoids", LootHumanoids },
            { "MatchTextValue", MatchTextValue },
            { "Groups", groupArray }
        };

        JArray itemsArray = new();

        foreach ( AutolootEntry entry in Items.OrderBy( e => DraggableTreeViewHelpers.GetIndex( e, Draggables ) ) )
        {
            JObject entryObj = new()
            {
                { "Name", entry.Name },
                { "ID", entry.ID },
                { "Autoloot", entry.Autoloot },
                { "Rehue", entry.Rehue },
                { "RehueHue", entry.RehueHue },
                { "Enabled", entry.Enabled },
                { "Priority", entry.Priority.ToString() },
                { "Group", entry.Group?.Name }
            };

            if ( entry.Constraints != null )
            {
                JArray constraintsArray = new();

                foreach ( AutolootConstraintEntry constraint in entry.Constraints )
                {
                    JObject constraintObj = new() { { "Name", constraint.Property.Name }, { "Operator", constraint.Operator.ToString() }, { "Value", constraint.Value }, { "Additional", constraint.Additional } };

                    if ( constraint.Values != null && constraint.Values.Count > 0 )
                    {
                        constraintObj.Add( "Values", JArray.FromObject( constraint.Values ) );
                    }

                    constraintsArray.Add( constraintObj );
                }

                entryObj.Add( "Properties", constraintsArray );
            }

            itemsArray.Add( entryObj );
        }

        autolootObj.Add( "Items", itemsArray );

        json.Add( "Autoloot", autolootObj );
    }

    public void Deserialize( JObject json, Options options )
    {
        Items.Clear();
        Draggables.Clear();

        if ( json?["Autoloot"] == null )
        {
            return;
        }

        JToken config = json["Autoloot"];

        Enabled = config["Enabled"]?.ToObject<bool>() ?? true;
        DisableInGuardzone = config["DisableInGuardzone"]?.ToObject<bool>() ?? false;
        ContainerSerial = config["Container"]?.ToObject<int>() ?? 0;
        RequeueFailedItems = config["RequeueFailedItems"]?.ToObject<bool>() ?? false;
        LootHumanoids = config["LootHumanoids"]?.ToObject<bool>() ?? true;
        MatchTextValue = config["MatchTextValue"]?.ToObject<bool>() ?? false;

        if ( config["Groups"] != null )
        {
            JToken groups = config["Groups"];

            foreach ( JToken token in groups )
            {
                AutolootGroup group = new()
                {
                    Name = token["Name"]?.ToObject<string>() ?? "Unknown",
                    Enabled = token["Enabled"]?.ToObject<bool>() ?? false
                };

                Draggables.Add( group );
            }
        }

        if ( config["Items"] != null )
        {
            JToken items = config["Items"];

            foreach ( JToken token in items )
            {
                AutolootEntry entry = new()
                {
                    Name = token["Name"]?.ToObject<string>() ?? "Unknown",
                    ID = token["ID"]?.ToObject<int>() ?? 0,
                    Autoloot = token["Autoloot"]?.ToObject<bool>() ?? false,
                    Rehue = token["Rehue"]?.ToObject<bool>() ?? false,
                    RehueHue = token["RehueHue"]?.ToObject<int>() ?? 0,
                    Enabled = token["Enabled"]?.ToObject<bool>() ?? true,
                    Priority = token["Priority"]?.ToObject<AutolootPriority>() ?? AutolootPriority.Normal
                };

                string groupName = token["Group"]?.ToObject<string>();

                if ( !string.IsNullOrEmpty( groupName ) )
                {
                    AutolootGroup group = (AutolootGroup) Draggables.FirstOrDefault( i =>
                        i is AutolootGroup gr && gr.Name == groupName );

                    if ( group == null )
                    {
                        group = new AutolootGroup { Name = groupName };
                        Draggables.Add( group );
                    }

                    entry.Group = group;
                }

                if ( token["Properties"] != null )
                {
                    List<AutolootConstraintEntry> constraintsList = new();

                    // ReSharper disable once LoopCanBeConvertedToQuery
                    foreach ( JToken constraintToken in token["Properties"] )
                    {
                        string constraintName = constraintToken["Name"]?.ToObject<string>() ?? "Unknown";

                        PropertyEntry propertyEntry = Constraints.FirstOrDefault( c => c.Name == constraintName );

                        if ( propertyEntry == null )
                        {
                            continue;
                        }

                        AutolootConstraintEntry constraintObj = new()
                        {
                            Property = propertyEntry,
                            Operator = constraintToken["Operator"]?.ToObject<AutolootOperator>() ?? AutolootOperator.Equal,
                            Value = constraintToken["Value"]?.ToObject<int>() ?? 0,
                            Additional = constraintToken["Additional"]?.ToString()
                        };

                        if ( constraintToken["Values"] != null )
                        {
                            constraintObj.Values = constraintToken["Values"].ToObject<ObservableCollection<int>>() ?? new ObservableCollection<int>();
                        }

                        constraintsList.Add( constraintObj );
                    }

                    entry.Constraints.AddRange( constraintsList );
                }

                Items.Add( entry );
            }
        }

        if ( SelectedItem != null && !Items.Contains( SelectedItem ) )
        {
            SelectedItem = null;
        }

        if ( SelectedGroup != null && !Draggables.Contains( SelectedGroup ) )
        {
            SelectedGroup = null;
        }
    }

    private void RemoveSingleConstraint( AutolootConstraintEntry obj )
    {
        SelectedItem?.Constraints.Remove( obj );
    }

    private void LoadCustomProperties()
    {
        if ( !File.Exists( _propertiesFileCustom ) )
        {
            return;
        }

        JsonSerializer serializer = new();

        using ( StreamReader sr = new( _propertiesFileCustom ) )
        {
            using ( JsonTextReader reader = new( sr ) )
            {
                PropertyEntry[] constraints = serializer.Deserialize<PropertyEntry[]>( reader );

                foreach ( PropertyEntry constraint in constraints )
                {
                    Constraints.AddSorted( constraint );
                }
            }
        }
    }

    private void LoadProperties()
    {
        JsonSerializer serializer = new();

        using ( StreamReader sr = new( _propertiesFile ) )
        {
            using ( JsonTextReader reader = new( sr ) )
            {
                PropertyEntry[] constraints = serializer.Deserialize<PropertyEntry[]>( reader );

                foreach ( PropertyEntry constraint in constraints )
                {
                    Constraints.AddSorted( constraint );
                }
            }
        }
    }

    private void ClipboardPaste( object obj )
    {
        string text = Engine.UIInvoker.GetClipboardText();

        try
        {
            IEnumerable<AutolootConstraintEntry> entries = JsonConvert.DeserializeObject<IEnumerable<AutolootConstraintEntry>>( text );

            if ( entries == null )
            {
                return;
            }

            foreach ( AutolootConstraintEntry entry in entries )
            {
                if ( !SelectedItem.Constraints.Contains( entry ) )
                {
                    SelectedItem?.Constraints.Add( entry );
                }
            }
        }
        catch ( Exception )
        {
            // ignored
        }
    }

    private static void ClipboardCopy( object obj )
    {
        if ( !( obj is IList<AutolootConstraintEntry> entries ) )
        {
            return;
        }

        string text = JsonConvert.SerializeObject( entries );

        Engine.UIInvoker.SetClipboardText( text );
    }

    public void OnCorpseEvent( int serial, bool force = false )
    {
        if ( !Enabled && !force )
        {
            return;
        }

        lock ( _autolootLock )
        {
            Item item = Engine.Items.GetItem( serial );

            if ( item == null || item.ID != 0x2006 )
            {
                return;
            }

            if ( !LootHumanoids &&
                 TargetManager.GetInstance().BodyData.Where( bd => bd.BodyType == TargetBodyType.Humanoid )
                     .Select( bd => bd.Graphic ).Contains( item.Count ) )
            {
                return;
            }

            PacketWaitEntry we = Engine.PacketWaitEntries.Add( new PacketFilterInfo( 0x3C, new[] { PacketFilterConditions.IntAtPositionCondition( serial, 19 ) } ),
                PacketDirection.Incoming );

            we.Lock.WaitOne( 2000 );

            IEnumerable<Item> items = Engine.Items.GetItem( serial )?.Container?.GetItems();

            if ( items == null )
            {
                return;
            }

            if ( Engine.Features.HasFlag( FeatureFlags.AOS ) )
            {
                Engine.SendPacketToServer( new BatchQueryProperties( items.Select( i => i.Serial ).ToArray() ) );
                Thread.Sleep( 1000 );
            }

            List<Item> lootItems = new();

            // If change logic, also change in DebugAutolootViewModel

            foreach ( AutolootEntry entry in Items.OrderByDescending( x => x.Priority ) )
            {
                if ( !entry.Enabled )
                {
                    continue;
                }

                if ( entry.Group != null && !entry.Group.Enabled )
                {
                    continue;
                }

                IEnumerable<Item> matchItems = AutolootHelpers.AutolootFilter( items, entry );

                if ( matchItems == null )
                {
                    continue;
                }

                foreach ( Item matchItem in matchItems )
                {
                    if ( entry.Rehue )
                    {
                        Engine.SendPacketToClient( new ContainerContentUpdate( matchItem.Serial, matchItem.ID, matchItem.Direction, matchItem.Count, matchItem.X, matchItem.Y,
                            matchItem.Grid, matchItem.Owner, entry.RehueHue ) );
                    }

                    if ( DisableInGuardzone && Engine.Player.GetRegion().Attributes.HasFlag( RegionAttributes.Guarded ) )
                    {
                        continue;
                    }

                    if ( entry.Autoloot )
                    {
                        lootItems.Add( matchItem );
                    }
                }
            }

            foreach ( Item lootItem in lootItems.Distinct() )
            {
                int containerSerial = ContainerSerial;

                if ( containerSerial == 0 || Engine.Items.GetItem( containerSerial ) == null )
                {
                    containerSerial = Engine.Player.GetLayer( Layer.Backpack );
                }

                UOC.SystemMessage( string.Format( Strings.Autolooting___0__, lootItem.Name ), 61 );
                DragDropOptions options = new DragDropOptions
                {
                    CheckRange = true,
                    CheckExisting = true,
                    RequeueFailure = RequeueFailedItems,
                    SuccessPredicate = CheckItemContainer
                };

                Task t = ActionPacketQueue.EnqueueDragDrop( lootItem.Serial, lootItem.Count, containerSerial, QueuePriority.Medium, options: options );

                t.Wait( LOOT_TIMEOUT );
            }
        }
    }

    private static bool CheckItemContainer( int serial, int containerSerial )
    {
        Item item = Engine.Items.GetItem( serial );

        return item == null || item.Owner == containerSerial;
    }

    private void RemoveConstraint( object obj )
    {
        if ( !( obj is IEnumerable<AutolootConstraintEntry> constraints ) )
        {
            return;
        }

        foreach ( AutolootConstraintEntry constraintEntry in constraints.ToList() )
        {
            SelectedItem?.Constraints.Remove( constraintEntry );
        }
    }

    private void InsertConstraint( object obj )
    {
        if ( !( obj is PropertyEntry propertyEntry ) )
        {
            return;
        }

        List<AutolootConstraintEntry> constraints = new( SelectedItem.Constraints ) { new AutolootConstraintEntry { Property = propertyEntry } };

        SelectedItem.Constraints = new ObservableCollection<AutolootConstraintEntry>( constraints );
    }

    private void DefineCustomProperties( object obj )
    {
        Engine.UIInvoker.InvokeDialog<CustomPropertiesViewModel>( "CustomPropertiesWindow" );
        Constraints.Clear();
        LoadProperties();
        LoadCustomProperties();
        AutolootPropertyRegistration.LoadSpecialProperties( Constraints );
    }

    private void ResetContainer( object obj )
    {
        ContainerSerial = 0;
    }

    private async Task SetContainer( object arg )
    {
        int serial = await UOC.GetTargetSerialAsync( Strings.Target_container___ );

        if ( serial == 0 )
        {
            UOC.SystemMessage( Strings.Invalid_or_unknown_object_id );
            return;
        }

        ContainerSerial = serial;
    }

    private void InsertMatchAny( object obj )
    {
        AutolootEntry entry = new() { Name = Strings.Any, ID = -1, Constraints = new ObservableCollection<AutolootConstraintEntry>() };

        Items.Add( entry );
    }

    private async Task Insert( object obj )
    {
        int serial = await UOC.GetTargetSerialAsync( Strings.Target_object___ );

        if ( serial == 0 )
        {
            UOC.SystemMessage( Strings.Invalid_or_unknown_object_id );
            return;
        }

        Item item = Engine.Items.GetItem( serial );

        if ( item == null )
        {
            UOC.SystemMessage( Strings.Cannot_find_item___ );
            return;
        }

        AutolootEntry entry = new() { Name = TileData.GetStaticTile( item.ID ).Name, ID = item.ID, Constraints = new ObservableCollection<AutolootConstraintEntry>() };

        Items.Add( entry );
    }

    private async Task Remove( object arg )
    {
        if ( !( arg is AutolootEntry entry ) )
        {
            return;
        }

        Items.Remove( entry );

        await Task.CompletedTask;
    }

    private static async Task SelectHue( object obj )
    {
        if ( !( obj is AutolootEntry entry ) )
        {
            return;
        }

        int hue = await Engine.UIInvoker.GetHueAsync();

        entry.RehueHue = hue;
    }

    private void NewGroup( object obj )
    {
        int count = Draggables.Count( i => i is IDraggableGroup );

        string name = $"Group-{count + 1}";

        while ( Draggables.Any( e => e is IDraggableGroup && e.Name == name ) )
        {
            name += "-";
        }

        Draggables.Add( new AutolootGroup { Name = name } );
    }

    private void RemoveGroup( object obj )
    {
        if ( !( obj is IDraggableGroup group ) )
        {
            return;
        }

        foreach ( AutolootEntry groupChild in group.Children.Where( i => i is AutolootEntry ).Cast<AutolootEntry>() )
        {
            Draggables.Add( groupChild );

            groupChild.Group = null;
        }

        Draggables.Remove( group );
    }

    private void MoveToGroup( object obj )
    {
        if ( SelectedItem == null || !( obj is AutolootGroup autolootGroup ) )
        {
            return;
        }

        MoveToGroup( SelectedItem, autolootGroup );
    }

    /// <summary>
    ///     Moves an entry into a group, removing it from wherever it currently lives (root
    ///     <see cref="Draggables" /> or another group's <see cref="AutolootGroup.Children" />).
    ///     Used by the context-menu command and by tree drag-and-drop.
    /// </summary>
    public void MoveToGroup( AutolootEntry item, AutolootGroup autolootGroup )
    {
        if ( item == null || autolootGroup == null )
        {
            return;
        }

        int newSelectedIndex = GetNewSelectedIndex( item );

        if ( item.Group != null )
        {
            item.Group.Children.Remove( item );
        }
        else
        {
            Draggables.Remove( item );
        }

        item.Group = autolootGroup;
        autolootGroup.Children.Add( item );

        SetNewSelectedIndex( newSelectedIndex );
    }

    /// <summary>
    ///     Moves an entry out of its group back to the root of <see cref="Draggables" /> (drag-drop
    ///     "ungroup"). No-op for entries already at the root.
    /// </summary>
    public void MoveToRoot( AutolootEntry item )
    {
        if ( item == null || item.Group == null )
        {
            return;
        }

        item.Group.Children.Remove( item );
        Draggables.Add( item );
    }

    private void UpdateDraggables( object sender, NotifyCollectionChangedEventArgs e )
    {
        if ( e.NewItems != null )
        {
            foreach ( object newItem in e.NewItems )
            {
                if ( !( newItem is AutolootEntry autolootEntry ) )
                {
                    continue;
                }

                if ( autolootEntry.Group != null )
                {
                    autolootEntry.Group.Children.Add( autolootEntry );
                }
                else
                {
                    Draggables.Add( autolootEntry );
                }
            }
        }

        if ( e.OldItems == null )
        {
            return;
        }

        foreach ( object oldItem in e.OldItems )
        {
            if ( !( oldItem is AutolootEntry autolootEntry ) )
            {
                continue;
            }

            if ( autolootEntry.Group != null )
            {
                autolootEntry.Group.Children.Remove( autolootEntry );
            }
            else
            {
                Draggables.Remove( autolootEntry );
            }
        }
    }

    private void SetNewSelectedIndex( int newSelectedIndex )
    {
        try
        {
            IDraggable newSelection = Draggables[newSelectedIndex];

            if ( newSelection is AutolootGroup group )
            {
                SelectedGroup = group;
            }
            else
            {
                SelectedItem = newSelection as AutolootEntry;
            }
        }
        catch ( Exception )
        {
            // ignored
        }
    }

    private int GetNewSelectedIndex( AutolootEntry item )
    {
        int newSelectedIndex = 0;

        if ( item.Group == null )
        {
            int previousIndex = Draggables.IndexOf( item );

            if ( previousIndex > 0 )
            {
                newSelectedIndex = previousIndex - 1;
            }
            else if ( previousIndex < Draggables.Count - 1 )
            {
                newSelectedIndex = previousIndex;
            }
            else
            {
                newSelectedIndex = -1;
            }
        }

        return newSelectedIndex;
    }

    private async Task CSVImport( object obj )
    {
        CSVImportViewModel vm = new CSVImportViewModel();

        await Engine.UIInvoker.InvokeDialog( "CSVImportWindow", dataContext: vm );

        if ( !vm.Import )
        {
            return;
        }

        foreach ( AutolootEntry entry in vm.Entries )
        {
            if ( vm.IgnoreDuplicateEntries )
            {
                IEnumerable<AutolootEntry> items = Items.Where( i => i.ID == entry.ID && i.Constraints.Count == entry.Constraints.Count ).ToList();

                if ( items.Any() )
                {
                    bool exclude = ( from item in items
                            select item.Constraints.All( constraint =>
                                entry.Constraints.Any( e =>
                                    e.Property.Name == constraint.Property.Name && e.Operator == constraint.Operator &&
                                    e.Value == constraint.Value ) ) )
                        .Any( allMatch => allMatch );

                    if ( exclude )
                    {
                        continue;
                    }
                }
            }

            Items.Add( entry );
        }
    }
}