using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using ClassicAssist.Launcher.ViewModels;
using ClassicAssist.Updater.Models;
using ClassicAssist.Updater.Services;
using ClassicAssist.Updater.Views;

namespace ClassicAssist.Updater.ViewModels;

public class MainViewModel : BaseViewModel
{
    public MainViewModel() : this( false )
    {
    }

    public MainViewModel( bool testing )
    {
        UpdaterSettings = App.UpdaterSettings ?? new UpdaterSettings();
        Force = App.CurrentOptions?.Force ?? false;

        if ( testing )
        {
            return;
        }

        // Deliberately not Task.Run. BaseViewModel raises PropertyChanged and then refreshes every
        // command's CanExecute, and that second part writes IsEnabled on the bound Button - an
        // Avalonia property, which may only be touched from the UI thread. Driven from a pool thread
        // it threw inside the property setter, the exception disappeared into the unobserved task,
        // and the refresh button stayed at whatever enabled state it had when the throw happened.
        // Started here instead, every await below resumes on the UI thread; the blocking work is
        // what gets pushed off it, explicitly, and posts its output back.
        if ( Dispatcher.UIThread.CheckAccess() )
        {
            _ = RunAsync();
        }
        else
        {
            Dispatcher.UIThread.Post( () => _ = RunAsync() );
        }
    }

    public ICommand CheckForUpdateCommand =>
        field ??= new RelayCommandAsync( _ => CheckForUpdate(), _ => !IsUpdating );

    public long DownloadSize
    {
        get;
        set
        {
            SetProperty( ref field, value );
            IsIndeterminate = value == 0;
        }
    }

    public bool Force
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool IsIndeterminate
    {
        get;
        set => SetProperty( ref field, value );
    } = true;

    public bool IsUpdating
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ObservableCollection<string> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public UpdaterSettings UpdaterSettings
    {
        get;
        set => SetProperty( ref field, value );
    }

    /// <summary>
    ///     Set by the window so the "clients are running" prompt has an owner to centre on.
    /// </summary>
    public Avalonia.Controls.Window OwnerWindow { get; set; }

    private async Task RunAsync()
    {
        if ( App.CurrentOptions.Stage == UpdaterStage.Initial )
        {
            string path = await CheckForUpdate();

            if ( !string.IsNullOrEmpty( path ) )
            {
                App.CurrentOptions.UpdatePath = path;
            }
        }

        if ( string.IsNullOrEmpty( App.CurrentOptions.UpdatePath ) )
        {
            return;
        }

        await InstallAsync( App.CurrentOptions.UpdatePath, App.CurrentOptions.Path );
    }

    /// <summary>
    ///     Copies an extracted package over the install. Runs from the temp copy of the updater, not
    ///     from the install - see <see cref="Relaunch" />.
    /// </summary>
    private async Task InstallAsync( string updatePath, string installPath )
    {
        // IsUpdating is set here rather than inside the worker so it stays on the UI thread; the
        // copying itself is the only part that belongs on a pool thread.
        IsUpdating = true;

        try
        {
            await Task.Run( () => Install( updatePath, installPath ) );
        }
        finally
        {
            IsUpdating = false;
        }
    }

    private void Install( string updatePath, string installPath )
    {
        try
        {
            AddText( "Updating files..." );

            DirectoryInfo source = new( updatePath );
            DirectoryInfo destination = new( installPath );

            List<string> failList = [];

            InstallGuard.VerifyWriteAccess( source, destination, failList );

            if ( failList.Count > 0 )
            {
                AddText( "The following files were in use and cannot be overwritten, ensure all " +
                         $"instances of ClassicAssist are closed and then try again:\n{string.Join( "\n", failList )}" );

                return;
            }

            List<string> copyFailures = [];

            CopyAll( source, destination, copyFailures );

            if ( copyFailures.Count > 0 )
            {
                // The pre-flight passed and the copy still failed, so the install is now part old and
                // part new. WPF logged these among the per-file progress lines, where they scrolled
                // past unread; say plainly that the install needs re-running.
                AddText( $"Update incomplete, {copyFailures.Count} file(s) could not be written:\n" +
                         $"{string.Join( "\n", copyFailures )}\nRun the update again once nothing is using them." );

                return;
            }

            ExtractModules( updatePath, destination.FullName );

            AddText( "Done." );
        }
        catch ( Exception e )
        {
            AddText( $"Error: {e.Message}" );
        }
    }

