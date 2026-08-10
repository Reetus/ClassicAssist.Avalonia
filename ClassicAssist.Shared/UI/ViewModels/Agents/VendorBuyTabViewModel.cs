using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Vendors;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json.Linq;
using UOC = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.Shared.UI.ViewModels.Agents;

public class VendorBuyTabViewModel : BaseViewModel, ISettingProvider
{
    public VendorBuyTabViewModel()
    {
        IncomingPacketHandlers.VendorBuyDisplayEvent += OnVendorBuyDisplayEvent;

        VendorBuyManager manager = VendorBuyManager.GetInstance();
        manager.Items = Items;
    }

    public bool AutoDisableOnLogin
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool CheckItemCount
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool CheckWeight
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool Enabled
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool IncludeBackpackAmount
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool IncludePurchasedAmount
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand InsertCommand => field ??= new RelayCommand( Insert, o => true );

    public ICommand InsertEntryCommand => field ??= new RelayCommandAsync( InsertEntry, o => Engine.Connected && SelectedItem != null );

    public ObservableCollection<VendorBuyAgentEntry> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public int MinItemsAvailable
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int MinWeightAvailable
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand RemoveCommand => field ??= new RelayCommand( Remove, o => SelectedItem != null );

    public ICommand RemoveEntryCommand => field ??= new RelayCommand( RemoveEntry, o => SelectedEntry != null );

    public VendorBuyAgentItem SelectedEntry
    {
        get;
        set => SetProperty( ref field, value );
    }

    public VendorBuyAgentEntry SelectedItem
    {
        get;
        set => SetProperty( ref field, value );
    }

    public void Serialize( JObject json )
    {
        if ( json == null )
        {
            return;
        }

        JObject vendorBuy = [];

        JArray items = [];

        vendorBuy.Add( "AutoDisableOnLogin", AutoDisableOnLogin );
        vendorBuy.Add( "CheckWeight", CheckWeight );
        vendorBuy.Add( "MinWeightAvailable", MinWeightAvailable );
        vendorBuy.Add( "CheckItemCount", CheckItemCount );
        vendorBuy.Add( "MinItemsAvailable", MinItemsAvailable );

        foreach ( VendorBuyAgentEntry entry in Items )
        {
            JObject config = new() { { "Name", entry.Name }, { "Enabled", entry.Enabled }, { "IncludeBackpackAmount", entry.IncludeBackpackAmount } };

            JArray itemObj = [];

            foreach ( VendorBuyAgentItem item in entry.Items )
            {
                JObject entryObj = new()
                {
                    { "Enabled", item.Enabled },
                    { "Name", item.Name },
                    { "Graphic", item.Graphic },
                    { "Hue", item.Hue },
                    { "Amount", item.Amount },
                    { "MaxPrice", item.MaxPrice },
                    { "BackpackGraphic", item.BackpackGraphic },
                    { "Weight", item.Weight },
                    { "Stackable", item.Stackable }
                };

                itemObj.Add( entryObj );
            }

            config.Add( "Items", itemObj );

            items.Add( config );
        }

        vendorBuy.Add( "Entries", items );
        json.Add( "VendorBuy", vendorBuy );
    }

    private static async Task InsertEntry( object arg )
    {
        if ( arg is not VendorBuyAgentEntry entry )
        {
            return;
        }

        int serial = await UOC.GetTargetSerialAsync( Strings.Target_object___ );

        if ( serial == 0 )
        {
            UOC.SystemMessage( Strings.Invalid_or_unknown_object_id, true );
            return;
        }

        Item item = Engine.Items.GetItem( serial );

        if ( item == null )
        {
            UOC.SystemMessage( Strings.Cannot_find_item___ );
            return;
        }

        string name = TileData.GetStaticTile( item.ID ).Name ?? item.Name;
        double weight = 0;

        if ( Engine.CharacterListFlags.HasFlag( CharacterListFlags.PaladinNecromancerClassTooltips ) && item.Properties != null )
        {
            //1072788 - Weight: ~1_WEIGHT~ stone
            //1072789 - Weight: ~1_WEIGHT~ stones
            Property weightProperty = item.Properties.FirstOrDefault( p => p.Cliloc is 1072788 or 1072789 );

            if ( weightProperty != null && weightProperty.Arguments.Length > 0 )
            {
                weight = Math.Round( double.Parse( weightProperty.Arguments[0] ) / item.Count, 2 );
            }
        }

        StaticTile staticTile = TileData.GetStaticTile( item.ID );

        bool stackable = staticTile.Flags.HasFlag( TileFlags.Stackable );

        entry.Items.Add( new VendorBuyAgentItem
        {
            Enabled = true,
            Name = name,
            Graphic = item.ID,
            Amount = -1,
            Hue = item.Hue,
            MaxPrice = -1,
            BackpackGraphic = item.ID,
            Weight = weight,
            Stackable = stackable
        } );
    }

