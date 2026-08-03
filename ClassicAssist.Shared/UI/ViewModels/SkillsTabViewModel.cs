using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using ClassicAssist.Shared;
using ClassicAssist.Data;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Hotkeys.Commands;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Skills;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UI.Misc;
using ClassicAssist.UO;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.Packets;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.UI.ViewModels
{
    public class SkillsTabViewModel : BaseViewModel, IGlobalSettingProvider
    {
        private HotkeyCommand _hotkeyCategory;
        private ObservableCollectionEx<SkillEntry> _items = new ObservableCollectionEx<SkillEntry>();
        private ICommand _resetDeltasCommand;
        private SkillEntry _selectedItem;
        private ICommand _setAllSkillLocksCommand;
        private ICommand _setSkillLocksCommand;
        private SkillSortInfo _sortInfo;
        private float _totalBase;
        private ICommand _useSkillCommand;

        public SkillsTabViewModel()
        {
            Items.CollectionChanged += ( sender, args ) => { UpdateTotalBase(); };

            IncomingPacketHandlers.SkillUpdatedEvent += OnSkillUpdatedEvent;
            IncomingPacketHandlers.SkillsListEvent += OnSkillsListEvent;

            if ( Engine.Player != null && Engine.Connected )
            {
                Shared.UO.Commands.MobileQuery( Engine.Player.Serial, MobileQueryType.SkillsRequest );
            }

            SkillManager manager = SkillManager.GetInstance();
            manager.Items = Items;
        }

        public SkillSortInfo SortInfo
        {
            get => _sortInfo;
            private set => SetProperty( ref _sortInfo, value );
        }

        public ObservableCollectionEx<SkillEntry> Items
        {
            get => _items;
            set => SetProperty( ref _items, value );
        }

        public ICommand ResetDeltasCommand =>
            _resetDeltasCommand ?? ( _resetDeltasCommand = new RelayCommand( ResetDeltas, o => true ) );

        public SkillEntry SelectedItem
        {
            get => _selectedItem;
            set => SetProperty( ref _selectedItem, value );
        }

        public ICommand SetAllSkillLocksCommand =>
            _setAllSkillLocksCommand ?? ( _setAllSkillLocksCommand = new RelayCommand( SetAllSkillLocks, o => true ) );

        public ICommand SetSkillLocksCommand =>
            _setSkillLocksCommand ?? ( _setSkillLocksCommand = new RelayCommand( SetSkillLocks, o => true ) );

        public float TotalBase
        {
            get => _totalBase;
            set => SetProperty( ref _totalBase, value );
        }

        public ICommand UseSkillCommand =>
            _useSkillCommand ?? ( _useSkillCommand =
                new RelayCommand( UseSkill, o => SelectedItem?.Skill.Invokable ?? false ) );

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
            return "Skills.json";
        }

        public void Serialize( JObject json, bool global = false )
        {
            JArray skills = new JArray();

            if ( _hotkeyCategory?.Children != null )
            {
                foreach ( HotkeyEntry hks in _hotkeyCategory.Children.Where( o => o.IsGlobal == global ) )
                {
                    if ( Equals( hks.Hotkey, ShortcutKeys.Default ) )
                    {
                        continue;
                    }

                    skills.Add( new JObject
                    {
                        { "Name", hks.Name },
                        { "Keys", hks.Hotkey.ToJObject() },
                        { "PassToUO", hks.PassToUO },
                        { "Disableable", hks.Disableable }
                    } );
                }

                json.Add( "Skills", skills );
            }

            if ( _sortInfo != null && !global )
            {
                json.Add( "SkillsSort", new JObject
                {
                    { "SortField", _sortInfo.SortBy.ToString() },
                    { "SortDirection", _sortInfo.Direction.ToString() }
                } );
            }
        }

        public void Deserialize( JObject json, Options options, bool global = false )
        {
            if ( !global && json?["SkillsSort"] is JObject sortObj && sortObj["SortField"] != null &&
                 sortObj["SortDirection"] != null &&
                 Enum.TryParse( sortObj["SortField"].ToString(), out SkillSortBy sortBy ) &&
                 Enum.TryParse( sortObj["SortDirection"].ToString(), out ListSortDirection direction ) )
            {
                SortInfo = new SkillSortInfo( sortBy, direction );
            }

            HotkeyManager hotkey = HotkeyManager.GetInstance();

            if ( Skills.GetSkillsArray() == null )
            {
                return;
            }

            IOrderedEnumerable<SkillData> skills =
                Skills.GetSkillsArray().Where( s => s.Invokable ).OrderBy( s => s.Name );

            ObservableCollectionEx<HotkeyEntry> hotkeyEntries = new ObservableCollectionEx<HotkeyEntry>();

            foreach ( SkillData skill in skills )
            {
                hotkeyEntries.Add( new HotkeyCommand
                {
                    Action = ( hks, _ ) => SkillCommands.UseSkill( skill.Name ), Name = skill.Name
                } );
            }

            if ( json["Skills"] != null )
            {
                foreach ( HotkeyEntry hke in hotkeyEntries )
                {
                    JToken token = json["Skills"].FirstOrDefault( jo => jo["Name"].ToObject<string>() == hke.Name );

                    if ( token == null )
                    {
                        continue;
                    }

                    hke.Hotkey = new ShortcutKeys( token["Keys"] );
                    hke.PassToUO = token["PassToUO"]?.ToObject<bool>() ?? true;
                    hke.Disableable = token["Disableable"]?.ToObject<bool>() ?? true;
                    hke.IsGlobal = global;
                }
            }

            _hotkeyCategory = hotkey.Items.FirstOrDefault( hk => hk.IsCategory && hk.Name == Strings.Skills ) ??
                              new HotkeyCommand { Name = Strings.Skills, IsCategory = true };

            // Global pass runs after the profile pass and overlays onto the same entries
            if ( !global )
            {
                _hotkeyCategory.Children = hotkeyEntries;
            }
            else
            {
                foreach ( HotkeyEntry hke in hotkeyEntries )
                {
                    HotkeyEntry hk = _hotkeyCategory.Children.FirstOrDefault( o => o.Name == hke.Name );

                    if ( hk == null )
                    {
                        _hotkeyCategory.Children.Add( hke );
                        continue;
                    }

                    if ( !hk.Hotkey.Equals( ShortcutKeys.Default ) && hke.Hotkey.Equals( ShortcutKeys.Default ) )
                    {
                        continue;
                    }

                    hk.Hotkey = hke.Hotkey;
                    hk.PassToUO = hke.PassToUO;
                    hk.Disableable = hke.Disableable;
                    hk.IsGlobal = hke.IsGlobal;
                }
            }

            hotkey.AddCategory( _hotkeyCategory );
        }

        private void ResetDeltas( object obj )
        {
            foreach ( SkillEntry skillEntry in Items )
            {
                skillEntry.Delta = 0;
            }
        }

        private void SetSkillLocks( object obj )
        {
            LockStatus lockStatus = (LockStatus) obj;

            if ( SelectedItem == null )
            {
                return;
            }

            Shared.UO.Commands.ChangeSkillLock( SelectedItem, lockStatus );
        }

        private void SetAllSkillLocks( object obj )
        {
            LockStatus lockStatus = (LockStatus) (int) obj;

            IEnumerable<SkillEntry> skillsToSet = Items.Where( i => i.LockStatus != lockStatus );

            foreach ( SkillEntry skillEntry in skillsToSet )
            {
                Shared.UO.Commands.ChangeSkillLock( skillEntry, lockStatus, false );
            }

            Shared.UO.Commands.MobileQuery( Engine.Player.Serial, MobileQueryType.SkillsRequest );
        }

        private void UpdateTotalBase()
        {
            TotalBase = Items.Sum( se => se.Base );
        }

        private void OnSkillsListEvent( SkillInfo[] skills )
        {
            _dispatcher.Invoke( () => { Items.Clear(); } );

            foreach ( SkillInfo si in skills )
            {
                Skill skill = new Skill
                {
                    ID = si.ID, Name = Skills.GetSkillName( si.ID ), Invokable = Skills.IsInvokable( si.ID )
                };

                SkillEntry se = new SkillEntry
                {
                    Skill = skill,
                    Value = si.Value,
                    Base = si.BaseValue,
                    Cap = si.SkillCap,
                    LockStatus = si.LockStatus
                };

                _dispatcher.Invoke( () => { Items.Add( se ); } );
            }
        }

        private void UseSkill( object obj )
        {
            if ( SelectedItem == null )
            {
                return;
            }

            if ( SelectedItem.Skill.Invokable )
            {
                Engine.SendPacketToServer( new UseSkill( SelectedItem.Skill.ID ) );
            }
        }

        private void OnSkillUpdatedEvent( int skillID, float value, float baseValue, LockStatus lockStatus,
            float skillCap )
        {
            SkillEntry entry = _items.FirstOrDefault( se => se.Skill.ID == skillID );

            if ( entry == null )
            {
                return;
            }

            _dispatcher.Invoke( () =>
            {
                entry.Delta += baseValue - entry.Base;
                entry.Value = value;
                entry.Base = baseValue;
                entry.Cap = skillCap;
                entry.LockStatus = lockStatus;
            } );
        }

        /// <summary>
        ///     Called by the view when the user changes the skills sort, so the choice is persisted with
        ///     the profile via <see cref="Serialize" />.
        /// </summary>
        public void SetSort( SkillSortBy sortBy, ListSortDirection direction )
        {
            SortInfo = new SkillSortInfo( sortBy, direction );
        }

        public void ClearSort()
        {
            SortInfo = null;
        }
    }

    public enum SkillSortBy
    {
        Name,
        Value,
        Base,
        Delta,
        Cap,
        LockStatus
    }

    public class SkillSortInfo
    {
        public SkillSortInfo( SkillSortBy sortBy, ListSortDirection direction )
        {
            SortBy = sortBy;
            Direction = direction;
        }

        public ListSortDirection Direction { get; }
        public SkillSortBy SortBy { get; }
    }
}