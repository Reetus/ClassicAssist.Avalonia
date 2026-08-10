using ClassicAssist.Data;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UI.ViewModels.Debug;
using ClassicAssist.Shared.UO.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     The Journal tab captures nothing until its Enabled box is ticked, matching the WPF build. It
    ///     previously subscribed in its constructor, so it ran for as long as the Debug Window was open
    ///     whichever tab you were actually looking at, and there was no way to turn it off.
    /// </summary>
    [TestClass]
    public class DebugJournalViewModelTests
    {
        [TestInitialize]
        [TestCleanup]
        public void ClearJournal()
        {
            Engine.Journal.Clear();
        }

        [TestMethod]
        public void WillNotCaptureUntilEnabled()
        {
            Engine.Journal.Write( new JournalEntry { Text = "before", SpeechType = JournalSpeech.System } );

            DebugJournalViewModel viewModel = new();

            Assert.IsFalse( viewModel.Enabled, "capture should be off by default" );
            Assert.AreEqual( 0, viewModel.Items.Count );
        }

        [TestMethod]
        public void WillReplayTheBufferWhenEnabled()
        {
            Engine.Journal.Write( new JournalEntry { Text = "first", SpeechType = JournalSpeech.System } );
            Engine.Journal.Write( new JournalEntry { Text = "second", SpeechType = JournalSpeech.System } );

            DebugJournalViewModel viewModel = new() { Enabled = true };

            Assert.AreEqual( 2, viewModel.Items.Count );
            StringAssert.Contains( viewModel.Items[0], "first" );
            StringAssert.Contains( viewModel.Items[1], "second" );
        }

        /// <summary>
        ///     WPF appends the buffer on every enable without clearing, so a tick-untick-tick shows every
        ///     entry twice. Not carried over.
        /// </summary>
        [TestMethod]
        public void WillNotDuplicateWhenReEnabled()
        {
            Engine.Journal.Write( new JournalEntry { Text = "only once", SpeechType = JournalSpeech.System } );

            DebugJournalViewModel viewModel = new() { Enabled = true };

            int first = viewModel.Items.Count;

            viewModel.Enabled = false;
            viewModel.Enabled = true;

            Assert.AreEqual( first, viewModel.Items.Count );
        }

        [TestMethod]
        public void WillClear()
        {
            Engine.Journal.Write( new JournalEntry { Text = "entry", SpeechType = JournalSpeech.System } );

            DebugJournalViewModel viewModel = new() { Enabled = true };

            Assert.AreNotEqual( 0, viewModel.Items.Count );

            viewModel.ClearCommand.Execute( null );

            Assert.AreEqual( 0, viewModel.Items.Count );
        }
    }
}
