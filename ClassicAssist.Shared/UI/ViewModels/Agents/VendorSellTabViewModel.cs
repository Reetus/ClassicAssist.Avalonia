using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Vendors;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json.Linq;
using UOC = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.Shared.UI.ViewModels.Agents
{
    public class VendorSellTabViewModel : BaseViewModel, ISettingProvider, IDisposable
    {
        private int _containerSerial;
        private ICommand _insertCommand;
        private ICommand _insertMatchAnyCommand;
        private ObservableCollection<VendorSellAgentEntry> _items = new ObservableCollection<VendorSellAgentEntry>();
        private ICommand _removeCommand;
        private ICommand _resetContainerCommand;
        private VendorSellAgentEntry _selectedItem;
        private ICommand _setContainerCommand;

        public VendorSellTabViewModel()
        {
            IncomingPacketHandlers.VendorSellDisplayEvent += OnVendorSellDisplayEvent;
        }

        public void Dispose()
        {
            IncomingPacketHandlers.VendorSellDisplayEvent -= OnVendorSellDisplayEvent;
        }

        public int ContainerSerial
        {
            get => _containerSerial;
            set => SetProperty( ref _containerSerial, value );
        }

        public ICommand InsertCommand =>
            _insertCommand ?? ( _insertCommand = new RelayCommandAsync( Insert, o => true ) );

        public ICommand InsertMatchAnyCommand =>
            _insertMatchAnyCommand ?? ( _insertMatchAnyCommand = new RelayCommand( InsertMatchAny, o => true ) );

        public ObservableCollection<VendorSellAgentEntry> Items
        {
            get => _items;
            set => SetProperty( ref _items, value );
        }

        public ICommand RemoveCommand =>
            _removeCommand ?? ( _removeCommand = new RelayCommand( Remove, o => SelectedItem != null ) );

        public ICommand ResetContainerCommand =>
            _resetContainerCommand ??
            ( _resetContainerCommand = new RelayCommand( ResetContainer, o => ContainerSerial != 0 ) );

        public VendorSellAgentEntry SelectedItem
        {
            get => _selectedItem;
            set => SetProperty( ref _selectedItem, value );
        }

        public ICommand SetContainerCommand =>
            _setContainerCommand ?? ( _setContainerCommand = new RelayCommandAsync( SetContainer, o => true ) );

        public void Serialize( JObject json )
        {
            if ( json == null )
            {
                return;
            }

            JArray itemsObj = new JArray();

            foreach ( VendorSellAgentEntry item in Items )
            {
                JObject itemObj = new JObject
                {
                    { "Enabled", item.Enabled },
                    { "Graphic", item.Graphic },
                    { "Hue", item.Hue },
                    { "Amount", item.Amount },
                    { "MinPrice", item.MinPrice },
                    { "Name", item.Name }
                };

                itemsObj.Add( itemObj );
            }

            JObject config = new JObject { { "Items", itemsObj }, { "ContainerSerial", ContainerSerial } };

            json.Add( "VendorSell", config );
        }

        public void Deserialize( JObject json, Options options )
        {
            Items.Clear();

            if ( json?["VendorSell"] == null )
            {
                return;
            }

            JToken config = json["VendorSell"];

            foreach ( JToken items in config?["Items"] )
            {
                VendorSellAgentEntry vsae = new VendorSellAgentEntry
                {
                    Enabled = items["Enabled"]?.ToObject<bool>() ?? true,
                    Graphic = items["Graphic"]?.ToObject<int>() ?? 0,
                    Hue = items["Hue"]?.ToObject<int>() ?? 0,
                    Amount = items["Amount"]?.ToObject<int>() ?? -1,
                    MinPrice = items["MinPrice"]?.ToObject<int>() ?? 0,
                    Name = items["Name"]?.ToObject<string>() ?? string.Empty
                };

                Items.Add( vsae );
            }

            ContainerSerial = config["ContainerSerial"]?.ToObject<int>() ?? 0;
        }

        private void InsertMatchAny( object arg )
        {
            Items.Add( new VendorSellAgentEntry
            {
                Enabled = true,
                Name = Strings.Any,
                Graphic = -1,
                Hue = -1,
                Amount = -1,
                MinPrice = 0
            } );
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
                UOC.SystemMessage( Strings.Invalid_or_unknown_object_id, true );
                return;
            }

            ContainerSerial = serial;
        }

        private void Remove( object arg )
        {
            if ( !( arg is VendorSellAgentEntry entry ) )
            {
                return;
            }

            Items.Remove( entry );
        }

        private void OnVendorSellDisplayEvent( int serial, SellListEntry[] entries )
        {
            List<SellListEntry> sellList = new List<SellListEntry>();

            // Track the remaining sell budget per matched agent entry so the Amount limit is a total
            // across all matching stacks, rather than being applied to each stack separately.
            Dictionary<VendorSellAgentEntry, int> remaining = new Dictionary<VendorSellAgentEntry, int>();

            foreach ( SellListEntry entry in entries )
            {
                VendorSellAgentEntry match = Items.FirstOrDefault( i =>
                    ( i.Graphic == -1 || i.Graphic == entry.ID ) && ( i.Hue == -1 || i.Hue == entry.Hue ) &&
                    entry.Price >= i.MinPrice && i.Enabled );

                if ( match == null )
                {
                    continue;
                }

                if ( match.Amount != -1 )
                {
                    if ( !remaining.TryGetValue( match, out int budget ) )
                    {
                        budget = match.Amount;
                    }

                    entry.Amount = Math.Min( budget, entry.Amount );
                    remaining[match] = budget - entry.Amount;

                    if ( entry.Amount <= 0 )
                    {
                        continue;
                    }
                }

                sellList.Add( entry );
            }

            if ( ContainerSerial != 0 )
            {
                if ( Engine.Player?.Backpack?.Container == null ||
                     !Engine.Player.Backpack.Container.GetItem( ContainerSerial, out Item container ) )
                {
                    UOC.SystemMessage( Strings.Invalid_container___ );

                    return;
                }

                int[] containerIds =
                    container.Container?.GetItems().Select( i => i.ID ).ToArray() ?? Array.Empty<int>();

                UOC.WaitForContainerContents( ContainerSerial, 1000 );

                List<SellListEntry> filteredList = sellList.Where( e => containerIds.Contains( e.ID ) ).ToList();

                foreach ( SellListEntry entry in filteredList )
                {
                    int totalAmount = container.Container?.Where( e => e.ID == entry.ID ).Sum( e => e.Count ) ?? 0;

                    entry.Amount = Math.Min( entry.Amount, totalAmount );
                }

                sellList = filteredList;
            }

            if ( sellList.Count > 0 )
            {
                Sell( serial, sellList.ToArray() );
            }
        }

        public static void Sell( int vendorSerial, SellListEntry[] entries )
        {
            if ( entries == null || entries.Length == 0 )
            {
                return;
            }

            int len = 9 + entries.Length * 6;

            PacketWriter pw = new PacketWriter( len );
            pw.Write( (byte) 0x9F );
            pw.Write( (short) len );
            pw.Write( vendorSerial );
            pw.Write( (short) entries.Length );

            foreach ( SellListEntry entry in entries )
            {
                pw.Write( entry.Serial );
                pw.Write( (short) entry.Amount );
            }

            Engine.SendPacketToServer( pw );
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

            string name = TileData.GetStaticTile( item.ID ).Name ?? item.Name;

            Items.Add( new VendorSellAgentEntry
            {
                Enabled = true,
                Name = name,
                Graphic = item.ID,
                Hue = item.Hue,
                Amount = -1,
                MinPrice = 0
            } );
        }
    }
}