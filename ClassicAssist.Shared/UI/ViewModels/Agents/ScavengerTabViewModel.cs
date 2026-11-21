using System;
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

    // Upper bound on waiting for a single drag/drop to complete. A drag normally finishes in one
    // action delay (Options.ActionDelayMS, 900ms by default) plus DROP_DELAY (50ms) and two packet
    // sends, so 5000ms leaves room for a handful of other queued actions (autoloot etc.) to drain
    // ahead of it first. CheckArea always runs on a pool thread now (see ItemsOnCollectionChanged),
    // but the wait stays bounded so a stuck queue can't park that thread forever. On timeout the
    // item stays claimed, so its still-queued drag runs at most once later.
    private const int SCAVENGE_DRAG_DROP_TIMEOUT_MS = 5000;

    // How long a ground item must stay put before it is scavenged. Some servers put an item on the
    // ground for an instant on its way somewhere else - a quest reward is sent as a world item (0xF3)
    // at the player's feet and deleted (0x1D) a few milliseconds later, once it has been merged into
    // the backpack's gold. Without the wait an idle action queue sends the drag before the delete
    // has been processed.
    private const int SCAVENGE_SETTLE_MS = 250;

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

    /// <summary>
    ///     Guarded like <c>OptionsTabViewModel.Toggle*</c>: this is an async void command handler,
    ///     so anything the dialog throws after the first await would escape to the dispatcher
    ///     unhandled. Failures are reported to the user (see
    ///     <see cref="CommandExceptionPolicy.Report" />); fatal faults still propagate.
    /// </summary>
    private async void OpenClilocFilter( object obj )
    {
        try
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
        catch ( Exception e ) when ( !CommandExceptionPolicy.IsFatal( e ) )
        {
            CommandExceptionPolicy.Report( e );
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
            // Never on the calling thread: this is raised from the packet handlers, and CheckArea
            // waits - for the item to settle, then for each drag. While the packet thread is parked
            // here it can't process the delete or move that shows the item has already gone, so the
            // scavenger would act on stale state by construction.
            Task.Run( CheckArea );
        }
    }

    /// <summary>
    ///     Whether <paramref name="serial" /> is still a ground item within scavenging range. Read from
    ///     the item store each time, never from the <see cref="Item" /> a pass started with, because
    ///     the point is to notice that it has since been deleted or moved into a container.
    /// </summary>
    private static bool IsOnGroundNearby( int serial )
    {
        return Engine.Items.GetItem( serial ) is { Owner: 0 } item && item.Distance <= SCAVENGER_DISTANCE;
    }

    /// <summary>
    ///     Final check, made by the action queue when it reaches the drag. If the item has gone, the
    ///     drag is skipped without a message and the claim released, so an item that only moved out of
    ///     range can be scavenged again when the player comes back to it.
    /// </summary>
    private bool CanStillScavenge( int serial )
    {
        if ( IsOnGroundNearby( serial ) )
        {
            return true;
        }

        lock ( _scavengeLock )
        {
            _ignoreList.Remove( serial );
        }

        return false;
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

        // Let anything that was only passing through the ground go first - see SCAVENGE_SETTLE_MS.
        Thread.Sleep( SCAVENGE_SETTLE_MS );

        foreach ( Item scavengerItem in scavengerItems.Distinct() )
        {
            int serial = scavengerItem.Serial;

            if ( !IsOnGroundNearby( serial ) )
            {
                continue;
            }

            lock ( _scavengeLock )
            {
                // Claim the serial under the lock so overlapping passes can never enqueue a second
                // drag for the same item - each item is scavenged at most once, and the drag/drop
                // actions themselves still run one at a time on ActionPacketQueue's single worker.
                if ( _ignoreList.Contains( serial ) )
                {
                    continue;
                }

                _ignoreList.Add( serial );
            }

            DragDropOptions options = new()
            {
                CheckRange = true,
                // The queue can hold the drag for a while (action delay, other agents' drags), so
                // check again when it is reached rather than trusting what the pass saw.
                CanPerformAction = () => CanStillScavenge( serial ),
                // Announced only when the drag is actually sent, not when it is queued.
                BeforeDragDrop = item => UOC.SystemMessage( string.Format( Strings.Scavenging___0__, item.Name ?? "Unknown" ), 61 )
            };

            Task<bool> scavengeTask = ActionPacketQueue.EnqueueDragDrop( serial, scavengerItem.Count, container.Serial, QueuePriority.Low,
                options: options );

            // _scavengeLock is deliberately NOT held across this wait - waiting under the lock would
            // park every other CheckArea pass for the whole drag. The wait is bounded per
            // SCAVENGE_DRAG_DROP_TIMEOUT_MS above; it keeps the scavenge drags of one pass strictly
            // one-at-a-time as before.
            scavengeTask.Wait( SCAVENGE_DRAG_DROP_TIMEOUT_MS );
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