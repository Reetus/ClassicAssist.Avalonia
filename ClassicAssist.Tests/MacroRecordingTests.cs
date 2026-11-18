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
using ClassicAssist.Data.Macros;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Network.Packets;
using Xunit;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     The macros tab's Record button: <see cref="MacrosTabViewModel.RecordCommand" /> and the
    ///     <see cref="MacroManager" /> plumbing that turns packets into commands in the selected macro.
    /// </summary>
    public class MacroRecordingTests : IDisposable
    {
        private const int SERIAL = 0x40001234;
        private const string USE_OBJECT_COMMAND = "UseObject(0x40001234)\r\n";

        private IDispatcher _originalDispatcher;
        private Func<bool> _originalIsRecording;
        private Action<string> _originalInsertDocument;
        private Action<string, string> _originalNewMacro;
        private UI.Misc.ObservableCollectionEx<MacroEntry> _originalItems;
        private MacrosTabViewModel _viewModel;
        private MacroEntry _macro;

        public MacroRecordingTests()
        {
            MacroManager manager = MacroManager.GetInstance();

            _originalDispatcher = Engine.Dispatcher;
            _originalIsRecording = manager.IsRecording;
            _originalInsertDocument = manager.InsertDocument;
            _originalNewMacro = manager.NewMacro;
            _originalItems = manager.Items;

            // The view model captures the dispatcher when it is constructed; inline, so a recorded
            // command is in the macro by the time the send returns.
            Engine.Dispatcher = new InlineDispatcher();

            _viewModel = new MacrosTabViewModel();
            _macro = new MacroEntry { Name = "Recording", Macro = string.Empty };
            _viewModel.Items.Add( _macro );
            _viewModel.SelectedItem = _macro;
        }

        public void Dispose()
        {
            _viewModel.Dispose();

            MacroManager manager = MacroManager.GetInstance();

            manager.IsRecording = _originalIsRecording;
            manager.InsertDocument = _originalInsertDocument;
            manager.NewMacro = _originalNewMacro;
            manager.Items = _originalItems;

            Engine.Dispatcher = _originalDispatcher;
        }

        private static void SendUseObject()
        {
            // No plugin is attached, so this only reaches the observers of the internal send event.
            Engine.SendPacketToServer( new UseObject( SERIAL ) );
        }

        [Fact]
        public void RecordCommandTogglesRecordingAndLabel()
        {
            Assert.False( _viewModel.IsRecording );
            Assert.Equal( Strings.Record, _viewModel.RecordLabel );

            _viewModel.RecordCommand.Execute( null );

            Assert.True( _viewModel.IsRecording );
            Assert.Equal( Strings.Stop, _viewModel.RecordLabel );

            _viewModel.RecordCommand.Execute( null );

            Assert.False( _viewModel.IsRecording );
            Assert.Equal( Strings.Record, _viewModel.RecordLabel );
        }

        [Fact]
        public void RecordLabelChangeIsNotifiedWithIsRecording()
        {
            int labelChanges = 0;

            _viewModel.PropertyChanged += ( sender, e ) =>
            {
                if ( e.PropertyName == nameof( MacrosTabViewModel.RecordLabel ) )
                {
                    labelChanges++;
                }
            };

            _viewModel.RecordCommand.Execute( null );

            Assert.Equal( 1, labelChanges ); // "the button's label is bound to RecordLabel, not IsRecording"
        }

        [Fact]
        public void RecordCommandNeedsASelectedMacroToStart()
        {
            _viewModel.SelectedItem = null;

            Assert.False( _viewModel.RecordCommand.CanExecute( null ) );

            _viewModel.SelectedItem = _macro;

            Assert.True( _viewModel.RecordCommand.CanExecute( null ) );
        }

        [Fact]
        public void RecordCommandCanStillStopWhenSelectionIsLost()
        {
            _viewModel.RecordCommand.Execute( null );
            _viewModel.SelectedItem = null;

            Assert.True( _viewModel.RecordCommand.CanExecute( null ) );

            _viewModel.RecordCommand.Execute( null );

            Assert.False( _viewModel.IsRecording );
        }

        [Fact]
        public void RecordingAppendsCommandsForPacketsTheAssistantSends()
        {
            _macro.Macro = "Pause(500)\r\n";
            _viewModel.RecordCommand.Execute( null );

            SendUseObject();
            SendUseObject();

            Assert.Equal( "Pause(500)\r\n" + USE_OBJECT_COMMAND + USE_OBJECT_COMMAND, _macro.Macro );
        }

        [Fact]
        public void NothingIsRecordedWhenNotRecording()
        {
            SendUseObject();

            Assert.Equal( string.Empty, _macro.Macro );

            _viewModel.RecordCommand.Execute( null );
            _viewModel.RecordCommand.Execute( null );

            SendUseObject();

            Assert.Equal( string.Empty, _macro.Macro );
        }

        [Fact]
        public void RecordingWithNoSelectionDropsTheCommand()
        {
            _viewModel.RecordCommand.Execute( null );
            _viewModel.SelectedItem = null;

            SendUseObject();

            Assert.Equal( string.Empty, _macro.Macro );
        }

        [Fact]
        public void PacketsBeforeTheMacrosTabExistsAreIgnored()
        {
            // The state MacroManager is in until the first MacrosTabViewModel is constructed.
            MacroManager manager = MacroManager.GetInstance();
            manager.IsRecording = null;
            manager.InsertDocument = null;

            SendUseObject();

            Assert.Equal( string.Empty, _macro.Macro );
        }

        [Fact]
        public void PlayingTheRecordedMacroStopsRecording()
        {
            _viewModel.RecordCommand.Execute( null );

            MacroManager.GetInstance().OnMacroStarted( _macro );

            Assert.False( _viewModel.IsRecording );
            Assert.Equal( Strings.Record, _viewModel.RecordLabel );
        }

        [Fact]
        public void PlayingAnotherMacroKeepsRecording()
        {
            MacroEntry other = new MacroEntry { Name = "Other", Macro = string.Empty };
            _viewModel.Items.Add( other );

            _viewModel.RecordCommand.Execute( null );

            MacroManager.GetInstance().OnMacroStarted( other );

            Assert.True( _viewModel.IsRecording );
        }

        [Fact]
        public void PlayIsAvailableWhileRecording()
        {
            _viewModel.RecordCommand.Execute( null );

            Assert.True( _viewModel.ExecuteCommand.CanExecute( _macro ) );
        }

        [Fact]
        public void DisposedViewModelNoLongerReactsToMacroStart()
        {
            _viewModel.RecordCommand.Execute( null );
            _viewModel.Dispose();

            MacroManager.GetInstance().OnMacroStarted( _macro );

            Assert.True( _viewModel.IsRecording );
        }
    }
}
