using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClassicAssist.Launcher.Models;

namespace ClassicAssist.Launcher.ViewModels
{
    public class OptionsViewModel : BaseViewModel
    {
        private ICommand _addPluginCommand;
        private ICommand _cancelCommand;
        private ClassicOptions _classicOptions = new ClassicOptions();
        private ICommand _okCommand;
        private ObservableCollection<PluginEntry> _plugins = new ObservableCollection<PluginEntry>();
        private ICommand _removePluginCommand;
        private PluginEntry _selectedPlugin;

        public OptionsViewModel()
        {
        }

        public OptionsViewModel( IEnumerable<PluginEntry> plugins, ClassicOptions classicOptions )
        {
            Plugins = new ObservableCollection<PluginEntry>( plugins );
            ClassicOptions = classicOptions;
        }

        public ICommand AddPluginCommand => _addPluginCommand ?? ( _addPluginCommand = new RelayCommandAsync( AddPlugin, o => true ) );

        public ClassicOptions ClassicOptions
        {
            get => _classicOptions;
            set => SetProperty( ref _classicOptions, value );
        }

        public bool DialogResult { get; set; }

        public ICommand CancelCommand => _cancelCommand ?? ( _cancelCommand = new RelayCommand( Cancel, o => true ) );

        /// <summary>Raised by OK/Cancel once DialogResult is set - matches ShardsViewModel's
        /// CloseRequested; see that type for why the window must close in response to this rather
        /// than a Click-event behaviour racing the Command's own execution.</summary>
        public event Action CloseRequested;

        /// <summary>Set by OptionsWindow's code-behind after construction; used for the file picker.</summary>
        public Window OwnerWindow { get; set; }

        public ICommand OKCommand => _okCommand ?? ( _okCommand = new RelayCommand( OK, o => true ) );

        public ObservableCollection<PluginEntry> Plugins
        {
            get => _plugins;
            set => SetProperty( ref _plugins, value );
        }

        public ICommand RemovePluginCommand =>
            _removePluginCommand ?? ( _removePluginCommand = new RelayCommand( RemovePlugin, o => SelectedPlugin != null ) );

        public PluginEntry SelectedPlugin
        {
            get => _selectedPlugin;
            set => SetProperty( ref _selectedPlugin, value );
        }

        private void OK( object obj )
        {
            DialogResult = true;
            CloseRequested?.Invoke();
        }

        private void Cancel( object obj )
        {
            CloseRequested?.Invoke();
        }

        private async Task AddPlugin( object obj )
        {
            if ( OwnerWindow?.StorageProvider == null )
            {
                return;
            }

            // Windows-style filtering only on Windows: plugin binaries elsewhere are extensionless
            // (native .so/.dylib shims, TazUO-style scripts) and would otherwise be hidden outright.
            FilePickerFileType[] fileTypes = OperatingSystem.IsWindows()
                ? new[] { new FilePickerFileType( "Executable / DLL Files" ) { Patterns = new[] { "*.dll", "*.exe" } } }
                : new[] { FilePickerFileTypes.All };

            IReadOnlyList<IStorageFile> files = await OwnerWindow.StorageProvider.OpenFilePickerAsync( new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Select a plugin",
                FileTypeFilter = fileTypes
            } );

            if ( files.Count == 0 )
            {
                return;
            }

            string path = files[0].TryGetLocalPath();

            if ( string.IsNullOrEmpty( path ) )
            {
                return;
            }

            Plugins.Add( new PluginEntry { Name = Path.GetFileName( path ), FullPath = path } );
        }

        private void RemovePlugin( object obj )
        {
            if ( !( obj is PluginEntry entry ) )
            {
                return;
            }

            Plugins.Remove( entry );
        }
    }
}