    /// <summary>
    ///     Modules ship as a nested zip so the macro module tree can be replaced wholesale without the
    ///     per-file copy above walking it.
    /// </summary>
    private void ExtractModules( string updatePath, string destinationPath )
    {
        string modulesZip = Directory.EnumerateFiles( updatePath, "Modules.zip" ).FirstOrDefault();

        if ( string.IsNullOrEmpty( modulesZip ) )
        {
            return;
        }

        string modulesPath = Path.Combine( destinationPath, "Modules" );

        if ( !Directory.Exists( modulesPath ) )
        {
            Directory.CreateDirectory( modulesPath );
        }

        AddText( "Extracting modules..." );

        try
        {
            using ZipArchive zipFile = ZipFile.OpenRead( modulesZip );

            foreach ( ZipArchiveEntry entry in zipFile.Entries )
            {
                AddText( $"Copying {entry.FullName}..." );

                ( string basePath, string fileName ) = EnsurePathsExist( modulesPath, entry.FullName );

                if ( !string.IsNullOrEmpty( fileName ) )
                {
                    entry.ExtractToFile( Path.Combine( basePath, entry.Name ), true );
                }
            }
        }
        catch ( Exception e )
        {
            AddText( $"Could not extract modules: {e.Message}" );
        }
    }

    private static (string basePath, string fileName) EnsurePathsExist( string modulesPath, string fullName )
    {
        // Zip entries always use '/', whatever the platform that wrote them.
        string[] paths = fullName.Split( '/' );

        string basePath = modulesPath;

        for ( int i = 0; i < paths.Length - 1; i++ )
        {
            basePath = Path.Combine( basePath, paths[i] );

            if ( !Directory.Exists( basePath ) )
            {
                Directory.CreateDirectory( basePath );
            }
        }

        return ( basePath, paths[^1] );
    }

    private void CopyAll( DirectoryInfo source, DirectoryInfo target, List<string> failures )
    {
        if ( !Directory.Exists( target.FullName ) )
        {
            Directory.CreateDirectory( target.FullName );
        }

        foreach ( FileInfo fileInfo in source.GetFiles() )
        {
            try
            {
                fileInfo.CopyTo( Path.Combine( target.ToString(), fileInfo.Name ), true );
                AddText( $"Copying {fileInfo.Name}..." );
            }
            catch ( IOException e )
            {
                failures.Add( $"{fileInfo.Name}: {e.Message}" );
            }
            catch ( UnauthorizedAccessException e )
            {
                failures.Add( $"{fileInfo.Name}: {e.Message}" );
            }
        }

        foreach ( DirectoryInfo sourceDirectory in source.GetDirectories() )
        {
            DirectoryInfo nextTargetDir = target.CreateSubdirectory( sourceDirectory.Name );
            CopyAll( sourceDirectory, nextTargetDir, failures );
        }
    }

    private async Task<string> CheckForUpdate()
    {
        ClearText();

        try
        {
            IsUpdating = true;

            AddText( "Checking for latest release..." );

            ReleaseSource source = new( UpdaterSettings.ReleasesURL );

            ChangelogEntry latestRelease = await source.GetLatestRelease( UpdaterSettings.InstallPrereleases );

            if ( latestRelease == null )
            {
                AddText( $"No release with a package for {PlatformPackage.RuntimeIdentifier} was found." );

                return null;
            }

            AddText( $"Latest Release: {latestRelease.Version}" );

            string newVersion = latestRelease.Version;

            if ( !Force && !VersionHelpers.IsVersionNewer( App.CurrentOptions.CurrentVersion, newVersion ) )
            {
                AddText( "No new release available..." );

                return null;
            }

            if ( !await CloseRunningClients() )
            {
                return null;
            }

            AddText( $"Downloading {latestRelease.PackageName ?? latestRelease.DownloadURL}..." );

            string fileName = await DownloadFile( latestRelease.DownloadURL, latestRelease.DownloadSize );

            AddText( "Extracting package..." );

            string updatePath = await ExtractPackage( fileName, newVersion );

            string extractedUpdater = Path.Combine( updatePath, UpdaterExecutableName );

            if ( !File.Exists( extractedUpdater ) )
            {
                // No updater in the package, so this one can safely do the copy itself.
                return updatePath;
            }

            Relaunch( extractedUpdater, updatePath );

            return null;
        }
        catch ( Exception e )
        {
            AddText( $"Error: {e.Message}" );
        }
        finally
        {
            IsUpdating = false;
        }

        return null;
    }

