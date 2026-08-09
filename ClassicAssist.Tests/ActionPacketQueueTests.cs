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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Covers the queue behaviour driven by <see cref="Options" />. Both the ActionDelay toggle and the
    ///     UseObjectQueue cap were settings the queue used to ignore entirely.
    /// </summary>
    [TestClass]
    public class ActionPacketQueueTests
    {
        private Options _originalOptions;

        [TestInitialize]
        public void SetUp()
        {
            _originalOptions = Options.CurrentOptions;
            Options.CurrentOptions = new Options();

            ActionPacketQueue.Clear();

            // The delay gate is measured from the last packet the queue sent, which earlier tests in this
            // process may have set.
            Engine.LastActionPacket = DateTime.MinValue;
        }

        [TestCleanup]
        public void TearDown()
        {
            ActionPacketQueue.Clear();
            Options.CurrentOptions = _originalOptions;
        }

        [TestMethod]
        public void WillApplyActionDelayWhenEnabled()
        {
            Options.CurrentOptions.ActionDelay = true;
            Options.CurrentOptions.ActionDelayMS = 400;

            // Establish a recent "last packet" so the next queued item has to wait out the full delay.
            Engine.LastActionPacket = DateTime.Now;

            Stopwatch sw = Stopwatch.StartNew();

            bool result = ActionPacketQueue.EnqueueAction( 0, _ => true ).Result;

            sw.Stop();

            Assert.IsTrue( result );
            Assert.IsTrue( sw.ElapsedMilliseconds >= 300, $"Ran after only {sw.ElapsedMilliseconds}ms, delay not applied" );
        }

        [TestMethod]
        public void WillNotApplyActionDelayWhenDisabled()
        {
            Options.CurrentOptions.ActionDelay = false;
            Options.CurrentOptions.ActionDelayMS = 5000;

            Engine.LastActionPacket = DateTime.Now;

            Stopwatch sw = Stopwatch.StartNew();

            bool result = ActionPacketQueue.EnqueueAction( 0, _ => true ).Result;

            sw.Stop();

            Assert.IsTrue( result );
            Assert.IsTrue( sw.ElapsedMilliseconds < 1000, $"Took {sw.ElapsedMilliseconds}ms, delay applied despite being disabled" );
        }

        [TestMethod]
        public void WillPassArgumentsToAction()
        {
            Options.CurrentOptions.ActionDelay = false;

            string observed = null;

            bool result = ActionPacketQueue.EnqueueAction( "payload", arg =>
            {
                observed = arg;
                return true;
            } ).Result;

            Assert.IsTrue( result );
            Assert.AreEqual( "payload", observed );
        }

        [TestMethod]
        public void WillReturnActionResult()
        {
            Options.CurrentOptions.ActionDelay = false;

            Assert.IsFalse( ActionPacketQueue.EnqueueAction( 0, _ => false ).Result );
            Assert.IsTrue( ActionPacketQueue.EnqueueAction( 0, _ => true ).Result );
        }

        [TestMethod]
        public void WillNotRunCancelledAction()
        {
            Options.CurrentOptions.ActionDelay = false;

            CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            bool ran = false;

            bool result = ActionPacketQueue.EnqueueAction( 0, _ =>
            {
                ran = true;
                return true;
            }, cancellationToken: cts.Token ).Result;

            Assert.IsFalse( result );
            Assert.IsFalse( ran, "Action ran despite a cancelled token" );
        }

        [TestMethod]
        public void WillRejectWhenObjectQueueFull()
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
            } );

            try
            {
                // Queue enough behind the blocked item to reach the cap.
                for ( int i = 0; i < 4; i++ )
                {
                    ActionPacketQueue.EnqueueAction( i, _ => true );
                }

                Assert.IsFalse( ActionPacketQueue.CheckUseObjectQueueLength() );
                Assert.IsFalse( ActionPacketQueue.EnqueueAction( 0, _ => true, checkUseObjectQueue: true ).Result );
            }
            finally
            {
                release.Set();
                blocking.Wait( 5000 );
            }
        }

        [TestMethod]
        public void WillAllowWhenObjectQueueDisabled()
        {
            Options.CurrentOptions.ActionDelay = false;
            Options.CurrentOptions.UseObjectQueue = false;
            Options.CurrentOptions.UseObjectQueueAmount = 1;

            Assert.IsTrue( ActionPacketQueue.CheckUseObjectQueueLength() );
        }

        [TestMethod]
        public void WillRaiseQueueEventsInOrder()
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
                ActionPacketQueue.EnqueueAction( 0, _ => true ).Wait( 5000 );
            }
            finally
            {
                ActionPacketQueue.ActionQueueEvent -= OnActionQueueEvent;
            }

            lock ( events )
            {
                CollectionAssert.AreEqual(
                    new List<ActionQueueEvents>
                    {
                        ActionQueueEvents.Enqueue, ActionQueueEvents.Enter, ActionQueueEvents.Execute, ActionQueueEvents.Finish
                    }, events );

                Assert.IsNotNull( seen );
                Assert.IsNotNull( seen.TimeSpan );
                Assert.AreEqual( nameof( WillRaiseQueueEventsInOrder ), seen.Caller );
            }
        }
    }
}
