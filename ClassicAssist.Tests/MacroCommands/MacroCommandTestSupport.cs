using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClassicAssist.Data;
using ClassicAssist.Misc;
using ClassicAssist.UO.Network;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    /// <summary>
    ///     Shared plumbing for the macro command tests: draining the action queue so packets a command
    ///     queued are observable, and reading back the system messages a command printed.
    /// </summary>
    internal static class MacroCommandTestSupport
    {
        /// <summary>
        ///     Options with the action delay off, so queued packets go out as fast as the worker thread runs.
        ///     Callers save and restore <see cref="Options.CurrentOptions" /> around it.
        /// </summary>
        public static Options FastOptions()
        {
            return new Options { ActionDelay = false, UseObjectQueue = false };
        }

        /// <summary>
        ///     Completes once everything queued at <see cref="QueuePriority.Low" /> before this call has
        ///     been processed: the queue is FIFO within a priority, so a no-op queued behind them runs last.
        /// </summary>
        public static async Task DrainActionQueueAsync()
        {
            Task<bool> sentinel = ActionPacketQueue.EnqueueAction( 0, _ => true, QueuePriority.Low, false,
                TestContext.Current.CancellationToken );

            Task finished = await Task.WhenAny( sentinel, Task.Delay( 5000, TestContext.Current.CancellationToken ) );

            Assert.True( finished == sentinel, "The action queue did not drain within 5s" );
        }

        /// <summary>The text of every 0xAE system message injected into the client, oldest first.</summary>
        public static IReadOnlyList<string> SystemMessages( PacketRecorder recorder )
        {
            return recorder.ReceivedWithId( 0xAE )
                .Select( p => Encoding.BigEndianUnicode.GetString( p, 48, p.Length - 48 ).TrimEnd( '\0' ) ).ToList();
        }

        public static int ReadInt( byte[] packet, int offset )
        {
            return ( packet[offset] << 24 ) | ( packet[offset + 1] << 16 ) | ( packet[offset + 2] << 8 ) |
                   packet[offset + 3];
        }

        public static int ReadShort( byte[] packet, int offset )
        {
            return ( packet[offset] << 8 ) | packet[offset + 1];
        }

        /// <summary>Polls <paramref name="condition" /> until it holds or the bound passes.</summary>
        public static async Task<bool> WaitUntilAsync( Func<bool> condition, int timeoutMs = 5000 )
        {
            DateTime deadline = DateTime.Now.AddMilliseconds( timeoutMs );

            while ( DateTime.Now < deadline )
            {
                if ( condition() )
                {
                    return true;
                }

                await Task.Delay( 5, TestContext.Current.CancellationToken );
            }

            return condition();
        }
    }
}