    /// <summary>
    ///     Closes the process that launched the updater and anything else running out of the install,
    ///     asking first. False means the user cancelled and nothing should be touched.
    /// </summary>
    private async Task<bool> CloseRunningClients()
    {
        if ( App.CurrentOptions.PID != 0 )
        {
            try
            {
                Process.GetProcessById( App.CurrentOptions.PID ).Kill();
            }
            catch ( Exception )
            {
                // Already gone, or not ours to kill.
            }
        }

        // Enumerating every process and reading /proc is slow enough to stutter the window.
        Process[] clients = await Task.Run( () => RunningClients.Find( App.CurrentOptions.Path ) );

        if ( clients.Length == 0 )
        {
            return true;
        }

        bool proceed = await Dispatcher.UIThread.InvokeAsync( async () =>
        {
            ProcessesViewModel viewModel = new( clients );
            ProcessesView window = new() { DataContext = viewModel };

            if ( OwnerWindow != null )
            {
                await window.ShowDialog( OwnerWindow );
            }
            else
            {
                window.Show();
            }

            return viewModel.Accepted;
        } );

        if ( !proceed )
        {
            AddText( "Update cancelled..." );

            return false;
        }

        foreach ( Process process in clients )
        {
            try
            {
                process.Kill();
            }
            catch ( Exception )
            {
                // ignored
            }
        }

        // Killing is not instant; the files stay mapped for a moment after Kill returns, and on
        // Windows that is long enough for the write-access pre-flight to fail on a client that is
        // already on its way out.
        await Task.Run( () =>
        {
            foreach ( Process process in clients )
            {
                try
                {
                    process.WaitForExit( 5000 );
                }
                catch ( Exception )
                {
                    // ignored
                }
            }
        } );

        return true;
    }

    /// <summary>
    ///     Hands over to the copy of the updater inside the package. An updater cannot overwrite its
    ///     own binaries while running, so the second stage runs from the temp folder and this process
    ///     exits immediately.
    /// </summary>
    private void Relaunch( string extractedUpdater, string updatePath )
    {
        ProcessStartInfo psi = new( extractedUpdater,
            $"--stage Install --updatepath \"{updatePath}\" --path \"{App.CurrentOptions.Path}\"" )
        {
            UseShellExecute = false, WorkingDirectory = updatePath
        };

        Process.Start( psi );

        Dispatcher.UIThread.Post( () => App.Shutdown( 0 ) );
    }

    /// <summary>
    ///     The apphost has no extension outside Windows.
    /// </summary>
    internal static string UpdaterExecutableName =>
        OperatingSystem.IsWindows() ? "ClassicAssist.Updater.exe" : "ClassicAssist.Updater";

    private static async Task<string> ExtractPackage( string fileName, string newVersion )
    {
        string path = Path.Combine( Path.GetTempPath(), $"CAUpdate-{Sanitise( newVersion )}" );

        if ( Directory.Exists( path ) )
        {
            Directory.Delete( path, true );
        }

        await Task.Run( () => ZipFile.ExtractToDirectory( fileName, path ) );

        return path;
    }

    /// <summary>
    ///     Version strings become a directory name, and a GitHub tag may legitimately contain
    ///     characters a path may not.
    /// </summary>
    private static string Sanitise( string version )
    {
        return string.Join( "_", ( version ?? "unknown" ).Split( Path.GetInvalidFileNameChars() ) );
    }

    private async Task<string> DownloadFile( string browserDownloadUrl, long size )
    {
        using HttpClient http = new() { Timeout = TimeSpan.FromMinutes( 5 ) };

        http.DefaultRequestHeaders.Add( "User-Agent", "ClassicAssist Updater" );

        byte[] response = await http.GetByteArrayAsync( browserDownloadUrl );

        // A truncated download would otherwise be extracted over a working install.
        if ( size > 0 && response.Length != size && !App.CurrentOptions.Force )
        {
            throw new InvalidOperationException(
                $"Downloaded size doesn't match expected ({response.Length} vs {size})." );
        }

        // Temp rather than the current directory, which for the second stage is the package folder
        // and for the first is the install being replaced.
        string fileName = Path.Combine( Path.GetTempPath(), "ClassicAssist-Update.zip" );

        await File.WriteAllBytesAsync( fileName, response );

        return fileName;
    }

    private void AddText( string message )
    {
        Dispatcher.UIThread.Post( () => Items.Add( message ) );
    }

    private void ClearText()
    {
        Dispatcher.UIThread.Post( Items.Clear );
    }
}
