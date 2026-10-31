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
using System.Linq;
using System.Threading;
using Priority_Queue;
using Sentry;

namespace ClassicAssist.Misc;

public enum QueuePriority
{
    Immediate,
    High,
    Medium,
    Low
}

public class ThreadPriorityQueue<T> : IDisposable
{
    // Upper bound on how long teardown waits for the worker to finish its in-flight action before
    // detaching it to the background - see StopThread() for why the join is bounded.
    private const int _stopJoinTimeoutMS = 5000;

    private readonly Lock _lock = new();
    private readonly Action<T> _onAction;
    private readonly SimplePriorityQueue<T> _queue = new();
    private readonly EventWaitHandle _wh = new AutoResetEvent( false );
    private readonly Thread _workerThread;

    public ThreadPriorityQueue( Action<T> onAction )
    {
        _onAction = onAction;
        _workerThread = new Thread( ProcessQueue ) { IsBackground = true };
        _workerThread.Start();
    }

    public void Dispose()
    {
        StopThread();
    }

    public int Count()
    {
        lock ( _lock )
        {
            return _queue.Count;
        }
    }

    public int Count( Predicate<T> predicate )
    {
        lock ( _lock )
        {
            return _queue.Count( predicate.Invoke );
        }
    }

    public void Clear()
    {
        lock ( _lock )
        {
            _queue.Clear();
        }
    }

    private void ProcessQueue()
    {
        while ( _workerThread.IsAlive )
        {
            bool result;
            T queueItem;

            lock ( _lock )
            {
                result = _queue.TryDequeue( out queueItem );
            }

            if ( result )
            {
                if ( queueItem == null )
                {
                    return;
                }

                try
                {
                    _onAction( queueItem );
                }
                catch ( Exception e )
                {
                    // An unhandled exception on this thread would terminate the process, and
                    // letting it escape would kill the worker and silently stop every future
                    // queued item - log it and keep processing.
                    SentrySdk.CaptureException( e );
                }
            }
            else
            {
                _wh.WaitOne();
            }
        }
    }

    public void Enqueue( T queueItem, QueuePriority priority )
    {
        lock ( _lock )
        {
            _queue.Enqueue( queueItem, (float) priority );
        }

        try
        {
            _wh.Set();
        }
        catch ( ObjectDisposedException )
        {
        }
    }

    public bool Contains( Predicate<T> predicate )
    {
        lock ( _lock )
        {
            return _queue.Any( predicate.Invoke );
        }
    }

    private void StopThread()
    {
        lock ( _lock )
        {
            _queue.Enqueue( default, (float) QueuePriority.Immediate );
        }

        try
        {
            _wh.Set();
        }
        catch ( ObjectDisposedException )
        {
        }

        // Bounded join: teardown is often called from the UI thread (a viewer's Cleanup runs from its
        // window's OnClosed), and the in-flight action can be a long loop that has not yet noticed its
        // cancellation, so an unconditional Join() would freeze the caller for the remainder of the
        // action. The sentinel above outranks everything still queued (Immediate beats every other
        // priority), so once the in-flight action returns the worker exits without starting anything
        // new; give it _stopJoinTimeoutMS to unwind, otherwise leave it to finish in the background -
        // it is a background thread, its callback is exception-wrapped, and a missed join only defers
        // teardown. Only close the wait handle once the worker is gone, so a still-running worker
        // cannot hit ObjectDisposedException in WaitOne().
        if ( _workerThread.Join( _stopJoinTimeoutMS ) )
        {
            _wh.Close();
        }
    }
}