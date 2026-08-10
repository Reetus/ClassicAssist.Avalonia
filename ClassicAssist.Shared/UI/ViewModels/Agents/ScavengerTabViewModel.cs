using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Scavenger;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json.Linq;
using UOC = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.Shared.UI.ViewModels.Agents;

public class ScavengerTabViewModel : BaseViewModel, ISettingProvider
{
    private const int SCAVENGER_DISTANCE = 2;
    private readonly List<int> _ignoreList;
    private readonly Lock _scavengeLock = new();

    public ScavengerTabViewModel()
    {
        ScavengerManager manager = ScavengerManager.GetInstance();
        manager.Items = Items;
        manager.CheckArea = () => Task.Run( CheckArea );
        manager.IsEnabled = () => Enabled;
        manager.SetEnabled = enabled => Enabled = enabled;
        _ignoreList = [];
    }

    public bool CheckWeight
    {
        get;
        set => SetProperty( ref field, value );
    } = true;

    public ICommand ClearAllCommand => field ??= new RelayCommandAsync( ClearAll, o => true );

    public int ContainerSerial
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool Enabled
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool FilterEnabled
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ObservableCollection<ScavengerClilocFilterEntry> Filters
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ICommand InsertCommand => field ??= new RelayCommandAsync( Insert, o => true );

    public ObservableCollection<ScavengerEntry> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public int MinWeightAvailable
    {
        get;
        set => SetProperty( ref field, value );
    } = 25;

    public ICommand OpenClilocFilterCommand => field ??= new RelayCommand( OpenClilocFilter );

    public ICommand RemoveCommand => field ??= new RelayCommandAsync( Remove, o => SelectedItem != null );

    public ScavengerEntry SelectedItem
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand SetContainerCommand => field ??= new RelayCommandAsync( SetContainer, o => true );

    public void Serialize( JObject json )
    {
        Engine.Items.CollectionChanged -= ItemsOnCollectionChanged;

        if ( json == null )
        {
            return;
        }

        JObject scavengerObj = new()
        {
            { "Enabled", Enabled },
            { "Container", ContainerSerial },
            { "CheckWeight", CheckWeight },
            { "MinWeightAvailable", MinWeightAvailable },
            { "FilterEnabled", FilterEnabled }
        };

        JArray itemsArray = [];

        foreach ( ScavengerEntry entry in Items )
        {
            itemsArray.Add( new JObject
            {
                { "Graphic", entry.Graphic },
                { "Name", entry.Name },
                { "Hue", entry.Hue },
                { "Enabled", entry.Enabled },
                { "Priority", entry.Priority.ToString() }
            } );
        }

        scavengerObj.Add( "Items", itemsArray );

        JArray filtersArray = [];

        foreach ( ScavengerClilocFilterEntry filter in Filters )
        {
            filtersArray.Add( new JObject { { "Enabled", filter.Enabled }, { "Cliloc", filter.Cliloc } } );
        }

        scavengerObj.Add( "Filters", filtersArray );

        json.Add( "Scavenger", scavengerObj );
    }

    public void Deserialize( JObject json, Options options )
    {
        Engine.Items.CollectionChanged += ItemsOnCollectionChanged;
        Items.Clear();

        if ( json?["Scavenger"] == null )
        {
            return;
        }

        JToken config = json["Scavenger"];

        Enabled = config["Enabled"]?.ToObject<bool>() ?? true;
        ContainerSerial = config["Container"]?.ToObject<int>() ?? 0;
        CheckWeight = config["CheckWeight"]?.ToObject<bool>() ?? true;
        MinWeightAvailable = config["MinWeightAvailable"]?.ToObject<int>() ?? 25;
        FilterEnabled = config["FilterEnabled"]?.ToObject<bool>() ?? false;

        if ( config["Items"] != null )
        {
            foreach ( JToken token in config["Items"] )
            {
                ScavengerEntry entry = new()
                {
                    Graphic = token["Graphic"]?.ToObject<int>() ?? 0,
                    Name = token["Name"]?.ToObject<string>() ?? "Unknown",
                    Hue = token["Hue"]?.ToObject<int>() ?? 0,
                    Enabled = token["Enabled"]?.ToObject<bool>() ?? true,
                    Priority = token["Priority"]?.ToObject<ScavengerPriority>() ?? ScavengerPriority.Normal
                };

                bool alreadyExists = Items.Any( s => s.Graphic == entry.Graphic && s.Hue == entry.Hue );

                if ( !alreadyExists )
                {
                    Items.Add( entry );
                }
            }
        }

        Filters.Clear();

        if ( config["Filters"] != null )
        {
            foreach ( JToken token in config["Filters"] )
            {
                Filters.Add( new ScavengerClilocFilterEntry
                {
                    Enabled = token["Enabled"]?.ToObject<bool>() ?? false,
                    Cliloc = token["Cliloc"]?.ToObject<int>() ?? 0
                } );
            }
        }
    }

