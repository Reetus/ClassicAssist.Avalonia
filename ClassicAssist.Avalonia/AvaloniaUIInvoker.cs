#region License

// Copyright (C) 2020 Reetus
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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClassicAssist.Avalonia.Views;
using ClassicAssist.Data;
using ClassicAssist.Shared;
using Engine = ClassicAssist.Avalonia.UiHost;

namespace ClassicAssist.Avalonia;

/// <summary>
///     Opens windows on behalf of view models, which live in ClassicAssist.Shared and so cannot name
///     an Avalonia type.
///     <para>
///         Everything here constructs its window inside the dispatcher callback rather than before it.
///         Avalonia's <see cref="Window" /> constructor creates the platform window and throws
///         "Call from invalid thread" off the UI thread - unlike WPF, which lets a window be built on
///         any STA thread. Callers routinely are not on the UI thread: hotkeys run under
///         <c>Task.Run</c>, and packet handlers run on whatever thread StreamJsonRpc dispatched them
///         on. Constructing first meant those callers threw before ever reaching the dispatcher, and
///         the exception died in the caller's task with no window and no message.
///     </para>
/// </summary>
public class AvaloniaUIInvoker : IUIInvoker
{
    private static readonly Dictionary<string, Type> _windowTypeCache = [];
    private static readonly Lock _windowTypeCacheLock = new();

    /// <summary>
    ///     Windows that must never have two live instances: a DebugWindow's view models subscribe
    ///     to live packet traffic and serialise settings under a fixed key, so a second copy doubles
    ///     the work and corrupts profile save.
    /// </summary>
    private static readonly HashSet<string> _singleInstanceWindowTypes = ["DebugWindow"];

    private readonly Lock _clipboardLock = new();

    private Task _clipboardTask = Task.CompletedTask;

    private readonly Dispatcher _dispatcher;

    /// <summary>
    ///     Open single-instance windows by type name. Only touched on the dispatcher thread, so it
    ///     needs no lock of its own.
    /// </summary>
    private readonly Dictionary<string, Window> _openWindows = [];

    public AvaloniaUIInvoker( Dispatcher dispatcher )
    {
        _dispatcher = dispatcher;
    }

    public Task Invoke( string typeName, object[] ctorParam = null, Type dataContextType = null,
        object[] dataContextParam = null )
    {
        Type type = FindWindowType( typeName );

        if ( type == null )
        {
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync( () =>
        {
            try
            {
                // Reuse an already-open window instead of stacking another instance of it.
                if ( _singleInstanceWindowTypes.Contains( typeName ) &&
                     _openWindows.TryGetValue( typeName, out Window openWindow ) )
                {
                    openWindow.Activate();

                    return;
                }

                Window window = (Window) Activator.CreateInstance( type, ctorParam ) ?? throw new InvalidOperationException( $"Failed to create window of type: {typeName}" );
                if ( dataContextType != null )
                {
                    window.DataContext = Activator.CreateInstance( dataContextType, dataContextParam );
                }

                window.Show();
                window.Activate();

                if ( _singleInstanceWindowTypes.Contains( typeName ) )
                {
                    _openWindows[typeName] = window;
                    window.Closed += ( _, _ ) => _openWindows.Remove( typeName );
                }
            }
            catch ( Exception e )
            {
                Report( typeName, e );
            }
        } ).GetTask();
    }

    public Task InvokeDialog<T>( string typeName, object[] ctorParam = null, T dataContext = default )
        where T : class
    {
        Type type = FindWindowType( typeName );

        if ( type == null )
        {
            return Task.CompletedTask;
        }

        TaskCompletionSource<bool> taskCompletionSource = new();

        _dispatcher.InvokeAsync( async () =>
        {
            try
            {
                // ShowDialog needs an owner; Engine.MainWindow is only assigned once startup has
                // progressed far enough. Failing here must not fault or hang the caller.
                Window owner = Engine.MainWindow ??
                               throw new InvalidOperationException( "Main window is not available yet" );

                Window window = (Window) Activator.CreateInstance( type, ctorParam ) ?? throw new InvalidOperationException( $"Failed to create window of type: {typeName}" );
                if ( dataContext != null )
                {
                    window.DataContext = dataContext;
                }

                await window.ShowDialog( owner );

                taskCompletionSource.TrySetResult( true );
            }
            catch ( Exception e )
            {
                Report( typeName, e );
            }
            finally
            {
                // Always resolve - awaiting a dialog that never opened would hang the caller
                // forever. A no-op when the dialog completed normally.
                taskCompletionSource.TrySetResult( false );
            }
        } );

        return taskCompletionSource.Task;
    }

