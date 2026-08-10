using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Dress;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Hotkeys.Commands;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Shared.UI.ViewModels.Agents;

public class DressTabViewModel : HotkeyEntryViewModel<DressAgentEntry>, ISettingProvider
{
    private readonly DressManager _manager;

    private readonly Layer[] _validLayers =
    [
        Layer.Arms, Layer.Bracelet, Layer.Cloak, Layer.Earrings, Layer.Gloves, Layer.Helm, Layer.InnerLegs,
        Layer.InnerTorso, Layer.MiddleTorso, Layer.Neck, Layer.OneHanded, Layer.OuterLegs, Layer.OuterTorso,
        Layer.Pants, Layer.Ring, Layer.Shirt, Layer.Shoes, Layer.Talisman, Layer.TwoHanded, Layer.Waist
    ];

    public DressTabViewModel() : base( Strings.Dress )
    {
        _manager = DressManager.GetInstance();

        _manager.Items = Items;

        HotkeyCommand stopHotkey = new()
        {
            Name = Strings.Stop_Dress,
            Action = ( entry, objects ) => _manager.Stop(),
            CanGlobal = false
        };

        _staticOptions.Add( stopHotkey );
    }

    public ICommand AddDressItemCommand => field ??= new RelayCommandAsync( AddDressItem, o => true );

    //TODO UI
    public ICommand ChangeDressTypeCommand => field ??= new RelayCommand( ChangeDressType, o => SelectedDressItem != null );

    public ICommand ClearDressItemsCommand => field ??=
            new RelayCommand( ClearDressItems, o => SelectedItem != null );

    public ICommand DressAllItemsCommand => field ??=
            new RelayCommandAsync( DressAllItems,
                o => SelectedItem != null && !IsUndressing && !IsUndressingAll );

    public ICommand ImportItemsCommand => field ??=
            new RelayCommand( ImportItems, o => SelectedItem != null );

    public bool IsDressing
    {
        get;
        set
        {
            SetProperty( ref field, value );
            OnPropertyChanged( nameof( IsDressingOrUndressing ) );
        }
    }

    public bool IsDressingOrUndressing => IsDressing || IsUndressing || IsUndressingAll;

    public bool IsUndressing
    {
        get;
        set
        {
            SetProperty( ref field, value );
            OnPropertyChanged( nameof( IsDressingOrUndressing ) );
        }
    }

    public bool IsUndressingAll
    {
        get;
        set
        {
            SetProperty( ref field, value );
            OnPropertyChanged( nameof( IsDressingOrUndressing ) );
        }
    }

    public bool MoveConflictingItems
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand NewDressEntryCommand => field ??= new RelayCommand( NewDressEntry, o => true );

    public ICommand RemoveDressEntryCommand => field ??=
            new RelayCommand( RemoveDressEntry, o => SelectedItem != null );

    public ICommand RemoveDressItemCommand => field ??=
            new RelayCommand( RemoveDressItem, o => SelectedDressItem != null );

    public DressAgentItem SelectedDressItem
    {
        get;
        set => SetProperty( ref field, value );
    }

    public DressAgentEntry SelectedItem
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand SetUndressContainerCommand => field ??=
            new RelayCommandAsync( SetUndressContainer, o => SelectedItem != null );

    public ICommand UndressAllItemsCommand => field ??=
            new RelayCommandAsync( UndressAllItems, o => !IsDressing && !IsUndressing );

    public ICommand UndressItemsCommand => field ??=
            new RelayCommandAsync( UndressItems, o => !IsDressing && !IsUndressingAll );

    public bool UseUO3DPackets
    {
        get;
        set
        {
            SetProperty( ref field, value );

            _manager?.UseUO3DPackets = value;
        }
    }

    public void Serialize( JObject json )
    {
        JObject dress = new()
        {
            {
                "Options",
                new JObject { ["MoveConflictingItems"] = MoveConflictingItems, ["UseUO3DPackets"] = UseUO3DPackets }
            }
        };

        SerializeStatic( dress );

        JArray dressEntries = [];

        foreach ( DressAgentEntry dae in Items )
        {
            JObject djson = [];

            SetJsonValue( djson, "Name", dae.Name );
            SetJsonValue( djson, "UndressContainer", dae.UndressContainer );
            SetJsonValue( djson, "PassToUO", dae.PassToUO );
            SetJsonValue( djson, "Keys", dae.Hotkey.ToJObject() );

            JArray items = [];

            if ( dae.Items != null )
            {
                foreach ( DressAgentItem dressAgentItem in dae.Items )
                {
                    JObject item = new()
                    {
                        { "Layer", (int) dressAgentItem.Layer },
                        { "Serial", dressAgentItem.Serial },
                        { "ID", dressAgentItem.ID },
                        { "Type", (int) dressAgentItem.Type }
                    };

                    items.Add( item );
                }
            }

            djson.Add( "Items", items );
            dressEntries.Add( djson );
        }

        dress.Add( "Entries", dressEntries );
        json?.Add( "Dress", dress );
    }

