using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClassicAssist.Launcher.Models;
using ClassicAssist.Launcher.ViewModels;
using ClassicAssist.Launcher.Views;

namespace ClassicAssist.Launcher;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load( this );
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if ( ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop )
        {
            return;
        }

        // The --shard jump-list relaunch path may finish and shut down before any window is
        // ever shown; without this, Avalonia's default OnLastWindowClose mode would tear the
        // app down the instant it notices there is no window, mid-launch. Because of that, every
        // exit path below has to call desktop.Shutdown() itself - nothing does it implicitly.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        string shardName = ParseShardArgument( desktop.Args );

        if ( !string.IsNullOrEmpty( shardName ) )
        {
            MainViewModel headlessViewModel = new();
            headlessViewModel.RequestShutdown += () => Dispatcher.UIThread.Post( () => desktop.Shutdown( 0 ) );

            ShardEntry shard = headlessViewModel.ShardManager.VisibleShards.FirstOrDefault( e => e.Name == shardName );

            if ( shard != null )
            {
                headlessViewModel.SelectedShard = shard;
                headlessViewModel.StartCommand.Execute( null );

                base.OnFrameworkInitializationCompleted();
                return;
            }
        }

        // Deliberately just `new MainWindow()` rather than also assigning DataContext here: the
        // window's own XAML instantiates its MainViewModel and wires OwnerWindow onto that exact
        // instance in MainWindow's constructor. Overwriting DataContext afterwards would swap in
        // a second, never-wired ViewModel, leaving OwnerWindow null (this previously crashed
        // Options/Shards with ArgumentNullException on ShowDialog's "owner" parameter).
        MainWindow window = new();

        // Covers both exits: RequestShutdown->Close() after a successful launch, and the user
        // closing the window directly - either way Closed fires once and the app quits for real.
        window.Closed += ( _, _ ) => desktop.Shutdown( 0 );

        desktop.MainWindow = window;

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    ///     Deliberately separate from ClassicUO/TazUO's own "-shard &lt;int&gt;" expansion-type flag
    ///     (see ClassicOptions/MainViewModel.Start): this one is a shard *name* string, used only by
    ///     the Windows jump list to relaunch the Launcher itself into a direct, windowless start.
    /// </summary>
    private static string ParseShardArgument( string[] args )
    {
        if ( args == null )
        {
            return null;
        }

        for ( int i = 0; i < args.Length - 1; i++ )
        {
            if ( args[i] is "--shard" or "-shard" )
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
