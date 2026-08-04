#region License

// Copyright (C) 2026 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO;
using ClassicAssist.UI.Misc;

namespace ClassicAssist.UI.ViewModels
{
    /// <summary>
    ///     Live view of everything the macro engine is holding: global/instance/player aliases, lists,
    ///     timers and the ignore list. Ported from the WPF tree's <c>ActiveObjectsViewModel</c>.
    ///     <para>
    ///         Everything is a snapshot refreshed on demand rather than bound to the underlying stores -
    ///         those are plain dictionaries mutated from macro threads, so binding to them directly would
    ///         mean cross-thread collection changes.
    ///     </para>
    /// </summary>
    public class ActiveObjectsViewModel : BaseViewModel
    {
        private ICommand _clearAllAliasesCommand;
        private ICommand _clearAllListsCommand;
        private ICommand _clearAllPlayerAliasesCommand;
        private ICommand _clearIgnoreListCommand;
        private ObservableCollection<int> _ignoreEntries = new ObservableCollection<int>();
        private ICommand _refreshAliasesCommand;
        private ICommand _refreshIgnoreListCommand;
        private ICommand _refreshInstanceAliasesCommand;
        private ICommand _refreshListsCommand;
        private ICommand _refreshPlayerAliasesCommand;
        private ICommand _refreshTimersCommand;
        private ICommand _removeAliasCommand;
        private ICommand _removeIgnoreEntryCommand;
        private ICommand _removeInstanceAliasCommand;
        private ICommand _removeListCommand;
        private ICommand _removePlayerAliasCommand;
        private AliasEntry _selectedAlias;
        private int _selectedIgnoreEntry;
        private InstanceAliasEntry _selectedInstanceAlias;
        private ListEntry _selectedList;
        private AliasEntry _selectedPlayerAlias;
        private TimerData _selectedTimer;
        private ICommand _setAliasCommand;
        private ICommand _setPlayerAliasCommand;
        private ObservableCollection<TimerData> _timers = new ObservableCollection<TimerData>();

        public ActiveObjectsViewModel()
        {
            RefreshAll();
        }

        public ObservableCollection<AliasEntry> Aliases { get; } = new ObservableCollection<AliasEntry>();

        public ICommand ClearAllAliasesCommand =>
            _clearAllAliasesCommand ?? ( _clearAllAliasesCommand = new RelayCommand( ClearAllAliases, o => true ) );

        public ICommand ClearAllListsCommand =>
            _clearAllListsCommand ?? ( _clearAllListsCommand = new RelayCommand( ClearAllLists, o => true ) );

        public ICommand ClearAllPlayerAliasesCommand =>
            _clearAllPlayerAliasesCommand ?? ( _clearAllPlayerAliasesCommand =
                new RelayCommand( ClearAllPlayerAliases, o => true ) );

        public ICommand ClearIgnoreListCommand =>
            _clearIgnoreListCommand ?? ( _clearIgnoreListCommand = new RelayCommand( ClearIgnoreList, o => true ) );

        public ObservableCollection<int> IgnoreEntries
        {
            get => _ignoreEntries;
            set => SetProperty( ref _ignoreEntries, value );
        }

        public ObservableCollection<InstanceAliasEntry> InstanceAliases { get; } =
            new ObservableCollection<InstanceAliasEntry>();

        public ObservableCollection<ListEntry> Lists { get; } = new ObservableCollection<ListEntry>();

        public ObservableCollection<AliasEntry> PlayerAliases { get; } = new ObservableCollection<AliasEntry>();

        public ICommand RefreshAliasesCommand =>
            _refreshAliasesCommand ?? ( _refreshAliasesCommand = new RelayCommand( o => RefreshAliases(), o => true ) );

        public ICommand RefreshIgnoreListCommand =>
            _refreshIgnoreListCommand ??
            ( _refreshIgnoreListCommand = new RelayCommand( o => RefreshIgnoreList(), o => true ) );

        public ICommand RefreshInstanceAliasesCommand =>
            _refreshInstanceAliasesCommand ??
            ( _refreshInstanceAliasesCommand = new RelayCommand( o => RefreshInstanceAliases(), o => true ) );

        public ICommand RefreshListsCommand =>
            _refreshListsCommand ?? ( _refreshListsCommand = new RelayCommand( o => RefreshLists(), o => true ) );

        public ICommand RefreshPlayerAliasesCommand =>
            _refreshPlayerAliasesCommand ??
            ( _refreshPlayerAliasesCommand = new RelayCommand( o => RefreshPlayerAliases(), o => true ) );

