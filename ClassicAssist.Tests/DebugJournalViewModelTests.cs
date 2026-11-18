using ClassicAssist.Data;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UI.ViewModels.Debug;
using ClassicAssist.Shared.UO.Data;
using System;
using Xunit;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     The Journal tab captures nothing until its Enabled box is ticked, matching the WPF build. It
    ///     previously subscribed in its constructor, so it ran for as long as the Debug Window was open
    ///     whichever tab you were actually looking at, and there was no way to turn it off.
    /// </summary>
    public class DebugJournalViewModelTests : IDisposable
    {
        public DebugJournalViewModelTests()
        {
            Engine.Journal.Clear();
        }

        public void Dispose()
        {
            Engine.Journal.Clear();
        }

        [Fact]
        public void WillNotCaptureUntilEnabled()
        {
            Engine.Journal.Write( new JournalEntry { Text = "before", SpeechType = JournalSpeech.System } );

            DebugJournalViewModel viewModel = new();

            Assert.False( viewModel.Enabled, "capture should be off by default" );
            Assert.Empty( viewModel.Items );
        }

        [Fact]
        public void WillReplayTheBufferWhenEnabled()
        {
            Engine.Journal.Write( new JournalEntry { Text = "first", SpeechType = JournalSpeech.System } );
            Engine.Journal.Write( new JournalEntry { Text = "second", SpeechType = JournalSpeech.System } );

            DebugJournalViewModel viewModel = new() { Enabled = true };

            Assert.Equal( 2, viewModel.Items.Count );
            Assert.Contains( "first", viewModel.Items[0] );
            Assert.Contains( "second", viewModel.Items[1] );
        }

        /// <summary>
        ///     WPF appends the buffer on every enable without clearing, so a tick-untick-tick shows every
        ///     entry twice. Not carried over.
        /// </summary>
        [Fact]
        public void WillNotDuplicateWhenReEnabled()
        {
            Engine.Journal.Write( new JournalEntry { Text = "only once", SpeechType = JournalSpeech.System } );

            DebugJournalViewModel viewModel = new() { Enabled = true };

            int first = viewModel.Items.Count;

            viewModel.Enabled = false;
            viewModel.Enabled = true;

            Assert.Equal( first, viewModel.Items.Count );
        }

        [Fact]
        public void WillClear()
        {
            Engine.Journal.Write( new JournalEntry { Text = "entry", SpeechType = JournalSpeech.System } );

            DebugJournalViewModel viewModel = new() { Enabled = true };

            Assert.NotEmpty( viewModel.Items );

            viewModel.ClearCommand.Execute( null );

            Assert.Empty( viewModel.Items );
        }
    }
}
