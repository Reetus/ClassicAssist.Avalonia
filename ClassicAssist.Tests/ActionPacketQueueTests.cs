#region License

// Copyright (C) 2026 Reetus
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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Data;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.UO.Network;
using Xunit;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Covers the queue behaviour driven by <see cref="Options" />. Both the ActionDelay toggle and the
    ///     UseObjectQueue cap were settings the queue used to ignore entirely.
    /// </summary>
    public class ActionPacketQueueTests : IDisposable
    {
        private Options _originalOptions;

        public ActionPacketQueueTests()
        {
            _originalOptions = Options.CurrentOptions;
            Options.CurrentOptions = new Options();

            ActionPacketQueue.Clear();

            // The delay gate is measured from the last packet the queue sent, which earlier tests in this
            // process may have set.
            Engine.LastActionPacket = DateTime.MinValue;
        }

        public void Dispose()
        {
            ActionPacketQueue.Clear();
            Options.CurrentOptions = _originalOptions;
        }

        [Fact]
        public async Task WillApplyActionDelayWhenEnabled()
        {
            Options.CurrentOptions.ActionDelay = true;
            Options.CurrentOptions.ActionDelayMS = 400;

            // Establish a recent "last packet" so the next queued item has to wait out the full delay.
            Engine.LastActionPacket = DateTime.Now;

            Stopwatch sw = Stopwatch.StartNew();

            bool result = await ActionPacketQueue.EnqueueAction( 0, _ => true, cancellationToken: TestContext.Current.CancellationToken );

            sw.Stop();

            Assert.True( result );
            Assert.True( sw.ElapsedMilliseconds >= 300, $"Ran after only {sw.ElapsedMilliseconds}ms, delay not applied" );
        }

        [Fact]
        public async Task WillNotApplyActionDelayWhenDisabled()
        {
            Options.CurrentOptions.ActionDelay = false;
            Options.CurrentOptions.ActionDelayMS = 5000;

            Engine.LastActionPacket = DateTime.Now;

            Stopwatch sw = Stopwatch.StartNew();

            bool result = await ActionPacketQueue.EnqueueAction( 0, _ => true, cancellationToken: TestContext.Current.CancellationToken );

            sw.Stop();

            Assert.True( result );
            Assert.True( sw.ElapsedMilliseconds < 1000, $"Took {sw.ElapsedMilliseconds}ms, delay applied despite being disabled" );
        }

        [Fact]
        public async Task WillPassArgumentsToAction()
        {
            Options.CurrentOptions.ActionDelay = false;

            string observed = null;

            bool result = await ActionPacketQueue.EnqueueAction( "payload", arg =>
            {
                observed = arg;
                return true;
            }, cancellationToken: TestContext.Current.CancellationToken );

            Assert.True( result );
            Assert.Equal( "payload", observed );
        }

        [Fact]
        public async Task WillReturnActionResult()
        {
            Options.CurrentOptions.ActionDelay = false;

            Assert.False( await ActionPacketQueue.EnqueueAction( 0, _ => false, cancellationToken: TestContext.Current.CancellationToken ) );
            Assert.True( await ActionPacketQueue.EnqueueAction( 0, _ => true, cancellationToken: TestContext.Current.CancellationToken ) );
        }

        [Fact]
        public async Task WillNotRunCancelledAction()
        {
            Options.CurrentOptions.ActionDelay = false;

            CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            bool ran = false;

            bool result = await ActionPacketQueue.EnqueueAction( 0, _ =>
            {
                ran = true;
                return true;
            }, cancellationToken: cts.Token );

            Assert.False( result );
            Assert.False( ran, "Action ran despite a cancelled token" );
        }

        [Fact]
        public async Task WillRejectWhenObjectQueueFull()
        {
            Options.CurrentOptions.ActionDelay = false;
            Options.CurrentOptions.UseObjectQueue = true;
            Options.CurrentOptions.UseObjectQueueAmount = 1;

            // Hold the worker inside one item so the queue keeps a backlog while we probe it.
            ManualResetEventSlim release = new ManualResetEventSlim( false );

            Task<bool> blocking = ActionPacketQueue.EnqueueAction( 0, _ =>
            {
                release.Wait( 5000 );
                return true;
            }, cancellationToken: TestContext.Current.CancellationToken );

            try
            {
                // Queue enough behind the blocked item to reach the cap. Not awaited: they cannot
                // complete until the blocked item is released.
                for ( int i = 0; i < 4; i++ )
                {
                    _ = ActionPacketQueue.EnqueueAction( i, _ => true, cancellationToken: TestContext.Current.CancellationToken );
                }

                Assert.False( ActionPacketQueue.CheckUseObjectQueueLength() );
                Assert.False( await ActionPacketQueue.EnqueueAction( 0, _ => true, checkUseObjectQueue: true, cancellationToken: TestContext.Current.CancellationToken ) );
            }
            finally
            {
                release.Set();
                await Task.WhenAny( blocking, Task.Delay( 5000, TestContext.Current.CancellationToken ) );
            }
        }

        [Fact]
        public void WillAllowWhenObjectQueueDisabled()
        {
            Options.CurrentOptions.ActionDelay = false;
            Options.CurrentOptions.UseObjectQueue = false;
            Options.CurrentOptions.UseObjectQueueAmount = 1;

            Assert.True( ActionPacketQueue.CheckUseObjectQueueLength() );
        }

        [Fact]
        public async Task WillRaiseQueueEventsInOrder()
        {
            Options.CurrentOptions.ActionDelay = false;

            List<ActionQueueEvents> events = new List<ActionQueueEvents>();
            BaseQueueItem seen = null;

            void OnActionQueueEvent( ActionQueueEvents actionEvent, BaseQueueItem queueItem )
            {
                lock ( events )
                {
                    events.Add( actionEvent );
                    seen = queueItem;
                }
            }

            ActionPacketQueue.ActionQueueEvent += OnActionQueueEvent;

            try
            {
                await ActionPacketQueue.EnqueueAction( 0, _ => true, cancellationToken: TestContext.Current.CancellationToken )
                    .WaitAsync( TimeSpan.FromSeconds( 5 ), TestContext.Current.CancellationToken );
            }
            finally
            {
                ActionPacketQueue.ActionQueueEvent -= OnActionQueueEvent;
            }

            lock ( events )
            {
                Assert.Equal(
                    new List<ActionQueueEvents>
                    {
                        ActionQueueEvents.Enqueue, ActionQueueEvents.Enter, ActionQueueEvents.Execute, ActionQueueEvents.Finish
                    }, events );

                Assert.NotNull( seen );
                Assert.NotNull( seen.TimeSpan );
                Assert.Equal( nameof( WillRaiseQueueEventsInOrder ), seen.Caller );
            }
        }
    }
}
