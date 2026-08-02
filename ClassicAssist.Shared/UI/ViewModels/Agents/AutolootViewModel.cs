using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Data.Regions;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UI.ViewModels.Autoloot;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UI.Misc;
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

    private RelayCommand _resetContainerCommand;

    public AutolootViewModel()
    {
        if ( !File.Exists( _propertiesFile ) )
        {
            return;
        }

        LoadProperties();
        LoadCustomProperties();

        AutolootHelpers.SetAutolootContainer = serial => ContainerSerial = serial;
        IncomingPacketHandlers.CorpseContainerDisplayEvent += OnCorpseEvent;
        AutolootManager manager = AutolootManager.GetInstance();
        manager.GetEntries = () => _items.ToList();
        manager.CheckContainer = OnCorpseEvent;
        manager.IsEnabled = () => Enabled;
        manager.SetEnabled = enabled => Enabled = enabled;
        manager.IsRunning = () => false;
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

    public ICommand DefineCustomPropertiesCommand => field ??= new RelayCommand( DefineCustomProperties, o => true );

    public bool DisableInGuardzone
    {
        get;
        set => SetProperty( ref field, value );
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

    public ICommand RemoveCommand => field ??= new RelayCommandAsync( Remove, o => SelectedItem != null );

    public ICommand RemoveConstraintCommand => field ??= new RelayCommand( RemoveConstraint, o => SelectedProperty != null );

    public ICommand RemoveSingleConstraintCommand =>
        field ??= new RelayCommand( o => RemoveSingleConstraint( ( AutolootConstraintEntry )o ), o => SelectedProperty != null );

    public ICommand ResetContainerCommand => _resetContainerCommand = new RelayCommand( ResetContainer, o => true );

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

        JObject autolootObj = new() { { "Enabled", Enabled }, { "DisableInGuardzone", DisableInGuardzone }, { "Container", ContainerSerial } };

        JArray itemsArray = new();

        foreach ( AutolootEntry entry in Items )
        {
            JObject entryObj = new()
            {
                { "Name", entry.Name },
                { "ID", entry.ID },
                { "Autoloot", entry.Autoloot },
                { "Rehue", entry.Rehue },
                { "RehueHue", entry.RehueHue },
                { "Enabled", entry.Enabled }
            };

            if ( entry.Constraints != null )
            {
                JArray constraintsArray = new();

                foreach ( AutolootConstraintEntry constraint in entry.Constraints )
                {
                    JObject constraintObj = new() { { "Name", constraint.Property.Name }, { "Operator", constraint.Operator.ToString() }, { "Value", constraint.Value } };

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

        if ( json?["Autoloot"] == null )
        {
            return;
        }

        JToken config = json["Autoloot"];

        Enabled = config["Enabled"]?.ToObject<bool>() ?? true;
        DisableInGuardzone = config["DisableInGuardzone"]?.ToObject<bool>() ?? false;
        ContainerSerial = config["Container"]?.ToObject<int>() ?? 0;

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
                    Enabled = token["Enabled"]?.ToObject<bool>() ?? true
                };

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
                            Value = constraintToken["Value"]?.ToObject<int>() ?? 0
                        };

                        constraintsList.Add( constraintObj );
                    }

                    entry.Constraints.AddRange( constraintsList );
                }

                Items.Add( entry );
            }
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

            foreach ( AutolootEntry entry in Items )
            {
                if ( !entry.Enabled )
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
                Task t = ActionPacketQueue.EnqueueDragDrop( lootItem.Serial, lootItem.Count, containerSerial, QueuePriority.Medium, true, true );

                t.Wait( LOOT_TIMEOUT );
            }
        }
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
}