    private async void OpenClilocFilter( object obj )
    {
        ScavengerClilocFilterViewModel vm = new( FilterEnabled, Filters );

        await Engine.UIInvoker.InvokeDialog( "ScavengerClilocFilterWindow", dataContext: vm );

        if ( !vm.Result )
        {
            return;
        }

        FilterEnabled = vm.Enabled;
        Filters.Clear();

        foreach ( ScavengerClilocFilterEntry entry in vm.Items )
        {
            Filters.Add( entry );
        }
    }

    private void ItemsOnCollectionChanged( int totalcount, bool added, Item[] items )
    {
        if ( !added || !Enabled )
        {
            return;
        }

        bool hasNearby = items.Any( i => i.Distance <= SCAVENGER_DISTANCE );

        if ( hasNearby )
        {
            CheckArea();
        }
    }

    public void CheckArea()
    {
        if ( !Enabled || Engine.Player == null )
        {
            return;
        }

        List<Item> scavengerItems = [];

        if ( CheckWeight && Engine.Player.WeightMax - Engine.Player.Weight <= MinWeightAvailable )
        {
            return;
        }

        lock ( _scavengeLock )
        {
            foreach ( ScavengerEntry entry in Items.OrderByDescending( x => x.Priority ) )
            {
                if ( !entry.Enabled )
                {
                    continue;
                }

                Item[] matches = Engine.Items.SelectEntities( i =>
                    i.Distance <= SCAVENGER_DISTANCE && i.Owner == 0 && i.ID == entry.Graphic &&
                    ( entry.Hue == -1 || i.Hue == entry.Hue ) && !_ignoreList.Contains( i.Serial ) &&
                    ( !FilterEnabled || !i.Properties.Any( e => Filters.Select( f => f.Cliloc ).Contains( e.Cliloc ) ) ) );

                if ( matches == null )
                {
                    continue;
                }

                scavengerItems.AddRange( matches );
            }

            if ( scavengerItems.Count == 0 )
            {
                return;
            }

            Item container = Engine.Items.GetItem( ContainerSerial ) ?? Engine.Player.Backpack;

            if ( container == null )
            {
                return;
            }

            foreach ( Item scavengerItem in scavengerItems.Where( scavengerItem =>
                scavengerItem.Distance <= SCAVENGER_DISTANCE ).Distinct() )
            {
                _ignoreList.Add( scavengerItem.Serial );
                UOC.SystemMessage( string.Format( Strings.Scavenging___0__, scavengerItem.Name ?? "Unknown" ), 61 );
                ActionPacketQueue.EnqueueDragDrop( scavengerItem.Serial, scavengerItem.Count, container.Serial,
                    QueuePriority.Low, options: new DragDropOptions { CheckRange = true } ).Wait();
            }
        }
    }

    private async Task Insert( object arg )
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

        string tiledataName = TileData.GetStaticTile( item.ID ).Name ?? "Unknown";

        ScavengerEntry entry =
            new()
            { Enabled = true, Graphic = item.ID, Hue = item.Hue, Name = tiledataName };

        Items.Add( entry );
    }

    private async Task Remove( object arg )
    {
        if ( arg is not ScavengerEntry entry )
        {
            return;
        }

        Items.Remove( entry );

        await Task.CompletedTask;
    }

    private async Task ClearAll( object obj )
    {
        Items.Clear();

        await Task.CompletedTask;
    }

    private async Task SetContainer( object arg )
    {
        int serial = await UOC.GetTargetSerialAsync( Strings.Select_destination_container___ );

        if ( serial == 0 )
        {
            UOC.SystemMessage( Strings.Invalid_container___ );
            return;
        }

        ContainerSerial = serial;
    }
}