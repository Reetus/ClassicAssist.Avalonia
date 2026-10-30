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
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.Packets;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.UI.ViewModels;

public class SkillsTabViewModel : BaseViewModel, IGlobalSettingProvider
{
    private HotkeyCommand _hotkeyCategory;

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
        get;
        private set => SetProperty( ref field, value );
    }

    public ObservableCollectionEx<SkillEntry> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ICommand ResetDeltasCommand => field ??= new RelayCommand( ResetDeltas, o => true );

    public SkillEntry SelectedItem
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand SetAllSkillLocksCommand => field ??= new RelayCommand( SetAllSkillLocks, o => true );

    public ICommand SetSkillLocksCommand => field ??= new RelayCommand( SetSkillLocks, o => true );

    public float TotalBase
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand UseSkillCommand => field ??=
            new RelayCommand( UseSkill, o => SelectedItem?.Skill.Invokable ?? false );

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
        JArray skills = [];

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

        if ( SortInfo != null && !global )
        {
            json.Add( "SkillsSort", new JObject
            {
                { "SortField", SortInfo.SortBy.ToString() },
                { "SortDirection", SortInfo.Direction.ToString() }
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

        ObservableCollectionEx<HotkeyEntry> hotkeyEntries = [];

        foreach ( SkillData skill in skills )
        {
            hotkeyEntries.Add( new HotkeyCommand
            {
                Action = ( hks, _ ) => SkillCommands.UseSkill( skill.Name ),
                Name = skill.Name
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
        // Build the replacement entries here on the packet worker (they are not yet in Items, so
        // nothing observes them), then swap them in as a single posted operation. The rebuild used
        // to be posted as one Clear plus one Add per skill, while OnSkillUpdatedEvent searched
        // Items from this same packet worker and mutated whichever entry it found - usually the
        // old entry the in-flight rebuild was about to discard, so the rebuilt entry never
        // received the update. One delegate per packet keeps the effects FIFO-ordered on the
        // dispatcher and confines every Items access to the dispatcher thread.
        List<SkillEntry> entries = [];

        foreach ( SkillInfo si in skills )
        {
            Skill skill = new()
            {
                ID = si.ID,
                Name = Skills.GetSkillName( si.ID ),
                Invokable = Skills.IsInvokable( si.ID )
            };

            SkillEntry se = new()
            {
                Skill = skill,
                Value = si.Value,
                Base = si.BaseValue,
                Cap = si.SkillCap,
                LockStatus = si.LockStatus
            };

            entries.Add( se );
        }

        _dispatcher.Invoke( () =>
        {
            Items.Clear();

            foreach ( SkillEntry se in entries )
            {
                Items.Add( se );
            }
        } );
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
        // Look the entry up inside the posted delegate, not on the packet worker: both handlers
        // run on the same packet worker and OnSkillsListEvent posts a full rebuild, so a lookup
        // here could find the old entry the rebuild is about to discard (the update was lost) or
        // enumerate Items mid-mutation ("Collection was modified"). Posted, the lookup runs after
        // any earlier rebuild completes - the dispatcher is FIFO, so effects apply in packet
        // order - and finds the rebuilt entry.
        _dispatcher.Invoke( () =>
        {
            SkillEntry entry = Items.FirstOrDefault( se => se.Skill.ID == skillID );

            if ( entry == null )
            {
                return;
            }

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