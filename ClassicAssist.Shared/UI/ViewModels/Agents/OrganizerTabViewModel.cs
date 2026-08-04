using System;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Hotkeys.Commands;
using ClassicAssist.Data.Organizer;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Shared.UI.ViewModels.Agents
{
    public class OrganizerTabViewModel : HotkeyEntryViewModel<OrganizerEntry>, ISettingProvider
    {
        private readonly OrganizerManager _manager;
        private ICommand _clearEntryDestinationContainerCommand;
        private ICommand _clearEntrySourceContainerCommand;
        private ICommand _insertItemCommand;
        private bool _isOrganizing;
        private ICommand _newOrganizerEntryCommand;
        private ICommand _organizeCommand;
        private ICommand _removeItemCommand;
        private ICommand _removeOrganizerAgentEntryCommand;
        private OrganizerEntry _selectedItem;
        private OrganizerItem _selectedOrganizerItem;
        private ICommand _setContainersCommand;
        private ICommand _setEntryDestinationContainerCommand;
        private ICommand _setEntrySourceContainerCommand;

        public OrganizerTabViewModel() : base( Strings.Organizer )
        {
            _manager = OrganizerManager.GetInstance();

            _manager.Items = Items;

            // A category-level hotkey rather than a per-entry one: it stops whichever organizer is
            // running. Persisted separately from the entries, under the top-level "OrganizerOptions".
            _staticOptions.Add( new HotkeyCommand
            {
                Name = Strings.Stop_Organizer, Action = ( entry, objects ) => _manager.Stop(), CanGlobal = false
            } );
        }

        public ICommand InsertItemCommand =>
            _insertItemCommand ?? ( _insertItemCommand =
                new RelayCommandAsync( InsertItem, o => SelectedItem != null && !IsOrganizing ) );

        public ICommand ClearEntryDestinationContainerCommand =>
            _clearEntryDestinationContainerCommand ?? ( _clearEntryDestinationContainerCommand =
                new RelayCommand( ClearEntryDestinationContainer, o => !IsOrganizing ) );

        public ICommand ClearEntrySourceContainerCommand =>
            _clearEntrySourceContainerCommand ?? ( _clearEntrySourceContainerCommand =
                new RelayCommand( ClearEntrySourceContainer, o => !IsOrganizing ) );

        public bool IsOrganizing
        {
            get => _isOrganizing;
            set
            {
                SetProperty( ref _isOrganizing, value );
                NotifyPropertyChanged( nameof( PlayStopButtonText ) );
            }
        }

        public ICommand NewOrganizerEntryCommand =>
            _newOrganizerEntryCommand ??
            ( _newOrganizerEntryCommand = new RelayCommand( NewOrganizerEntry, o => !IsOrganizing ) );

        public ICommand OrganizeCommand =>
            _organizeCommand ?? ( _organizeCommand = new RelayCommandAsync( Organize, o => SelectedItem != null ) );

        public string PlayStopButtonText => IsOrganizing ? Strings.Stop : Strings.Play;

        public ICommand RemoveItemCommand =>
            _removeItemCommand ?? ( _removeItemCommand =
                new RelayCommand( RemoveItem, o => !IsOrganizing && SelectedOrganizerItem != null ) );

        public ICommand RemoveOrganizerAgentEntryCommand =>
            _removeOrganizerAgentEntryCommand ?? ( _removeOrganizerAgentEntryCommand =
                new RelayCommand( RemoveOrganizerAgentEntry, o => SelectedItem != null ) );

        public OrganizerEntry SelectedItem
        {
            get => _selectedItem;
            set => SetProperty( ref _selectedItem, value );
        }

        public OrganizerItem SelectedOrganizerItem
        {
            get => _selectedOrganizerItem;
            set => SetProperty( ref _selectedOrganizerItem, value );
        }

        public ICommand SetContainersCommand =>
            _setContainersCommand ?? ( _setContainersCommand =
                new RelayCommandAsync( _manager.SetContainers, o => SelectedItem != null && !IsOrganizing ) );

        public ICommand SetEntryDestinationContainerCommand =>
            _setEntryDestinationContainerCommand ?? ( _setEntryDestinationContainerCommand =
                new RelayCommandAsync( SetEntryDestinationContainer, o => !IsOrganizing && Engine.Connected ) );

        public ICommand SetEntrySourceContainerCommand =>
            _setEntrySourceContainerCommand ?? ( _setEntrySourceContainerCommand =
                new RelayCommandAsync( SetEntrySourceContainer, o => !IsOrganizing && Engine.Connected ) );

        public void Serialize( JObject json )
        {
            JObject options = new JObject();

            SerializeStatic( options );

            json?.Add( "OrganizerOptions", options );

            JArray organizer = new JArray();

            foreach ( OrganizerEntry organizerEntry in Items )
            {
                JObject entryObj = new JObject();

                SetJsonValue( entryObj, "Name", organizerEntry.Name );
                SetJsonValue( entryObj, "Stack", organizerEntry.Stack );
                SetJsonValue( entryObj, "SourceContainer", organizerEntry.SourceContainer );
                SetJsonValue( entryObj, "DestinationContainer", organizerEntry.DestinationContainer );
                SetJsonValue( entryObj, "Keys", organizerEntry.Hotkey.ToJObject() );
                SetJsonValue( entryObj, "Complete", organizerEntry.Complete );
                SetJsonValue( entryObj, "ReturnExcess", organizerEntry.ReturnExcess );

                JArray itemsArray = new JArray();

                foreach ( OrganizerItem organizerItem in organizerEntry.Items )
                {
                    JObject itemsObj = new JObject();

                    SetJsonValue( itemsObj, "Item", organizerItem.Item );
                    SetJsonValue( itemsObj, "ID", organizerItem.ID );
                    SetJsonValue( itemsObj, "Hue", organizerItem.Hue );
                    SetJsonValue( itemsObj, "Amount", organizerItem.Amount );
                    SetJsonValue( itemsObj, "SourceContainer", organizerItem.SourceContainer );
                    SetJsonValue( itemsObj, "DestinationContainer", organizerItem.DestinationContainer );

                    itemsArray.Add( itemsObj );
                }

                entryObj.Add( "Items", itemsArray );

                organizer.Add( entryObj );
            }

            json?.Add( "Organizer", organizer );
        }

        public void Deserialize( JObject json, Options options )
        {
            Items.Clear();

            if ( json?["OrganizerOptions"] is JObject organizerOptions )
            {
                DeserializeStatic( organizerOptions );
            }

            if ( json?["Organizer"] == null )
            {
                return;
            }

            foreach ( JToken token in json["Organizer"] )
            {
                OrganizerEntry entry = new OrganizerEntry
                {
                    Name = GetJsonValue( token, "Name", "Organizer" ),
                    Stack = GetJsonValue( token, "Stack", true ),
                    SourceContainer = GetJsonValue( token, "SourceContainer", 0 ),
                    DestinationContainer = GetJsonValue( token, "DestinationContainer", 0 ),
                    Hotkey = new ShortcutKeys( token["Keys"] ),
                    Complete = GetJsonValue( token, "Complete", false ),
                    ReturnExcess = GetJsonValue( token, "ReturnExcess", false )
                };

                entry.Action = ( hks, _ ) => Task.Run( async () => await _manager.Organize( entry ) );
                entry.IsRunning = () => IsOrganizing;

                foreach ( JToken itemToken in token["Items"] )
                {
                    OrganizerItem item = new OrganizerItem
                    {
                        Item = GetJsonValue( itemToken, "Item", string.Empty ),
                        ID = GetJsonValue( itemToken, "ID", 0 ),
                        Hue = GetJsonValue( itemToken, "Hue", -1 ),
                        Amount = GetJsonValue( itemToken, "Amount", -1 ),
                        SourceContainer = GetJsonValue<int?>( itemToken, "SourceContainer", null ),
                        DestinationContainer = GetJsonValue<int?>( itemToken, "DestinationContainer", null )
                    };

                    entry.Items.Add( item );
                }

                Items.Add( entry );
            }
        }

        private async Task Organize( object arg )
        {
            if ( !( arg is OrganizerEntry entry ) )
            {
                return;
            }

            IsOrganizing = true;

            await _manager.Organize( entry );

            IsOrganizing = false;
        }

        private void NewOrganizerEntry( object obj )
        {
            int count = Items.Count + 1;

            OrganizerEntry entry = new OrganizerEntry
            {
                Name = $"Organizer-{count}",
                Action = ( hks, _ ) => Task.Run( async () => await _manager.Organize( SelectedItem ) ),
                IsRunning = () => IsOrganizing
            };

            Items.Add( entry );
        }

        private void RemoveOrganizerAgentEntry( object obj )
        {
            if ( !( obj is OrganizerEntry entry ) )
            {
                return;
            }

            Items.Remove( entry );
        }

        private void RemoveItem( object obj )
        {
            if ( !( obj is OrganizerItem item ) )
            {
                return;
            }

            SelectedItem.Items.Remove( item );
        }

        private static async Task InsertItem( object arg )
        {
            if ( !( arg is OrganizerEntry entry ) )
            {
                return;
            }

            int serial = await Commands.GetTargetSerialAsync( Strings.Target_new_item___ );

            if ( serial <= 0 )
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

            OrganizerItem organizerItem = new OrganizerItem
            {
                Item = TileData.GetStaticTile( item.ID ).Name, ID = item.ID, Hue = item.Hue, Amount = -1
            };

            entry.Items.Add( organizerItem );
        }

        private static void ClearEntryDestinationContainer( object obj )
        {
            if ( obj is OrganizerItem entry )
            {
                entry.DestinationContainer = null;
            }
        }

        private static void ClearEntrySourceContainer( object obj )
        {
            if ( obj is OrganizerItem entry )
            {
                entry.SourceContainer = null;
            }
        }

        private static async Task SetEntryContainer( Action<int> action )
        {
            int serial = await Commands.GetTargetSerialAsync( Strings.Target_container___ );

            if ( serial <= 0 )
            {
                Commands.SystemMessage( Strings.Invalid_or_unknown_object_id );
                return;
            }

            action( serial );
        }

        private static Task SetEntryDestinationContainer( object obj )
        {
            if ( !( obj is OrganizerItem entry ) )
            {
                return Task.CompletedTask;
            }

            return SetEntryContainer( serial => entry.DestinationContainer = serial );
        }

        private static Task SetEntrySourceContainer( object obj )
        {
            if ( !( obj is OrganizerItem entry ) )
            {
                return Task.CompletedTask;
            }

            return SetEntryContainer( serial => entry.SourceContainer = serial );
        }
    }
}