        public ICommand RefreshTimersCommand =>
            _refreshTimersCommand ?? ( _refreshTimersCommand = new RelayCommand( o => RefreshTimers(), o => true ) );

        public ICommand RemoveAliasCommand =>
            _removeAliasCommand ?? ( _removeAliasCommand = new RelayCommand( RemoveAlias, o => o != null ) );

        public ICommand RemoveIgnoreEntryCommand =>
            _removeIgnoreEntryCommand ??
            ( _removeIgnoreEntryCommand = new RelayCommand( RemoveIgnoreEntry, o => o != null ) );

        public ICommand RemoveInstanceAliasCommand =>
            _removeInstanceAliasCommand ??
            ( _removeInstanceAliasCommand = new RelayCommand( RemoveInstanceAlias, o => o != null ) );

        public ICommand RemoveListCommand =>
            _removeListCommand ?? ( _removeListCommand = new RelayCommand( RemoveList, o => o != null ) );

        public ICommand RemovePlayerAliasCommand =>
            _removePlayerAliasCommand ??
            ( _removePlayerAliasCommand = new RelayCommand( RemovePlayerAlias, o => o != null ) );

        public AliasEntry SelectedAlias
        {
            get => _selectedAlias;
            set => SetProperty( ref _selectedAlias, value );
        }

        public int SelectedIgnoreEntry
        {
            get => _selectedIgnoreEntry;
            set => SetProperty( ref _selectedIgnoreEntry, value );
        }

        public InstanceAliasEntry SelectedInstanceAlias
        {
            get => _selectedInstanceAlias;
            set => SetProperty( ref _selectedInstanceAlias, value );
        }

        public ListEntry SelectedList
        {
            get => _selectedList;
            set => SetProperty( ref _selectedList, value );
        }

        public AliasEntry SelectedPlayerAlias
        {
            get => _selectedPlayerAlias;
            set => SetProperty( ref _selectedPlayerAlias, value );
        }

        public TimerData SelectedTimer
        {
            get => _selectedTimer;
            set => SetProperty( ref _selectedTimer, value );
        }

        public ICommand SetAliasCommand =>
            _setAliasCommand ?? ( _setAliasCommand = new RelayCommandAsync( SetAlias, o => o != null ) );

        public ICommand SetPlayerAliasCommand =>
            _setPlayerAliasCommand ??
            ( _setPlayerAliasCommand = new RelayCommandAsync( SetPlayerAlias, o => o != null ) );

        public ObservableCollection<TimerData> Timers
        {
            get => _timers;
            set => SetProperty( ref _timers, value );
        }

        public void RefreshAll()
        {
            RefreshAliases();
            RefreshInstanceAliases();
            RefreshPlayerAliases();
            RefreshLists();
            RefreshTimers();
            RefreshIgnoreList();
        }

        public void RefreshAliases()
        {
            Aliases.Clear();

            foreach ( KeyValuePair<string, int> alias in AliasCommands.GetAllAliases().ToList() )
            {
                Aliases.AddSorted( new AliasEntry { Name = alias.Key, Serial = alias.Value } );
            }
        }

        public void RefreshPlayerAliases()
        {
            PlayerAliases.Clear();

            foreach ( KeyValuePair<string, int> alias in AliasCommands.GetPlayerAliases().ToList() )
            {
                PlayerAliases.AddSorted( new AliasEntry { Name = alias.Key, Serial = alias.Value } );
            }
        }

        private void RefreshInstanceAliases()
        {
            InstanceAliases.Clear();

            // Items is only assigned once MacrosTabViewModel has been constructed; this window can be
            // opened (or instantiated by the XAML designer) before that.
            ObservableCollectionEx<MacroEntry> macros = MacroManager.GetInstance().Items;

            if ( macros == null )
            {
                return;
            }

            foreach ( MacroEntry entry in macros.ToList() )
            {
                foreach ( KeyValuePair<string, int> alias in entry.Aliases.ToList() )
                {
                    InstanceAliases.Add( new InstanceAliasEntry
                    {
                        Macro = entry, Name = alias.Key, Serial = alias.Value
                    } );
                }
            }
        }

        private void RefreshLists()
        {
            Lists.Clear();

            foreach ( KeyValuePair<string, List<object>> list in ListCommands.GetAllLists().ToList() )
            {
                Lists.Add( new ListEntry { Name = list.Key, Serials = list.Value.ToArray() } );
            }
        }