    public async Task<HueChoice> GetHueAsync()
    {
        return await _dispatcher.InvokeAsync( async () =>
        {
            try
            {
                Window owner = Engine.MainWindow ??
                               throw new InvalidOperationException( "Main window is not available yet" );

                HuePickerWindow window = new() { Topmost = Options.CurrentOptions.AlwaysOnTop };

                await window.ShowDialog( owner );

                // SelectedHue is null until the user accepts - never report a dismissed dialog as
                // hue 0.
                return window.SelectedHue is int hue ? HueChoice.FromHue( hue ) : default( HueChoice );
            }
            catch ( Exception e )
            {
                Report( "HuePickerWindow", e );

                return default( HueChoice );
            }
        } );
    }

    public async Task<string> ShowOpenFileDialogAsync( string title, string filterName, string[] extensions )
    {
        return await _dispatcher.InvokeAsync( async () =>
        {
            try
            {
                Window owner = Engine.MainWindow ??
                               throw new InvalidOperationException( "Main window is not available yet" );

                IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = title,
                        AllowMultiple = false,
                        FileTypeFilter = [new FilePickerFileType( filterName ) { Patterns = extensions }]
                    } );

                return files.Count > 0 ? files[0].TryGetLocalPath() : null;
            }
            catch ( Exception e )
            {
                Report( "FilePicker", e );

                return null;
            }
        } );
    }

    public void SetClipboardText( string text )
    {
        lock ( _clipboardLock )
        {
            // Chained so back-to-back copies reach the clipboard in order, and so every clipboard
            // task is observed - the fire-and-forget version silently dropped both.
            _clipboardTask = SetClipboardTextAsync( _clipboardTask, text );
        }
    }

    public async Task<string> GetClipboardText()
    {
        return await _dispatcher.InvokeAsync( async () =>
        {
            // A null task means the platform has no clipboard - report "no text" instead of
            // faulting the awaiter with a NullReferenceException.
            Task<string> getText = Engine.MainWindow?.Clipboard?.TryGetTextAsync();

            return getText == null ? null : await getText;
        } );
    }

    private async Task SetClipboardTextAsync( Task previous, string text )
    {
        try
        {
            await previous;

            await _dispatcher.InvokeAsync( async () =>
            {
                IClipboard clipboard = Engine.MainWindow?.Clipboard;

                if ( clipboard != null )
                {
                    await clipboard.SetTextAsync( text );
                }
            } );
        }
        catch ( Exception e )
        {
            // Clipboard access can fail (locked clipboard, no platform clipboard) - observe it
            // here so it cannot surface as an unobserved task exception.
            Console.WriteLine( $"Failed to set clipboard text: {e}" );
        }
    }

    private static Type FindWindowType( string typeName )
    {
        Type type;

        // The assembly scan is reflection-heavy, so cache it - this runs on the caller's thread
        // every time a window opens.
        lock ( _windowTypeCacheLock )
        {
            if ( !_windowTypeCache.TryGetValue( typeName, out type ) )
            {
                type = Assembly.GetExecutingAssembly().GetTypes()
                    .FirstOrDefault( t => t.Name == typeName && t.IsSubclassOf( typeof( Window ) ) );

                _windowTypeCache[typeName] = type;
            }
        }

        if ( type == null )
        {
            // MessageBoxProvider is null until InstallRPC runs - same as Report below.
            Shared.Engine.MessageBoxProvider?.Show( $"Cannot find type: {typeName}" );
        }

        return type;
    }

    /// <summary>
    ///     A window that fails to open is otherwise completely silent - the caller is usually a hotkey
    ///     or a packet handler with nowhere to surface an exception.
    /// </summary>
    private static void Report( string typeName, Exception e )
    {
        Console.WriteLine( $"Failed to open {typeName}: {e}" );

        Shared.Engine.MessageBoxProvider?.Show( $"Failed to open {typeName}: {e.Message}" );
    }
}
