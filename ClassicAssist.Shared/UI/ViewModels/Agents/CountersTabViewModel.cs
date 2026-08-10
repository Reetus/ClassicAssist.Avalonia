using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Counters;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Shared.UI.ViewModels.Agents;

public class CountersTabViewModel : BaseViewModel, ISettingProvider
{
    public CountersTabViewModel()
    {
        CountersManager manager = CountersManager.GetInstance();

        manager.Items = Items;
        manager.RecountAll = Recount;
    }

    public ICommand InsertEntryCommand => field ??= new RelayCommandAsync( InsertEntry, o => true );

    public ObservableCollection<CountersAgentEntry> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ICommand RecountCommand => field ??= new RelayCommand( o => Recount(), o => true );

    public ICommand RemoveEntryCommand => field ??= new RelayCommand( RemoveEntry, o => SelectedItem != null );

    public CountersAgentEntry SelectedItem
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool Warn
    {
        get;
        set => SetProperty( ref field, value );
    } = true;

    public int WarnAmount
    {
        get;
        set => SetProperty( ref field, value );
    }

    public void Serialize( JObject json )
    {
        JObject options = new() { { "Warn", Warn }, { "WarnAmount", WarnAmount } };

        JArray items = [];

        foreach ( CountersAgentEntry entry in Items )
        {
            JObject entryObj = new()
            {
                { "Name", entry.Name }, { "Graphic", entry.Graphic }, { "Color", entry.Color }
            };

            items.Add( entryObj );
        }

        options.Add( "Items", items );

        json?.Add( "Counters", options );
    }

    public void Deserialize( JObject json, Options options )
    {
        Items.Clear();

        if ( json?["Counters"] == null )
        {
            return;
        }

        Warn = json["Counters"]["Warn"]?.ToObject<bool>() ?? false;
        WarnAmount = json["Counters"]["WarnAmount"]?.ToObject<int>() ?? 0;

        foreach ( JToken token in json["Counters"]["Items"] )
        {
            if ( token != null )
            {
                Items.Add( new CountersAgentEntry
                {
                    Name = token["Name"].ToObject<string>() ?? "Unknown",
                    Graphic = token["Graphic"].ToObject<int>(),
                    Color = token["Color"].ToObject<int>()
                } );
            }
        }
    }

    private void RemoveEntry( object obj )
    {
        if ( obj is not CountersAgentEntry entry )
        {
            return;
        }

        Items.Remove( entry );
    }

    private void Recount()
    {
        foreach ( CountersAgentEntry item in Items )
        {
            int count = item.Count;

            item.Recount();

            if ( Warn && item.Count <= WarnAmount && count > WarnAmount )
            {
                Commands.SystemMessage(
                    string.Format( Strings.Counter___0___amount_is_now__1____, item.Name, item.Count ), 61 );
            }
        }
    }

    private async Task InsertEntry( object arg )
    {
        int serial = await Commands.GetTargetSerialAsync( Strings.Target_object___ );

        if ( serial == 0 )
        {
            Commands.SystemMessage( Strings.Invalid_or_unknown_object_id );
            return;
        }

        Item item = Engine.Items.GetItem( serial );

        if ( item == null )
        {
            Commands.SystemMessage( Strings.Cannot_find_item___ );
            return;
        }

        string name = TileData.GetStaticTile( item.ID ).Name;

        if ( string.IsNullOrEmpty( name ) )
        {
            name = item.Name;
        }

        CountersAgentEntry entry = new() { Name = name, Graphic = item.ID, Color = item.Hue };

        entry.Recount();

        Items.Add( entry );
    }
}