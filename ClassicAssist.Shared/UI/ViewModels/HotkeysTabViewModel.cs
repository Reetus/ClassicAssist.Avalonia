using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Reflection;
using ClassicAssist.Data;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Hotkeys.Commands;
using ClassicAssist.Data.Spells;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.Misc;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.UI.ViewModels
{
    public class HotkeysTabViewModel : BaseViewModel, IGlobalSettingProvider
    {
        private const string GLOBAL_HOTKEYS_FILENAME = "Hotkeys.json";
        private readonly HotkeyManager _hotkeyManager;
        private readonly List<HotkeyCommand> _serializeCategories = new List<HotkeyCommand>();
        private ICommand _clearHotkeyCommand;
        private ICommand _executeCommand;
        private ObservableCollectionEx<HotkeyCommand> _filterItems;
        private string _filterText;
        private HotkeyCommand _masteriesCategory;
        private HotkeyEntry _selectedItem;
        private bool _searching;
        private HotkeyCommand _spellsCategory;

        public HotkeysTabViewModel()
        {
            _hotkeyManager = HotkeyManager.GetInstance();
            _hotkeyManager.ClearAllHotkeys = ClearAllHotkeys;

            _filterItems = Items;

            Items.CollectionChanged += ( s, ea ) => UpdateFilteredItems();
        }

        public ICommand ClearHotkeyCommand =>
            _clearHotkeyCommand ?? ( _clearHotkeyCommand = new RelayCommand( ClearHotkey, o => SelectedItem != null ) );

        public ICommand ExecuteCommand =>
            _executeCommand ?? ( _executeCommand =
                new RelayCommand( ExecuteHotkey, o => SelectedItem != null && !SelectedItem.IsCategory ) );

        public ObservableCollectionEx<HotkeyCommand> FilterItems
        {
            get => _filterItems;
            set => SetProperty( ref _filterItems, value );
        }

        public string FilterText
        {
            get => _filterText;
            set
            {
                SetProperty( ref _filterText, value );
                UpdateFilteredItems();
            }
        }

        public ShortcutKeys Hotkey
        {
            get => SelectedItem?.Hotkey;
            set => CheckOverwriteHotkey( SelectedItem, value );
        }

        public ObservableCollectionEx<HotkeyCommand> Items
        {
            get => _hotkeyManager.Items;
            set => _hotkeyManager.Items = value;
        }

        public HotkeyEntry SelectedItem
        {
            get => _selectedItem;
            set
            {
                SetProperty( ref _selectedItem, value == null || !value.IsCategory ? value : null );
                NotifyPropertyChanged( nameof( Hotkey ) );
            }
        }

        public void Serialize( JObject json )
        {
            Serialize( json, false );
        }

        public void Deserialize( JObject json, Options options )
        {
            Deserialize( json, options, false );
        }

        public string GetGlobalFilename()
        {
            return GLOBAL_HOTKEYS_FILENAME;
        }

        public void Serialize( JObject json, bool global = false )
        {
            JObject hotkeys = new JObject();

            JArray commandsArray = new JArray();

            foreach ( HotkeyCommand category in _serializeCategories )
            {
                foreach ( HotkeyEntry categoryChild in category.Children.Where( e => e.IsGlobal == global ) )
                {
                    if ( Equals( categoryChild.Hotkey, ShortcutKeys.Default ) )
                    {
                        continue;
                    }

                    JObject entry = new JObject
                    {
                        { "Type", categoryChild.GetType().FullName },
                        { "Keys", categoryChild.Hotkey.ToJObject() },
                        { "PassToUO", categoryChild.PassToUO },
                        { "Disableable", categoryChild.Disableable }
                    };

                    commandsArray.Add( entry );
                }
            }

            hotkeys.Add( "Commands", commandsArray );

            JArray spellsArray = new JArray();

            foreach ( HotkeyEntry spellsCategoryChild in _spellsCategory.Children.Where( e => e.IsGlobal == global ) )
            {
                if ( Equals( spellsCategoryChild.Hotkey, ShortcutKeys.Default ) )
                {
                    continue;
                }

                JObject entry = new JObject
                {
                    { "Name", spellsCategoryChild.Name },
                    { "Keys", spellsCategoryChild.Hotkey.ToJObject() },
                    { "PassToUO", spellsCategoryChild.PassToUO },
                    { "Disableable", spellsCategoryChild.Disableable }
                };

                spellsArray.Add( entry );
            }

            hotkeys.Add( "Spells", spellsArray );

            JArray masteryArray = new JArray();

            foreach ( HotkeyEntry masteriesCategoryChild in _masteriesCategory.Children.Where( e => e.IsGlobal == global ) )
            {
                if ( Equals( masteriesCategoryChild.Hotkey, ShortcutKeys.Default ) )
                {
                    continue;
                }

                JObject entry = new JObject
                {
                    { "Name", masteriesCategoryChild.Name },
                    { "Keys", masteriesCategoryChild.Hotkey.ToJObject() },
                    { "PassToUO", masteriesCategoryChild.PassToUO },
                    { "Disableable", masteriesCategoryChild.Disableable }
                };

                masteryArray.Add( entry );
            }

            hotkeys.Add( "Masteries", masteryArray );

            json?.Add( "Hotkeys", hotkeys );
        }

        public void Deserialize( JObject json, Options options, bool global = false )
        {
            // Commands are only ever built on the profile pass; the global pass just overlays
            // hotkeys onto the existing instances.
            if ( !global )
            {
                // Rebuild each time: a previous load may have already created the categories on the
                // shared HotkeyManager singleton, in which case the discovery loop below wouldn't
                // re-add them here and they'd silently stop serializing.
                _serializeCategories.Clear();

                IEnumerable<Type> hotkeyCommands = Assembly.GetExecutingAssembly().GetTypes()
                    .Where( i => i.IsSubclassOf( typeof( HotkeyCommand ) ) );

                foreach ( Type hotkeyCommand in hotkeyCommands )
                {
                    HotkeyCommand hkc = (HotkeyCommand) Activator.CreateInstance( hotkeyCommand );

                    HotkeyCommandAttribute attr = hkc.GetType().GetCustomAttribute<HotkeyCommandAttribute>();

                    string categoryName = Strings.Commands;

                    if ( attr != null && !string.IsNullOrEmpty( attr.Category ) )
                    {
                        categoryName = Strings.ResourceManager.GetString( attr.Category );
                    }

                    HotkeyCommand category = Items.FirstOrDefault( hke => hke.Name == categoryName && hke.IsCategory );

                    if ( category != null )
                    {
                        if ( category.Children == null )
                        {
                            category.Children = new ObservableCollectionEx<HotkeyEntry>();
                        }

                        if ( category.Children.Contains( hkc ) )
                        {
                            category.Children.Remove( hkc );
                        }

                        category.Children.Add( hkc );
                    }
                    else
                    {
                        category = new HotkeyCommand
                        {
                            Name = categoryName, IsCategory = true, Children = new ObservableCollectionEx<HotkeyEntry>()
                        };

                        category.Children.Add( hkc );
                        _hotkeyManager.AddCategory( category );
                    }

                    if ( !_serializeCategories.Contains( category ) )
                    {
                        _serializeCategories.Add( category );
                    }
                }
            }

            JToken hotkeys = json?["Hotkeys"];

            if ( hotkeys?["Commands"] != null )
            {
                foreach ( JToken token in hotkeys["Commands"] )
                {
                    JToken type = token["Type"];

                    foreach ( HotkeyCommand category in _serializeCategories )
                    {
                        HotkeyEntry entry =
                            category.Children.FirstOrDefault( o => o.GetType().FullName == type.ToObject<string>() );

                        if ( entry == null )
                        {
                            continue;
                        }

                        JToken keys = token["Keys"];

                        entry.Hotkey = new ShortcutKeys( keys );
                        entry.PassToUO = token["PassToUO"]?.ToObject<bool>() ?? true;
                        entry.Disableable = token["Disableable"]?.ToObject<bool>() ?? entry.Disableable;
                        entry.IsGlobal = global;
                    }
                }
            }

            if ( _spellsCategory != null && !global )
            {
                _hotkeyManager.Items.Remove( _spellsCategory );
            }

            SpellManager spellManager = SpellManager.GetInstance();

            if ( !global )
            {
                _spellsCategory = new HotkeyCommand { Name = Strings.Spells, IsCategory = true };

                SpellData[] spells = spellManager.GetSpellData();

                ObservableCollectionEx<HotkeyEntry> children = new ObservableCollectionEx<HotkeyEntry>();

                foreach ( SpellData spell in spells )
                {
                    HotkeyCommand hkc = new HotkeyCommand
                    {
                        Name = spell.Name,
                        Action = ( hks, _ ) => spellManager.CastSpell( spell.ID ),
                        Hotkey = ShortcutKeys.Default,
                        PassToUO = true
                    };

                    children.Add( hkc );
                }

                _spellsCategory.Children = children;

                _hotkeyManager.AddCategory( _spellsCategory );
            }

            JToken spellsObj = hotkeys?["Spells"];

            if ( spellsObj != null )
            {
                foreach ( JToken token in spellsObj )
                {
                    JToken name = token["Name"];

                    HotkeyEntry entry =
                        _spellsCategory.Children.FirstOrDefault( s => s.Name == name.ToObject<string>() );

                    if ( entry == null )
                    {
                        continue;
                    }

                    entry.Hotkey = new ShortcutKeys( token["Keys"] );
                    entry.PassToUO = token["PassToUO"]?.ToObject<bool>() ?? true;
                    entry.Disableable = token["Disableable"]?.ToObject<bool>() ?? entry.Disableable;
                    entry.IsGlobal = global;
                }
            }

            if ( _masteriesCategory != null && !global )
            {
                _hotkeyManager.Items.Remove( _masteriesCategory );
            }

            if ( !global )
            {
                _masteriesCategory = new HotkeyCommand { Name = Strings.Masteries, IsCategory = true };

                SpellData[] masteries = spellManager.GetMasteryData();

                ObservableCollectionEx<HotkeyEntry> masteryChildren = new ObservableCollectionEx<HotkeyEntry>();

                foreach ( SpellData mastery in masteries )
                {
                    HotkeyCommand hkc = new HotkeyCommand
                    {
                        Name = mastery.Name,
                        Action = ( hks, _ ) => spellManager.CastSpell( mastery.ID ),
                        Hotkey = ShortcutKeys.Default,
                        PassToUO = true
                    };

                    masteryChildren.Add( hkc );
                }

                _masteriesCategory.Children = masteryChildren;

                _hotkeyManager.AddCategory( _masteriesCategory );
            }

            JToken masteryObj = hotkeys?["Masteries"];

            if ( masteryObj != null )
            {
                foreach ( JToken token in masteryObj )
                {
                    JToken name = token["Name"];

                    HotkeyEntry entry =
                        _masteriesCategory.Children.FirstOrDefault( s => s.Name == name.ToObject<string>() );

                    if ( entry == null )
                    {
                        continue;
                    }

                    entry.Hotkey = new ShortcutKeys( token["Keys"] );
                    entry.PassToUO = token["PassToUO"]?.ToObject<bool>() ?? true;
                    entry.Disableable = token["Disableable"]?.ToObject<bool>() ?? entry.Disableable;
                    entry.IsGlobal = global;
                }
            }
        }

        private void UpdateFilteredItems()
        {
            if ( _searching )
            {
                return;
            }

            _searching = true;

            if ( string.IsNullOrEmpty( FilterText ) )
            {
                FilterItems = Items;
            }
            else
            {
                bool Predicate( HotkeyEntry hke ) =>
                    hke.Name?.ToLower().Contains( FilterText.ToLower() ) ?? false;

                // Reuse the real child entries (so selection/serialization still refer to them) inside
                // lightweight category wrappers that only carry the matching children.
                ObservableCollectionEx<HotkeyCommand> items = new ObservableCollectionEx<HotkeyCommand>();

                foreach ( HotkeyCommand category in Items.Where( c => c.Children != null && c.Children.Any( Predicate ) ) )
                {
                    ObservableCollectionEx<HotkeyEntry> children = new ObservableCollectionEx<HotkeyEntry>();

                    foreach ( HotkeyEntry child in category.Children.Where( Predicate ) )
                    {
                        children.Add( child );
                    }

                    items.Add( new HotkeyCommand { Name = category.Name, IsCategory = true, Children = children } );
                }

                FilterItems = items;
            }

            _searching = false;
        }

        private async void CheckOverwriteHotkey( HotkeyEntry selectedItem, ShortcutKeys hotkey )
        {
            HotkeyEntry conflict = null;

            foreach ( HotkeyCommand hotkeyEntry in Items )
            {
                foreach ( HotkeyEntry entry in hotkeyEntry.Children )
                {
                    if ( entry.Hotkey.Equals( hotkey ) )
                    {
                        conflict = entry;
                    }
                }
            }

            if ( conflict != null && !ReferenceEquals( selectedItem, conflict ) )
            {
                MessageBoxResult result = await Engine.MessageBoxProvider.Show(
                    string.Format( Strings.Overwrite_existing_hotkey___0____, conflict ), Strings.Warning,
                    MessageBoxButtons.YesNo );

                if ( result == MessageBoxResult.No )
                {
                    NotifyPropertyChanged( nameof( Hotkey ) );
                    return;
                }
            }

            SelectedItem.Hotkey = hotkey;
            NotifyPropertyChanged( nameof( Hotkey ) );
        }

        private void ClearAllHotkeys()
        {
            foreach ( HotkeyEntry entryChild in _serializeCategories.SelectMany( hotkeyEntry => hotkeyEntry.Children ) )
            {
                entryChild.Hotkey = ShortcutKeys.Default;
            }

            foreach ( HotkeyEntry spellEntry in _spellsCategory.Children )
            {
                spellEntry.Hotkey = ShortcutKeys.Default;
            }
        }

        private void ClearHotkey( object obj )
        {
            if ( obj is HotkeyEntry cmd )
            {
                cmd.Hotkey = ShortcutKeys.Default;
                NotifyPropertyChanged( nameof( Hotkey ) );
            }
        }

        private static void ExecuteHotkey( object obj )
        {
            if ( obj is HotkeyEntry cmd )
            {
                cmd.Action( cmd, null );
            }
        }
    }
}