    public void Deserialize( JObject json, Options options )
    {
        Items.Clear();

        if ( json?["Dress"] == null )
        {
            return;
        }

        JToken dress = json["Dress"];

        DeserializeStatic( dress as JObject );

        MoveConflictingItems = GetJsonValue( dress["Options"], "MoveConflictingItems", false );
        UseUO3DPackets = _manager.UseUO3DPackets = GetJsonValue( dress["Options"], "UseUO3DPackets", false );

        foreach ( JToken entry in dress["Entries"] )
        {
            DressAgentEntry dae = new()
            {
                Name = GetJsonValue( entry, "Name", string.Empty ),
                UndressContainer = GetJsonValue( entry, "UndressContainer", 0 ),
                PassToUO = GetJsonValue( entry, "PassToUO", true ),
                Hotkey = new ShortcutKeys( entry["Keys"] )
            };

            dae.Action = async ( hks, _ ) => await DressAllItems( dae );

            List<DressAgentItem> items = [];

            if ( entry["Items"] != null )
            {
                items.AddRange( entry["Items"].Select( itemEntry => new DressAgentItem
                {
                    Layer = GetJsonValue( itemEntry, "Layer", Layer.Invalid ),
                    Serial = GetJsonValue( itemEntry, "Serial", 0 ),
                    ID = GetJsonValue( itemEntry, "ID", -1 ),
                    Type = GetJsonValue( itemEntry, "Type", DressAgentItemType.Serial )
                } ) );
            }

            dae.Items = [.. items];

            Items.Add( dae );
        }
    }

    private async Task UndressItems( object arg )
    {
        if ( arg is not DressAgentEntry dae )
        {
            return;
        }

        if ( IsUndressing )
        {
            _manager.Stop();
            return;
        }

        try
        {
            IsUndressing = true;

            await _manager.Undress( dae );
        }
        finally
        {
            IsUndressing = false;
        }
    }

    private static async Task SetUndressContainer( object obj )
    {
        if ( obj is not DressAgentEntry entry )
        {
            return;
        }

        int serial = await Commands.GetTargetSerialAsync( Strings.Select_undress_container___ );

        if ( serial <= 0 )
        {
            Commands.SystemMessage( Strings.Invalid_container___ );
            return;
        }

        entry.UndressContainer = serial;
    }

    private static void ClearDressItems( object obj )
    {
        if ( obj is not DressAgentEntry dae )
        {
            return;
        }

        dae.Items = [];
    }

    private void RemoveDressItem( object obj )
    {
        if ( obj is not DressAgentItem removeItem )
        {
            return;
        }

        if ( !SelectedItem.Items.Contains( removeItem ) )
        {
            return;
        }

        List<DressAgentItem> list = [.. SelectedItem.Items];
        list.Remove( removeItem );
        SelectedItem.Items = list;
    }

    private static async Task AddDressItem( object arg )
    {
        if ( arg is not DressAgentEntry dae )
        {
            return;
        }

        int serial = await Commands.GetTargetSerialAsync( Strings.Target_clothing_item___ );

        Item item = Engine.Items.GetItem( serial );

        if ( item == null )
        {
            Commands.SystemMessage( Strings.Cannot_find_item___ );
            return;
        }

        if ( item.Layer == Layer.Invalid )
        {
            Commands.SystemMessage( Strings.The_item_needs_to_be_equipped___ );
            return;
        }

        dae.AddOrReplaceDressItem( item );
    }

    private async Task UndressAllItems( object obj )
    {
        if ( IsUndressingAll )
        {
            _manager.Stop();
            return;
        }

        try
        {
            IsUndressingAll = true;
            await _manager.UndressAll( CancellationToken.None );
        }
        finally
        {
            IsUndressingAll = false;
        }
    }

    private void NewDressEntry( object obj )
    {
        int count = Items.Count;

        DressAgentEntry dae =
            new()
            { Name = $"Dress-{count + 1}", Items = [] };

        dae.Action = async ( hks, _ ) => await DressAllItems( dae );

        Items.Add( dae );
    }

    private void RemoveDressEntry( object obj )
    {
        if ( obj is not DressAgentEntry dae )
        {
            return;
        }

        dae.Hotkey = ShortcutKeys.Default;
        Items.Remove( dae );
    }

    private async Task DressAllItems( object obj )
    {
        if ( obj is not DressAgentEntry dae )
        {
            return;
        }

        if ( IsDressing )
        {
            _manager.Stop();
            return;
        }

        try
        {
            IsDressing = true;
            await _manager.DressAllItems( dae, MoveConflictingItems );
        }
        finally
        {
            IsDressing = false;
        }
    }

    private void ImportItems( object obj )
    {
        if ( obj is not DressAgentEntry dae )
        {
            return;
        }

        _manager.ImportItems( dae );
    }

    public bool IsValidLayer( Layer layer )
    {
        return _validLayers.Any( l => l == layer );
    }

    private static void ChangeDressType( object obj )
    {
        if ( obj is not DressAgentItem dai )
        {
            return;
        }

        dai.Type = dai.Type == DressAgentItemType.Serial ? DressAgentItemType.ID : DressAgentItemType.Serial;
    }
}