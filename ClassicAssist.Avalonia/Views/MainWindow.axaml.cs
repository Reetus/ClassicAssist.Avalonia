using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ClassicAssist.Avalonia.Views;

public partial class MainWindow : Window
{
    /// <summary>
    ///     Set once <see cref="OnClosed" /> has torn the tab view models down, so the walk runs at
    ///     most once even if <c>Closed</c> were ever delivered twice.
    /// </summary>
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();

        Closed += OnClosed;
    }

    /// <summary>
    ///     Refuses a close the user asked for. The assistant is a companion to the game client and
    ///     ends only when the client does: <see cref="App" /> wires the engine's shutdown to the
    ///     desktop lifetime, and the plugin's dropped RPC connection calls <see cref="Environment.Exit" />.
    ///     A close from either of those, or from the OS going down, is left alone - cancelling those
    ///     would strand the window over the client with no way to end the process.
    /// </summary>
    protected override void OnClosing( WindowClosingEventArgs e )
    {
        base.OnClosing( e );

        if ( e.IsProgrammatic )
        {
            // Close() called by this process - the lifetime's shutdown, a test, a future exit path.
            return;
        }

        if ( e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown )
        {
            return;
        }

        // Anything else is the user: the title bar's close button, Alt+F4, the taskbar's Close. They
        // are ignored so the window cannot be dismissed out from under the client. There is no
        // Avalonia API to disable the button itself - only to intercept the close.
        e.Cancel = true;
    }

    /// <summary>
    ///     Tears down the tab view models this window's XAML constructs - the tab controls declare
    ///     them as their own <c>UserControl.DataContext</c> - now that the window is really gone.
    ///     Their subscriptions into Engine events and packet handlers are process-wide statics or
    ///     singletons, so without this each handler keeps firing for the rest of the process (and
    ///     MacrosTabViewModel's file-scan timer keeps ticking).
    ///     <para>
    ///         The window's own DataContext, <c>MainWindowViewModel</c>, is deliberately NOT
    ///         disposed here. DebugWindow's teardown does dispose its window view model, but
    ///         DebugWindow.axaml constructs one per window and that window owns it; MainWindow.axaml
    ///         deliberately declares no <c>Window.DataContext</c> and the single instance is created
    ///         and owned by <see cref="App" /> for the life of the process. The rule is the same on
    ///         both windows - dispose what the window's XAML created - it is just that MainWindow's
    ///         XAML creates only the tab view models. The instance is still added to the set below
    ///         (without being disposed) so the walk cannot reach it where a tab content inherits it
    ///         as its own DataContext - AgentsTabControl declares none.
    ///     </para>
    ///     <para>
    ///         Timing and double-dispose: user closes never get here (<see cref="OnClosing" />
    ///         cancels them), <c>Closed</c> fires exactly once per window, and <c>_closed</c> makes
    ///         the teardown run at most once even so. The walk de-duplicates by reference (a tab
    ///         content can inherit the window's DataContext, exactly as in DebugWindow's walk), so
    ///         no instance is disposed twice even though every <c>Dispose</c> is idempotent anyway.
    ///     </para>
    /// </summary>
    private void OnClosed( object sender, EventArgs e )
    {
        Closed -= OnClosed;

        if ( _closed )
        {
            return;
        }

        _closed = true;

        HashSet<IDisposable> seen = new( ReferenceEqualityComparer.Instance );

        // Seeded, not disposed - see the summary above for why the window's DataContext is exempt.
        if ( DataContext is IDisposable windowViewModel )
        {
            seen.Add( windowViewModel );
        }

        DisposeTabViewModels( Content, seen );
    }

    /// <summary>
    ///     DebugWindow's tab walk, made recursive. Two things force the recursion: the window
    ///     content is a Grid that hosts the tab control rather than being one, and the Agents tab
    ///     hosts a second TabControl whose tabs are one level deeper - VendorSellTabViewModel lives
    ///     there. Layout containers are descended only to find further tab controls; disposal
    ///     happens at exactly one place, a tab content control's DataContext, which is where this
    ///     window's XAML constructs view models. Item view models in lists and grids are therefore
    ///     never touched.
    /// </summary>
    private static void DisposeTabViewModels( object node, HashSet<IDisposable> seen )
    {
        if ( node is TabControl tabControl )
        {
            foreach ( object item in tabControl.Items )
            {
                DisposeTabViewModels( item, seen );
            }
        }
        else if ( node is TabItem tabItem )
        {
            if ( tabItem.Content is Control control )
            {
                if ( control.DataContext is IDisposable tabViewModel && seen.Add( tabViewModel ) )
                {
                    tabViewModel.Dispose();
                }

                DisposeTabViewModels( control, seen );
            }
        }
        else if ( node is ContentControl contentControl )
        {
            DisposeTabViewModels( contentControl.Content, seen );
        }
        else if ( node is Decorator decorator )
        {
            DisposeTabViewModels( decorator.Child, seen );
        }
        else if ( node is Panel panel )
        {
            foreach ( Control child in panel.Children )
            {
                DisposeTabViewModels( child, seen );
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }
}