using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO;
using ClassicAssist.UI.Misc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.UI.ViewModels
{
    public class MacrosTabViewModel : HotkeyEntryViewModel<MacroEntry>, ISettingProvider
    {
        private const int FILE_SCAN_INTERVAL_SECONDS = 5;
        private readonly Dictionary<string, DateTime> _fileSyncTimes =
            new Dictionary<string, DateTime>( StringComparer.OrdinalIgnoreCase );
        private readonly Timer _fileScanTimer;
        private readonly MacroManager _manager;
        private int _caretPosition;

        private ICommand _clearHotkeyCommand;
        private ICommand _createMacroButtonCommand;

        //TODO
        //private TextDocument _document;
        private ICommand _executeCommand;
        private ObservableCollectionEx<MacroEntry> _filterItems;
        private string _filterText;
        private ICommand _inspectObjectCommand;
        private bool _isFilterOpen;
        private bool _isRecording;
        private RelayCommand _newMacroCommand;
        private ICommand _openExternalCommand;
        private ICommand _openMacrosFolderCommand;
        private ICommand _openModulesFolderCommand;
        private ICommand _recordCommand;
        private RelayCommand _removeMacroCommand;
        private ICommand _removeMacroConfirmCommand;
        private ICommand _resetImportCacheCommand;
        private ICommand _saveMacroCommand;
        private bool _scanning;
        private bool _searching;
        private MacroEntry _selectedItem;
        private ICommand _showActiveObjectsWindowCommand;
        private ICommand _showCommandsCommand;
        private ICommand _showMacrosWikiCommand;
        private ICommand _stopCommand;
        private ICommand _toggleSearchCommand;

        public MacrosTabViewModel() : base( Strings.Macros )
        {
            Engine.DisconnectedEvent += OnDisconnectedEvent;
            Engine.ConnectedEvent += OnConnectedEvent;

            _manager = MacroManager.GetInstance();

            _manager.IsRecording = () => _isRecording;
            _manager.InsertDocument = str => { _dispatcher.Invoke( () => { SelectedItem.Macro += str; } ); };
            _manager.NewMacro = NewMacro;
            _manager.Items = Items;

            _filterItems = Items;

            Items.CollectionChanged += ( s, ea ) => UpdateFilteredItems();

            _fileScanTimer = new Timer( _ => FileScanTimerTick(), null, Timeout.Infinite, Timeout.Infinite );
        }

        public int CaretPosition
        {
            get => _caretPosition;
            set => SetProperty( ref _caretPosition, value );
        }

        public ICommand ClearHotkeyCommand =>
            _clearHotkeyCommand ?? ( _clearHotkeyCommand = new RelayCommand( ClearHotkey, o => SelectedItem != null ) );

        public ICommand CreateMacroButtonCommand =>
            _createMacroButtonCommand ?? ( _createMacroButtonCommand = new RelayCommand( CreateMacroButton,
                o => Engine.Connected && Engine.ReflectionAvailable ) );

        //TODO
        //public TextDocument Document
        //{
        //    get => _document;
        //    set => SetProperty( ref _document, value );
        //}

        public ICommand ExecuteCommand =>
            _executeCommand ?? ( _executeCommand = new RelayCommandAsync( obj => Execute( obj, null ),
                o => !IsRecording && SelectedItem != null && !SelectedItem.IsRunning ) );

        public ObservableCollectionEx<MacroEntry> FilterItems
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

        //TODO
        public ICommand InspectObjectCommand =>
            _inspectObjectCommand ?? ( _inspectObjectCommand = new RelayCommandAsync( InspectObject, o => true ) );

        public bool IsFilterOpen
        {
            get => _isFilterOpen;
            set => SetProperty( ref _isFilterOpen, value );
        }

        public bool IsRecording
        {
            get => _isRecording;
            set => SetProperty( ref _isRecording, value );
        }

        public RelayCommand NewMacroCommand =>
            _newMacroCommand ??
            ( _newMacroCommand = new RelayCommand( NewMacro, o => !SelectedItem?.IsRunning ?? true ) );

        public ICommand OpenExternalCommand =>
            _openExternalCommand ?? ( _openExternalCommand =
                new RelayCommandAsync( OpenExternal,
                    o => o != null && ( !( o is MacroEntry macroEntry ) || !macroEntry.IsRunning ) ) );

        public ICommand OpenMacrosFolderCommand =>
            _openMacrosFolderCommand ?? ( _openMacrosFolderCommand = new RelayCommand( OpenMacrosFolder, o => true ) );

        public ICommand OpenModulesFolderCommand =>
            _openModulesFolderCommand ??
            ( _openModulesFolderCommand = new RelayCommand( OpenModulesFolder, o => true ) );

        public ICommand RecordCommand =>
            _recordCommand ?? ( _recordCommand = new RelayCommand( Record, o => SelectedItem != null ) );

        public string RecordLabel => IsRecording ? Strings.Stop : Strings.Record;

        public RelayCommand RemoveMacroCommand =>
            _removeMacroCommand ?? ( _removeMacroCommand =
                new RelayCommand( RemoveMacro, o => !SelectedItem?.IsRunning ?? SelectedItem != null ) );

        public ICommand RemoveMacroConfirmCommand =>
            _removeMacroConfirmCommand ?? ( _removeMacroConfirmCommand =
                new RelayCommand( RemoveMacroConfirm, o => SelectedItem != null ) );

        public ICommand ResetImportCacheCommand =>
            _resetImportCacheCommand ?? ( _resetImportCacheCommand =
                new RelayCommand( ResetImportCache, o => SelectedItem != null && !SelectedItem.IsRunning ) );

        public ICommand SaveMacroCommand =>
            _saveMacroCommand ?? ( _saveMacroCommand = new RelayCommand( SaveMacro, o => true ) );

        public MacroEntry SelectedItem
        {
            get => _selectedItem;
            set
            {
                if ( _selectedItem != null )
                {
                    _selectedItem.PropertyChanged -= OnSelectedItemPropertyChanged;
                }

                SetProperty( ref _selectedItem, value );

                if ( _selectedItem != null )
                {
                    _selectedItem.PropertyChanged += OnSelectedItemPropertyChanged;
                }

                NotifyPropertyChanged( nameof( Hotkey ) );
            }
        }

        public ICommand ShowActiveObjectsWindowCommand =>
            _showActiveObjectsWindowCommand ?? ( _showActiveObjectsWindowCommand =
                new RelayCommand( ShowActiveObjectsWindow, o => true ) );

        public ICommand ShowCommandsCommand =>
            _showCommandsCommand ?? ( _showCommandsCommand = new RelayCommand( ShowCommands, o => true ) );

        public ICommand ShowMacrosWikiCommand =>
            _showMacrosWikiCommand ?? ( _showMacrosWikiCommand = new RelayCommand( ShowMacrosWiki, o => true ) );

        //public ICommand StopCommand =>
        //    _stopCommand ?? ( _stopCommand = new RelayCommandAsync( Stop, o => SelectedItem?.IsRunning ?? false ) );

        public ICommand StopCommand =>
            _stopCommand ?? ( _stopCommand =
                new RelayCommandAsync( Stop, o => SelectedItem != null && SelectedItem.IsRunning ) );

        public ICommand ToggleSearchCommand =>
            _toggleSearchCommand ?? ( _toggleSearchCommand = new RelayCommand( ToggleSearch, o => true ) );

        public void Serialize( JObject json )
        {
            JObject macros = new JObject();

            JArray macroArray = new JArray();

            // Persist file-backed macro content to their .py files before serialising the entries
            // so that a failed write can fall back to embedding the content in the profile.
            foreach ( MacroEntry entry in Items.Where( e => e.IsFileBacked ).ToList() )
            {
                if ( entry.BackingFileReadFailed )
                {
                    // The file's content was never loaded - don't write over it with nothing.
                    continue;
                }

                try
                {
                    string dir = Path.GetDirectoryName( entry.FilePath );

                    if ( !string.IsNullOrEmpty( dir ) )
                    {
                        Directory.CreateDirectory( dir );
                    }

                    File.WriteAllText( entry.FilePath, entry.Macro );

                    // Remember our own write so the folder scan doesn't treat it as an external edit.
                    _fileSyncTimes[entry.FilePath] = File.GetLastWriteTimeUtc( entry.FilePath );
                    entry.BackingFileWritePending = false;
                }
                catch
                {
                    // Couldn't write the file - embed the content in the profile instead so the
                    // edit isn't lost, and retry the file write on the next save.
                    entry.BackingFileWritePending = true;
                }
            }

            IEnumerable<MacroEntry> globalMacros = Items.Where( e => e.Global );

            if ( globalMacros.Any() )
            {
                JArray globalArray = new JArray();

                foreach ( MacroEntry macroEntry in globalMacros )
                {
                    globalArray.Add( macroEntry.ToJObject() );
                }

                File.WriteAllText( Path.Combine( AssistantOptions.GetGlobalPath(), "Macros.json" ),
                    globalArray.ToString( Formatting.Indented ) );
            }

            foreach ( MacroEntry macroEntry in Items.Where( e => !e.Global ) )
            {
                macroArray.Add( macroEntry.ToJObject() );
            }

            macros.Add( "Macros", macroArray );
            macros.Add( "Selected", SelectedItem?.Name );

            JArray aliasArray = new JArray();

            foreach ( JObject entry in AliasCommands.GetAllAliases()
                .Select( kvp => new JObject { { "Name", kvp.Key }, { "Value", kvp.Value } } ) )
            {
                aliasArray.Add( entry );
            }

            macros.Add( "Alias", aliasArray );

            JArray playerAliasArray = new JArray();

            foreach ( KeyValuePair<int, Dictionary<string, int>> kvp in AliasCommands.GetAllPlayerAliases() )
            {
                JArray playerAliasEntries = new JArray();

                foreach ( KeyValuePair<string, int> alias in kvp.Value )
                {
                    playerAliasEntries.Add( new JObject { { "Name", alias.Key }, { "Value", alias.Value } } );
                }

                playerAliasArray.Add( new JObject { { "Serial", kvp.Key }, { "Aliases", playerAliasEntries } } );
            }

            macros.Add( "PlayerAliases", playerAliasArray );

            json?.Add( "Macros", macros );
        }

        public void Deserialize( JObject json, Options options )
        {
            _fileScanTimer.Change( Timeout.Infinite, Timeout.Infinite );
            _fileSyncTimes.Clear();

            SelectedItem = null;

            Items.Clear();

            string globalPath = Path.Combine( AssistantOptions.GetGlobalPath(), "Macros.json" );

            if ( File.Exists( globalPath ) )
            {
                JArray globalJson = JArray.Parse( File.ReadAllText( globalPath ) );

                foreach ( JToken token in globalJson )
                {
                    MacroEntry entry = new MacroEntry( token );

                    // Globals are stored in their own file and are global regardless of what the
                    // persisted flag says - force it so a foreign/older file can't quietly demote a
                    // global macro into a per-profile one on the next save.
                    entry.Global = true;

                    // File-backed macro whose file has been removed - drop the entry, unless its
                    // content is still waiting to be written back to the file.
                    if ( entry.IsFileBacked && !entry.BackingFileWritePending && !File.Exists( entry.FilePath ) )
                    {
                        continue;
                    }

                    entry.Action = async ( hks, parameters ) => await Execute( entry, parameters );
                    entry.Hotkey = new ShortcutKeys( token["Keys"] );

                    if ( Options.CurrentOptions.SortMacrosAlphabetical )
                    {
                        Items.AddSorted( entry );
                    }
                    else
                    {
                        Items.Add( entry );
                    }
                }
            }

            JToken config = json?["Macros"];

            if ( config?["Macros"] != null )
            {
                foreach ( JToken token in config["Macros"] )
                {
                    MacroEntry entry = new MacroEntry( token );
                    entry.Global = false;

                    // File-backed macro whose file has been removed - drop the entry, unless its
                    // content is still waiting to be written back to the file.
                    if ( entry.IsFileBacked && !entry.BackingFileWritePending && !File.Exists( entry.FilePath ) )
                    {
                        continue;
                    }

                    // Global macros take precedence for hotkey
                    ShortcutKeys hotkey = new ShortcutKeys( token["Keys"] );

                    if ( !Items.Any( e => e.Global && Equals( e.Hotkey, hotkey ) ) )
                    {
                        entry.Hotkey = hotkey;
                    }

                    entry.Action = async ( hks, parameters ) => await Execute( entry, parameters );

                    if ( Options.CurrentOptions.SortMacrosAlphabetical )
                    {
                        Items.AddSorted( entry );
                    }
                    else
                    {
                        Items.Add( entry );
                    }
                }
            }

            if ( config?["Alias"] != null )
            {
                foreach ( JToken token in config["Alias"] )
                {
                    AliasCommands.SetAlias( token["Name"].ToObject<string>(), token["Value"].ToObject<int>() );
                }
            }

            if ( config?["PlayerAliases"] != null )
            {
                foreach ( JToken token in config["PlayerAliases"] )
                {
                    if ( token["Serial"] == null || token["Aliases"] == null )
                    {
                        continue;
                    }

                    int serial = token["Serial"].ToObject<int>();

                    foreach ( JToken aliasToken in token["Aliases"] )
                    {
                        AliasCommands.SetPlayerSerialAlias( serial, aliasToken["Name"].ToObject<string>(),
                            aliasToken["Value"].ToObject<int>() );
                    }
                }
            }

            string modulePath = Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "Modules" );

            if ( !Directory.Exists( modulePath ) )
            {
                Directory.CreateDirectory( modulePath );
            }

            string assistantModule = Path.Combine( modulePath, "Assistant.py" );

            if ( !File.Exists( assistantModule ) )
            {
                File.WriteAllText( assistantModule, "from ClassicAssist.Shared import Engine as _Engine\nEngine = _Engine" );
            }

            // Discover any new .py files in the Macros folder and record their sync times.
            ScanMacrosFolder();

            MacroEntry selected = Items.LastOrDefault();

            if ( config?["Selected"] != null )
            {
                selected = Items.FirstOrDefault( e => e.Name == config["Selected"]?.ToObject<string>() );
            }

            SelectedItem = selected;

            // Keep watching the Macros folder for new/changed/removed files while the tab is open.
            _fileScanTimer.Change( TimeSpan.FromSeconds( FILE_SCAN_INTERVAL_SECONDS ),
                TimeSpan.FromSeconds( FILE_SCAN_INTERVAL_SECONDS ) );
        }

        private void FileScanTimerTick()
        {
            _dispatcher.Invoke( () =>
            {
                if ( _scanning )
                {
                    return;
                }

                _scanning = true;

                try
                {
                    ScanMacrosFolder();
                }
                finally
                {
                    _scanning = false;
                }
            } );
        }

        private void RemoveMacroConfirm( object obj )
        {
            if ( !( obj is MacroEntry entry ) )
            {
                return;
            }

            //TODO
            //MessageBoxResult result = MessageBox.Show( string.Format( Strings.Really_remove_macro___0___, entry.Name ),
            //    Strings.Warning, MessageBoxButton.YesNo, MessageBoxImage.Warning );

            //if ( result == MessageBoxResult.No )
            //{
            //    return;
            //}

            RemoveMacro( entry );
        }

        private static void ShowMacrosWiki( object obj )
        {
            ShellLauncher.OpenUrl( Strings.MACRO_WIKI_URL );
        }

        private void CheckOverwriteHotkey( HotkeyEntry selectedItem, ShortcutKeys hotkey )
        {
            HotkeyEntry conflict = null;

            foreach ( HotkeyEntry hotkeyEntry in HotkeyManager.GetInstance().Items )
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
                //TODO
                //MessageBoxResult result =
                //    MessageBox.Show( string.Format( Strings.Overwrite_existing_hotkey___0____, conflict ),
                //        Strings.Warning, MessageBoxButton.YesNo );

                //if ( result == MessageBoxResult.No )
                //{
                //    NotifyPropertyChanged( nameof( Hotkey ) );
                //    return;
                //}
            }

            SelectedItem.Hotkey = hotkey;
            NotifyPropertyChanged( nameof( Hotkey ) );
        }

        private static void SaveMacro( object obj )
        {
            //Saves whole profile, think of better way
            Options.Save( Options.CurrentOptions );
        }

        private async Task Execute( object obj, object[] parameters )
        {
            if ( !( obj is MacroEntry entry ) )
            {
                return;
            }

            _manager.Execute( entry, parameters );

            await Task.CompletedTask;
        }

        private static void CreateMacroButton( object obj )
        {
            if ( !( obj is MacroEntry macro ) )
            {
                return;
            }

            ReflectionCommands.CreateMacroButton( macro.Name, macro.Name );
        }

        private static void ShowActiveObjectsWindow( object obj )
        {
            // Non-modal: it's a live inspector meant to sit beside the editor while macros run.
            Engine.UIInvoker.Invoke( "ActiveObjectsWindow" );
        }

        private void OnDisconnectedEvent()
        {
            _manager.StopAll();
            NotifyPropertyChanged( nameof( CreateMacroButtonCommand ) );
        }

        private void OnConnectedEvent()
        {
            NotifyPropertyChanged( nameof( CreateMacroButtonCommand ) );
        }

        /// <summary>
        ///     ExecuteCommand/StopCommand/NewMacroCommand's CanExecute all key off SelectedItem.IsRunning,
        ///     but that flips on a MacroEntry the ViewModel doesn't otherwise listen to - nothing re-queries
        ///     CanExecute when it changes, so the Play/Stop buttons only caught up whenever some other
        ///     ViewModel property change happened to trigger BaseViewModel's CanExecute sweep (e.g.
        ///     reselecting the macro). Re-raising a ViewModel-level change here makes that sweep run as soon
        ///     as the macro actually starts or stops.
        /// </summary>
        private void OnSelectedItemPropertyChanged( object sender, PropertyChangedEventArgs e )
        {
            if ( e.PropertyName == nameof( MacroEntry.IsRunning ) )
            {
                NotifyPropertyChanged( nameof( SelectedItem ) );
            }
        }

        private void ClearHotkey( object obj )
        {
            if ( !( obj is MacroEntry entry ) )
            {
                return;
            }

            entry.Hotkey = ShortcutKeys.Default;

            NotifyPropertyChanged( nameof( Hotkey ) );
        }

        private static async Task InspectObject( object arg )
        {
            await Commands.InspectObjectAsync();
        }

        private void NewMacro( object obj )
        {
            int count = Items.Count;

            MacroEntry macro = new MacroEntry { Name = $"Macro-{count + 1}", Macro = string.Empty };

            macro.Action = async ( hks, parameters ) => await Execute( macro, parameters );

            Items.Add( macro );

            SelectedItem = macro;
        }

        private void NewMacro( string name, string macroText )
        {
            MacroEntry macro = new MacroEntry { Name = name, Macro = macroText };

            macro.Action = async ( hks, parameters ) => await Execute( macro, parameters );

            Items.Add( macro );

            SelectedItem = macro;
        }

        private void RemoveMacro( object obj )
        {
            if ( obj is MacroEntry entry )
            {
                Items.Remove( entry );
            }
        }

        private async Task Stop( object obj )
        {
            if ( !( obj is MacroEntry entry ) )
            {
                return;
            }

            entry.Stop();

            await Task.CompletedTask;
        }

        private void ShowCommands( object obj )
        {
            //TODO UI
            //MacrosCommandWindow window = new MacrosCommandWindow { DataContext = new MacrosCommandViewModel( this ) };
            //window.ShowDialog();
        }

        private void Record( object obj )
        {
            if ( IsRecording )
            {
                IsRecording = false;
                NotifyPropertyChanged( nameof( RecordLabel ) );
                return;
            }

            IsRecording = true;
            NotifyPropertyChanged( nameof( RecordLabel ) );
        }

        private void ToggleSearch( object obj )
        {
            IsFilterOpen = !IsFilterOpen;
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
                // Reuse the real MacroEntry instances so selection and hotkeys keep working through
                // the filtered view.
                ObservableCollectionEx<MacroEntry> items = new ObservableCollectionEx<MacroEntry>();

                foreach ( MacroEntry entry in Items.Where( e =>
                    e.Name?.ToLower().Contains( FilterText.ToLower() ) ?? false ) )
                {
                    items.Add( entry );
                }

                FilterItems = items;
            }

            _searching = false;
        }

        private static void ResetImportCache( object obj )
        {
            MacroInvoker.ResetImportCache();
        }

        /// <summary>
        ///     Opens the macro in VS Code. A file-backed macro opens its real .py directly - the folder
        ///     scan picks up external edits, so no wait is needed. An in-memory macro is written to a temp
        ///     file opened with <c>--wait</c>, and the edited content is read back when the tab is closed.
        /// </summary>
        private static async Task OpenExternal( object obj )
        {
            if ( !( obj is MacroEntry macroEntry ) )
            {
                return;
            }

            if ( macroEntry.IsFileBacked )
            {
                ShellLauncher.OpenInVSCode( macroEntry.FilePath );

                return;
            }

            string tempPath = Path.Combine( Path.GetTempPath(),
                $"{SanitizeFileName( macroEntry.Name )}_{Guid.NewGuid():N}.py" );

            try
            {
                File.WriteAllText( tempPath, macroEntry.Macro );

                Process process = ShellLauncher.OpenInVSCode( tempPath, true );

                if ( process == null )
                {
                    // Opened with the desktop's default handler instead, which we can't wait on - leave
                    // the temp file in place rather than deleting it out from under the editor.
                    return;
                }

                await ShellLauncher.WaitForExitAsync( process );

                if ( File.Exists( tempPath ) )
                {
                    macroEntry.Macro = File.ReadAllText( tempPath );
                }
            }
            catch ( Exception )
            {
                // ignored - opening the editor failed
            }
            finally
            {
                try
                {
                    File.Delete( tempPath );
                }
                catch ( Exception )
                {
                    // we tried
                }
            }
        }

        /// <summary>Macro names are free text and end up in a temp filename.</summary>
        private static string SanitizeFileName( string name )
        {
            return string.IsNullOrWhiteSpace( name )
                ? "macro"
                : string.Join( "_", name.Split( Path.GetInvalidFileNameChars() ) );
        }

        private void OpenMacrosFolder( object obj )
        {
            ShellLauncher.OpenFolder( Path.Combine( AssistantOptions.GetGlobalPath(), "Macros" ) );
        }

        private static void OpenModulesFolder( object obj )
        {
            ShellLauncher.OpenFolder( Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory,
                "Modules" ) );
        }

        private void ScanMacrosFolder()
        {
            string macrosFolder = Path.Combine( AssistantOptions.GetGlobalPath(), "Macros" );

            string[] files;

            try
            {
                Directory.CreateDirectory( macrosFolder );
                files = Directory.GetFiles( macrosFolder, "*.py" );
            }
            catch
            {
                // Folder temporarily inaccessible - leave entries untouched.
                return;
            }

            HashSet<string> seen = new HashSet<string>( files, StringComparer.OrdinalIgnoreCase );

            foreach ( string filePath in files )
            {
                DateTime lastWrite;

                try
                {
                    lastWrite = File.GetLastWriteTimeUtc( filePath );
                }
                catch
                {
                    continue;
                }

                MacroEntry entry = Items.FirstOrDefault( e => e.IsFileBacked &&
                    e.FilePath.Equals( filePath, StringComparison.OrdinalIgnoreCase ) );

                if ( entry == null )
                {
                    // New file on disk - add a file-backed macro for it.
                    string content = TryReadAllText( filePath );

                    if ( content == null )
                    {
                        continue;
                    }

                    MacroEntry newEntry = new MacroEntry
                    {
                        Name = Path.GetFileNameWithoutExtension( filePath ), FilePath = filePath, Macro = content
                    };

                    newEntry.Action = async ( hks, parameters ) => await Execute( newEntry, parameters );

                    if ( Options.CurrentOptions.SortMacrosAlphabetical )
                    {
                        Items.AddSorted( newEntry );
                    }
                    else
                    {
                        Items.Add( newEntry );
                    }

                    _fileSyncTimes[filePath] = lastWrite;
                }
                else if ( entry.BackingFileWritePending )
                {
                    // The in-memory content is newer than the file (failed write) - don't reload over it.
                }
                else if ( !_fileSyncTimes.TryGetValue( filePath, out DateTime synced ) || lastWrite != synced )
                {
                    // File changed on disk since we last synced it (external edit) - reload content.
                    string content = TryReadAllText( filePath );

                    if ( content == null )
                    {
                        continue;
                    }

                    if ( content != entry.Macro )
                    {
                        entry.Macro = content;
                    }

                    entry.BackingFileReadFailed = false;
                    _fileSyncTimes[filePath] = lastWrite;
                }
            }

            // Remove file-backed entries whose file has been deleted (never while running, nor
            // while their content is still waiting to be written back to the file).
            foreach ( MacroEntry entry in Items.Where( e =>
                         e.IsFileBacked && !e.IsRunning && !e.BackingFileWritePending && !seen.Contains( e.FilePath ) ).ToList() )
            {
                _fileSyncTimes.Remove( entry.FilePath );

                if ( ReferenceEquals( SelectedItem, entry ) )
                {
                    SelectedItem = null;
                }

                Items.Remove( entry );
            }
        }

        private static string TryReadAllText( string path )
        {
            try
            {
                return File.ReadAllText( path );
            }
            catch
            {
                return null;
            }
        }
    }
}