    public void Deserialize( JObject json, Options options )
    {
        Items.Clear();

        JToken config = json?["VendorBuy"];

        if ( config == null )
        {
            return;
        }

        AutoDisableOnLogin = config["AutoDisableOnLogin"]?.ToObject<bool>() ?? false;
        CheckWeight = config["CheckWeight"]?.ToObject<bool>() ?? false;
        MinWeightAvailable = config["MinWeightAvailable"]?.ToObject<int>() ?? 0;
        CheckItemCount = config["CheckItemCount"]?.ToObject<bool>() ?? false;
        MinItemsAvailable = config["MinItemsAvailable"]?.ToObject<int>() ?? 0;

        if ( config["Items"] != null )
        {
            // Convert from Legacy "Items" to "Entries"
            VendorBuyAgentEntry entry = new()
            {
                Name = "Legacy",
                Enabled = config["Enabled"]?.ToObject<bool>() ?? false,
                IncludeBackpackAmount = config["IncludeBackpackAmount"]?.ToObject<bool>() ?? false
            };

            foreach ( JToken token in config["Items"] )
            {
                VendorBuyAgentItem vbae = new()
                {
                    Enabled = token["Enabled"]?.ToObject<bool>() ?? false,
                    Name = token["Name"]?.ToObject<string>() ?? "Unknown",
                    Graphic = token["Graphic"]?.ToObject<int>() ?? 0,
                    Hue = token["Hue"]?.ToObject<int>() ?? 0,
                    Amount = token["Amount"]?.ToObject<int>() ?? 0,
                    MaxPrice = token["MaxPrice"]?.ToObject<int>() ?? 0,
                    BackpackGraphic = token["BackpackGraphic"]?.ToObject<int>() ?? 0,
                    Weight = token["Weight"]?.ToObject<double>() ?? 0,
                    Stackable = token["Stackable"]?.ToObject<bool>() ?? false
                };

                if ( vbae.BackpackGraphic == 0 )
                {
                    vbae.BackpackGraphic = vbae.Graphic;
                }

                entry.Items.Add( vbae );
            }

            Items.Add( entry );
        }

        if ( config["Entries"] == null )
        {
            return;
        }

        foreach ( JToken token in config["Entries"] )
        {
            VendorBuyAgentEntry entry = new()
            {
                Name = token["Name"]?.ToObject<string>() ?? "Unknown",
                Enabled = !AutoDisableOnLogin && ( token["Enabled"]?.ToObject<bool>() ?? false ),
                IncludeBackpackAmount = token["IncludeBackpackAmount"]?.ToObject<bool>() ?? false
            };

            if ( token["Items"] != null )
            {
                foreach ( JToken item in token["Items"] )
                {
                    VendorBuyAgentItem vbae = new()
                    {
                        Enabled = item["Enabled"]?.ToObject<bool>() ?? false,
                        Name = item["Name"]?.ToObject<string>() ?? "Unknown",
                        Graphic = item["Graphic"]?.ToObject<int>() ?? 0,
                        Hue = item["Hue"]?.ToObject<int>() ?? 0,
                        Amount = item["Amount"]?.ToObject<int>() ?? 0,
                        MaxPrice = item["MaxPrice"]?.ToObject<int>() ?? 0,
                        BackpackGraphic = item["BackpackGraphic"]?.ToObject<int>() ?? 0,
                        Weight = item["Weight"]?.ToObject<double>() ?? 0,
                        Stackable = item["Stackable"]?.ToObject<bool>() ?? false
                    };

                    if ( vbae.BackpackGraphic == 0 )
                    {
                        vbae.BackpackGraphic = vbae.Graphic;
                    }

                    entry.Items.Add( vbae );
                }
            }

            Items.Add( entry );
        }
    }

