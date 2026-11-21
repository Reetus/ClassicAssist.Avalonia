using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Scavenger;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UI.ViewModels.Agents;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.Agents
{
    /// <summary>
    ///     A server can put an item on the ground for an instant on its way somewhere else. Seen with a
    ///     quest reward: 500 gold arrives as a world item (0xF3) at the player's feet and is deleted
    ///     (0x1D) moments later, once merged into the backpack's gold. The scavenger announced
    ///     "Scavenging 'gold coin'..." for it because it committed on the 0xF3 - and, running on the
    ///     packet thread, held up the 0x1D while it waited for its own drag.
    /// </summary>
    public class ScavengerTransientItemTests : IDisposable
    {
        private const int GOLD = 0x0EED;
        private const int REWARD_SERIAL = 0x42836C4F;
        private const int X = 3530;
        private const int Y = 2543;

        private readonly Options _originalOptions;
        private readonly ScavengerTabViewModel _viewModel;

        public ScavengerTransientItemTests()
        {
            _originalOptions = Options.CurrentOptions;
            Options.CurrentOptions = new Options { ActionDelay = false };
            MacroManager.QuietMode = false;

            PlayerMobile player = EngineState.ResetWithPlayer();
            player.X = X;
            player.Y = Y;

            _viewModel = new ScavengerTabViewModel();

            // Deserialize is what subscribes the scavenger to new world items.
            _viewModel.Deserialize( new JObject(), Options.CurrentOptions );
            _viewModel.Enabled = true;
            _viewModel.CheckWeight = false;
            _viewModel.Items.Add( new ScavengerEntry { Enabled = true, Graphic = GOLD, Hue = -1, Name = "gold coin" } );
        }

        public void Dispose()
        {
            // Serialize unsubscribes from Engine.Items before anything else.
            _viewModel.Serialize( null );
            _viewModel.Enabled = false;
            ActionPacketQueue.Clear();
            Options.CurrentOptions = _originalOptions;
        }

        /// <summary>The 0xF3 from the report: 500 gold, at the player's position.</summary>
        private static byte[] WorldItem( int serial = REWARD_SERIAL )
        {
            return
            [
                0xF3, 0x00, 0x01, 0x00, (byte) ( serial >> 24 ), (byte) ( serial >> 16 ), (byte) ( serial >> 8 ),
                (byte) serial, 0x0E, 0xED, 0x00, 0x01, 0xF4, 0x01, 0xF4, X >> 8, X & 0xFF, Y >> 8, Y & 0xFF, 0x14,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x01
            ];
        }

        private static byte[] Delete( int serial = REWARD_SERIAL )
        {
            return [0x1D, (byte) ( serial >> 24 ), (byte) ( serial >> 16 ), (byte) ( serial >> 8 ), (byte) serial];
        }

        private static bool AnnouncedScavenging( PacketRecorder recorder )
        {
            byte[] text = Encoding.BigEndianUnicode.GetBytes( "Scavenging" );

            return recorder.ReceivedWithId( 0xAE ).Any( p => p.AsSpan().IndexOf( text ) >= 0 );
        }

        private static bool DraggedReward( PacketRecorder recorder )
        {
            return recorder.SentWithId( 0x07 ).Any( p =>
                ( ( p[1] << 24 ) | ( p[2] << 16 ) | ( p[3] << 8 ) | p[4] ) == REWARD_SERIAL );
        }

        [Fact]
        public async Task DoesNotHoldUpThePacketThread()
        {
            using ManualResetEventSlim release = new();

            // A busy queue (another agent's action), so a drag can't complete straight away.
            Task<bool> blocker = ActionPacketQueue.EnqueueAction( 0, _ => release.Wait( 5000 ), QueuePriority.High );

            try
            {
                DateTime start = DateTime.Now;

                EngineState.Receive( WorldItem() );

                // Before the fix this call didn't return until the scavenge drag had been sent - here,
                // not until the scavenger's 5s wait ran out.
                Assert.True( DateTime.Now - start < TimeSpan.FromMilliseconds( 200 ),
                    "Receiving a world item waited on the scavenger" );
            }
            finally
            {
                release.Set();
                await blocker;
            }
        }

        [Fact]
        public async Task IgnoresItemDeletedStraightAway()
        {
            using PacketRecorder recorder = new();

            EngineState.Receive( WorldItem() );
            EngineState.Receive( Delete() );

            // Longer than the settle wait, so the pass the 0xF3 started has finished.
            await Task.Delay( 1000, TestContext.Current.CancellationToken );

            Assert.False( DraggedReward( recorder ), "Dragged an item that had already been deleted" );
            Assert.False( AnnouncedScavenging( recorder ), "Announced an item that had already been deleted" );
        }

        [Fact]
        public async Task ScavengesItemThatStays()
        {
            using PacketRecorder recorder = new();

            EngineState.Receive( WorldItem() );

            for ( int i = 0; i < 50 && !DraggedReward( recorder ); i++ )
            {
                await Task.Delay( 100, TestContext.Current.CancellationToken );
            }

            Assert.True( DraggedReward( recorder ) );
            Assert.True( AnnouncedScavenging( recorder ) );
        }

        /// <summary>
        ///     The item is still there when the pass queues the drag, but gone by the time the queue
        ///     reaches it (here, held up behind another agent's action).
        /// </summary>
        [Fact]
        public async Task SkipsQueuedDragWhenItemHasGone()
        {
            using PacketRecorder recorder = new();
            using ManualResetEventSlim release = new();
            using ManualResetEventSlim dragQueued = new();

            void OnQueueEvent( ActionQueueEvents e, BaseQueueItem item )
            {
                if ( e == ActionQueueEvents.Enqueue && item is ActionQueueItem { Serial: REWARD_SERIAL } )
                {
                    dragQueued.Set();
                }
            }

            ActionPacketQueue.ActionQueueEvent += OnQueueEvent;

            try
            {
                Task<bool> blocker = ActionPacketQueue.EnqueueAction( 0, _ => release.Wait( 5000 ), QueuePriority.High );

                EngineState.Receive( WorldItem() );

                Assert.True( dragQueued.Wait( 5000, TestContext.Current.CancellationToken ), "Scavenger never queued the drag" );

                EngineState.Receive( Delete() );
                release.Set();
                await blocker;

                await Task.Delay( 500, TestContext.Current.CancellationToken );

                Assert.False( DraggedReward( recorder ), "Dragged an item that had gone while queued" );
                Assert.False( AnnouncedScavenging( recorder ), "Announced an item that had gone while queued" );
            }
            finally
            {
                ActionPacketQueue.ActionQueueEvent -= OnQueueEvent;
                release.Set();
            }
        }
    }
}
