using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClassicAssist.Launcher.Models;

namespace ClassicAssist.Launcher.ViewModels;

public class OptionsViewModel : BaseViewModel
{
    public OptionsViewModel()
    {
    }

    public OptionsViewModel( IEnumerable<PluginEntry> plugins, ClassicOptions classicOptions )
    {
        Plugins = new ObservableCollection<PluginEntry>( plugins );
        ClassicOptions = classicOptions;
    }

    public ICommand AddPluginCommand => field ??= new RelayCommandAsync( AddPlugin, o => true );

    public ClassicOptions ClassicOptions
    {
        get;
        set => SetProperty( ref field, value );
    } = new ClassicOptions();

    public bool DialogResult { get; set; }

    public ICommand CancelCommand => field ??= new RelayCommand( Cancel, o => true );

    /// <summary>Raised by OK/Cancel once DialogResult is set - matches ShardsViewModel's
    /// CloseRequested; see that type for why the window must close in response to this rather
    /// than a Click-event behaviour racing the Command's own execution.</summary>
    public event Action CloseRequested;

    /// <summary>Set by OptionsWindow's code-behind after construction; used for the file picker.</summary>
    public Window OwnerWindow { get; set; }

    public ICommand OKCommand => field ??= new RelayCommand( OK, o => true );

    public ObservableCollection<PluginEntry> Plugins
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ICommand RemovePluginCommand => field ??= new RelayCommand( RemovePlugin, o => SelectedPlugin != null );

    public PluginEntry SelectedPlugin
    {
        get;
        set => SetProperty( ref field, value );
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
            ? [new FilePickerFileType( "Executable / DLL Files" ) { Patterns = new[] { "*.dll", "*.exe" } }]
            : [FilePickerFileTypes.All];

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
        if ( obj is not PluginEntry entry )
        {
            return;
        }

        Plugins.Remove( entry );
    }
}