    private void OnVendorBuyDisplayEvent( int serial, ShopListEntry[] entries )
    {
        if ( CheckWeight && Engine.Player.WeightMax - Engine.Player.Weight < MinWeightAvailable )
        {
            UOC.SystemMessage( Strings.Buy_Agent__Not_enough_weight_available___, false );
            return;
        }

        if ( CheckItemCount && Engine.TooltipsEnabled )
        {
            (int count, int max) = GetBackpackItemCount();

            if ( max - count < MinItemsAvailable )
            {
                UOC.SystemMessage( Strings.Buy_Agent__Not_enough_backpack_space_available___, false );
                return;
            }
        }

        List<ShopListEntry> buyList = [];

        int purchasedWeight = 0;

        foreach ( VendorBuyAgentEntry entry in Items.Where( e => e.Enabled ) )
        {
            foreach ( VendorBuyAgentItem item in entry.Items.Where( e => e.Enabled ) )
            {
                ShopListEntry[] matches = [.. entries.Where( i =>
                    i.Item.ID == item.Graphic && ( item.Hue == -1 || i.Item.Hue == item.Hue ) && ( item.MaxPrice == -1 || i.Price <= item.MaxPrice ) )];

                if ( matches.Length > 0 && Engine.CharacterListFlags.HasFlag( CharacterListFlags.PaladinNecromancerClassTooltips ) )
                {
                    UOC.WaitForPropertiesAsync( matches.Select( e => e.Item ), 2000 ).ConfigureAwait( false );
                }

                foreach ( ShopListEntry match in matches )
                {
                    if ( item.Amount != -1 )
                    {
                        if ( match.Amount > item.Amount )
                        {
                            match.Amount = item.Amount;
                        }

                        if ( entry.IncludeBackpackAmount )
                        {
                            int currentAmount = ObjectCommands.CountType( item.BackpackGraphic, "backpack", item.Hue );

                            if ( currentAmount + match.Amount > item.Amount )
                            {
                                match.Amount = item.Amount - currentAmount;
                            }
                        }
                    }

                    if ( item.Weight > 0 )
                    {
                        int availableWeight = Engine.Player.WeightMax - Engine.Player.Weight - MinWeightAvailable - purchasedWeight;

                        int maxBuy = (int) ( availableWeight / item.Weight );

                        if ( match.Amount > maxBuy )
                        {
                            match.Amount = maxBuy;
                        }

                        purchasedWeight += (int) ( match.Amount * item.Weight );
                    }

                    if ( CheckItemCount && Engine.TooltipsEnabled )
                    {
                        int itemCount = item.Stackable ? 1 : match.Amount;

                        (int count, int max) = GetBackpackItemCount();

                        if ( max - count - MinItemsAvailable < itemCount )
                        {
                            match.Amount = Math.Max( 0, max - count - MinItemsAvailable );
                        }
                    }

                    if ( match.Amount > 0 && buyList.All( e => e.Item.Serial != match.Item.Serial ) )
                    {
                        buyList.Add( match );
                    }
                }
            }
        }

        if ( buyList.Count > 0 )
        {
            UOC.VendorBuy( serial, [.. buyList] );
        }

        if ( buyList.Count == 0 )
        {
            UOC.SystemMessage( Strings.Buy_Agent__No_matches_found_, true );
        }
    }

    private void Insert( object obj )
    {
        Items.Add( new VendorBuyAgentEntry { Name = $"Buy-{Items.Count + 1}", Enabled = true } );
    }

    private void Remove( object obj )
    {
        if ( obj is not VendorBuyAgentEntry entry )
        {
            return;
        }

        Items.Remove( entry );
    }

    private void RemoveEntry( object obj )
    {
        if ( obj is not VendorBuyAgentItem item )
        {
            return;
        }

        SelectedItem?.Items.Remove( item );
    }

    public (int count, int max) GetBackpackItemCount()
    {
        if ( Engine.Player?.Backpack == null || Engine.Player.Backpack.Properties == null )
        {
            return (-1, -1);
        }

        int count = -1;
        int max = -1;

        Property contentsProperty = Engine.Player.Backpack.Properties.FirstOrDefault( p => p.Cliloc is 1072241 or 1073841 );

        if ( contentsProperty != null && contentsProperty.Arguments.Length > 0 )
        {
            int.TryParse( contentsProperty.Arguments[0], out count );
            int.TryParse( contentsProperty.Arguments[1], out max );
        }

        return (count, max);
    }
}