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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClassicAssist.Avalonia.Views;
using ClassicAssist.Data;
using ClassicAssist.Shared;
using Engine = ClassicAssist.Avalonia.UiHost;

namespace ClassicAssist.Avalonia
{
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
        private readonly Dispatcher _dispatcher;

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
                    Window window = (Window) Activator.CreateInstance( type, ctorParam );

                    if ( window == null )
                    {
                        throw new InvalidOperationException( $"Failed to create window of type: {typeName}" );
                    }

                    if ( dataContextType != null )
                    {
                        window.DataContext = Activator.CreateInstance( dataContextType, dataContextParam );
                    }

                    window.Show();
                    window.Activate();
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

            TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();

            _dispatcher.InvokeAsync( async () =>
            {
                try
                {
                    Window window = (Window) Activator.CreateInstance( type, ctorParam );

                    if ( window == null )
                    {
                        throw new InvalidOperationException( $"Failed to create window of type: {typeName}" );
                    }

                    if ( dataContext != null )
                    {
                        window.DataContext = dataContext;
                    }

                    await window.ShowDialog( Engine.MainWindow );

                    taskCompletionSource.TrySetResult( true );
                }
                catch ( Exception e )
                {
                    Report( typeName, e );

                    // Awaiting a dialog that never opened would hang the caller forever.
                    taskCompletionSource.TrySetResult( false );
                }
            } );

            return taskCompletionSource.Task;
        }

        public Task<int> GetHueAsync()
        {
            return _dispatcher.InvokeAsync( async () =>
            {
                HuePickerWindow window = new HuePickerWindow { Topmost = Options.CurrentOptions.AlwaysOnTop };

                await window.ShowDialog( Engine.MainWindow );

                return window.SelectedHue;
            } );
        }

        public Task<string> ShowOpenFileDialogAsync( string title, string filterName, string[] extensions )
        {
            return _dispatcher.InvokeAsync( async () =>
            {
                IReadOnlyList<IStorageFile> files = await Engine.MainWindow.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = title,
                        AllowMultiple = false,
                        FileTypeFilter = new[] { new FilePickerFileType( filterName ) { Patterns = extensions } }
                    } );

                return files.Count > 0 ? files[0].TryGetLocalPath() : null;
            } );
        }

        public void SetClipboardText( string text )
        {
            _dispatcher.InvokeAsync( () => Engine.MainWindow.Clipboard?.SetTextAsync( text ) );
        }

        public string GetClipboardText()
        {
            return _dispatcher.InvokeAsync( () => Engine.MainWindow.Clipboard?.TryGetTextAsync() ).Result;
        }

        private static Type FindWindowType( string typeName )
        {
            Type type = Assembly.GetExecutingAssembly().GetTypes()
                .FirstOrDefault( t => t.Name == typeName && t.IsSubclassOf( typeof( Window ) ) );

            if ( type == null )
            {
                Shared.Engine.MessageBoxProvider.Show( $"Cannot find type: {typeName}" );
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
}
