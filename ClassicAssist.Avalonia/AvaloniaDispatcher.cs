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
using System.Threading.Tasks;
using Avalonia.Threading;

namespace ClassicAssist.Avalonia;

/// <summary>
///     <see cref="IDispatcher" /> over Avalonia's UI dispatcher. Note that <see cref="Invoke" />
///     posts and returns immediately - it is not WPF's synchronous <c>Dispatcher.Invoke</c> despite
///     the name. <see cref="InvokeSynchronous" /> is.
/// </summary>
public class AvaloniaDispatcher : IDispatcher
{
    private readonly Dispatcher _dispatcher;

    public AvaloniaDispatcher( Dispatcher dispatcher )
    {
        _dispatcher = dispatcher;
    }

    /// <summary>
    ///     Posts <paramref name="action" /> to the UI thread and returns immediately - it has NOT run
    ///     by the time this returns.
    ///     <para>
    ///         This is not equivalent to WPF's <c>Dispatcher.Invoke</c>, which blocks until the
    ///         delegate completes: this is a <c>Post</c> under the hood, so callers must not assume
    ///         any effect of <paramref name="action" /> is visible on return. Delegates posted this
    ///         way run in the order they were posted. Use <see cref="InvokeSynchronous" /> where
    ///         completion before returning matters, or <see cref="InvokeAsync" /> to await it.
    ///     </para>
    /// </summary>
    public void Invoke( Action action )
    {
        _dispatcher.Post( action );
    }

    /// <summary>
    ///     Runs <paramref name="action" /> on the UI thread and returns only once it has completed -
    ///     the synchronous behaviour WPF's <c>Dispatcher.Invoke</c> has and <see cref="Invoke" /> does
    ///     not. Runs inline when the caller is already on the UI thread, so it cannot self-deadlock;
    ///     from any other thread it blocks, so never call it from a thread the UI thread is itself
    ///     waiting on.
    /// </summary>
    public void InvokeSynchronous( Action action )
    {
        _dispatcher.Invoke( action );
    }

    public Task InvokeAsync( Action action )
    {
        return _dispatcher.InvokeAsync( action ).GetTask();
    }

    public bool CheckAccess()
    {
        return _dispatcher.CheckAccess();
    }
}