        private void RefreshTimers()
        {
            Dictionary<string, OffsetStopwatch> timers = TimerCommands.GetAllTimers();

            if ( timers == null )
            {
                return;
            }

            Timers.Clear();

            foreach ( KeyValuePair<string, OffsetStopwatch> timer in timers.ToList() )
            {
                Timers.Add( new TimerData { Name = timer.Key, Value = timer.Value } );
            }
        }

        private void RefreshIgnoreList()
        {
            IgnoreEntries.Clear();

            foreach ( int serial in ObjectCommands.IgnoreList.ToList() )
            {
                IgnoreEntries.Add( serial );
            }
        }

        private void ClearAllAliases( object obj )
        {
            foreach ( string alias in AliasCommands.GetAllAliases().Keys.ToArray() )
            {
                AliasCommands.UnsetAlias( alias );
            }

            RefreshAliases();
        }

        private void ClearAllPlayerAliases( object obj )
        {
            foreach ( string alias in AliasCommands.GetPlayerAliases().Keys.ToArray() )
            {
                AliasCommands.UnsetPlayerAlias( alias );
            }

            RefreshPlayerAliases();
        }

        private void ClearAllLists( object obj )
        {
            foreach ( string list in ListCommands.GetAllLists().Keys.ToArray() )
            {
                ListCommands.RemoveList( list );
            }

            RefreshLists();
        }

        private void ClearIgnoreList( object obj )
        {
            ObjectCommands.ClearIgnoreList();

            RefreshIgnoreList();
        }

        private void RemoveAlias( object obj )
        {
            if ( !( obj is AliasEntry entry ) )
            {
                return;
            }

            AliasCommands.UnsetAlias( entry.Name );
            Aliases.Remove( entry );
        }

        private void RemovePlayerAlias( object obj )
        {
            if ( !( obj is AliasEntry entry ) )
            {
                return;
            }

            AliasCommands.UnsetPlayerAlias( entry.Name );
            PlayerAliases.Remove( entry );
        }

        private void RemoveInstanceAlias( object obj )
        {
            if ( !( obj is InstanceAliasEntry entry ) )
            {
                return;
            }

            entry.Macro.Aliases.Remove( entry.Name );

            RefreshInstanceAliases();
        }

        private void RemoveList( object obj )
        {
            if ( !( obj is ListEntry entry ) )
            {
                return;
            }

            ListCommands.RemoveList( entry.Name );
            Lists.Remove( entry );
        }

        private void RemoveIgnoreEntry( object obj )
        {
            if ( !( obj is int serial ) )
            {
                return;
            }

            ObjectCommands.IgnoreList.Remove( serial );

            RefreshIgnoreList();
        }

        private static async Task SetAlias( object arg )
        {
            if ( !( arg is AliasEntry entry ) )
            {
                return;
            }

            int serial = await Commands.GetTargetSerialAsync(
                string.Format( Strings.Target_object___0_____, entry.Name ) );

            if ( serial <= 0 )
            {
                return;
            }

            AliasCommands.SetAlias( entry.Name, serial );
        }

        private static async Task SetPlayerAlias( object arg )
        {
            if ( !( arg is AliasEntry entry ) )
            {
                return;
            }

            int serial = await Commands.GetTargetSerialAsync(
                string.Format( Strings.Target_object___0_____, entry.Name ) );

            if ( serial <= 0 )
            {
                return;
            }

            AliasCommands.SetPlayerAlias( entry.Name, serial );
        }

        public class ListEntry
        {
            public string Name { get; set; }
            public object[] Serials { get; set; }

            /// <summary>Rendered in the grid - the list contents, comma separated.</summary>
            public string Display => Serials == null ? string.Empty : string.Join( ", ", Serials );
        }
    }

    public class TimerData
    {
        public string Name { get; set; }
        public OffsetStopwatch Value { get; set; }
    }

    public class AliasEntry : IComparable<AliasEntry>
    {
        public string Name { get; set; }
        public int Serial { get; set; }

        public int CompareTo( AliasEntry other )
        {
            if ( ReferenceEquals( this, other ) )
            {
                return 0;
            }

            return other is null ? 1 : string.Compare( Name, other.Name, StringComparison.InvariantCultureIgnoreCase );
        }
    }

    public class InstanceAliasEntry
    {
        public MacroEntry Macro { get; set; }
        public string Name { get; set; }
        public int Serial { get; set; }
    }
}
