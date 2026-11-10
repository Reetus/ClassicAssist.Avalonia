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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using AvaloniaEdit;
using ClassicAssist.Avalonia.Views;
using ClassicAssist.Data.Macros;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Network.Packets;
using Xunit;
using SEngine = ClassicAssist.Shared.Engine;

namespace ClassicAssist.HeadlessTests;

/// <summary>
///     The Record button on the macros tab, driven through the real view: its label, what it locks
///     while recording, and recorded commands reaching the editor.
/// </summary>
public class MacrosTabRecordButtonTests
{
    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            View = new MacrosTabControl();
            ViewModel = (MacrosTabViewModel) View.DataContext;
            Macro = new MacroEntry { Name = "Recording", Macro = string.Empty };
            ViewModel.Items.Add( Macro );

            Window = new Window { Width = 800, Height = 500, Content = View };
            Window.Show();
            Headless.Settle();

            RecordButton = View.FindControl<Button>( "RecordButton" );
            MacroList = View.FindControl<ListBox>( "ListBox" );
            Editor = View.FindControl<TextEditor>( "Editor" );
        }

        public MacrosTabControl View { get; }
        public MacrosTabViewModel ViewModel { get; }
        public MacroEntry Macro { get; }
        public Window Window { get; }
        public Button RecordButton { get; }
        public ListBox MacroList { get; }
        public TextEditor Editor { get; }

        public void Select( MacroEntry entry )
        {
            MacroList.SelectedItem = entry;
            Headless.Settle();
        }

        public void ClickRecord()
        {
            Point centre = RecordButton.TranslatePoint(
                new Point( RecordButton.Bounds.Width / 2, RecordButton.Bounds.Height / 2 ), Window ) ?? default;

            Window.MouseDown( centre, MouseButton.Left );
            Window.MouseUp( centre, MouseButton.Left );
            Headless.Settle();
        }

        public void Dispose()
        {
            Window.Close();
            ViewModel.Dispose();
        }
    }

    [Fact]
    public Task RecordButton_SitsBetweenTheEditAndPlayRows_AtFullWidth()
    {
        return Headless.Run( () =>
        {
            using Harness harness = new();

            Assert.NotNull( harness.RecordButton );
            Assert.Equal( Strings.Record, harness.RecordButton.Content );

            // Same width as the list above it, and laid out (not collapsed to nothing by the grid).
            Assert.Equal( harness.MacroList.Bounds.Width, harness.RecordButton.Bounds.Width );
            Assert.True( harness.RecordButton.Bounds.Height > 0 );
            Assert.True( harness.RecordButton.Bounds.Top >= harness.MacroList.Bounds.Bottom );
        } );
    }

    [Fact]
    public Task RecordButton_IsDisabledUntilAMacroIsSelected()
    {
        return Headless.Run( () =>
        {
            using Harness harness = new();

            Assert.False( harness.RecordButton.IsEffectivelyEnabled );

            harness.ClickRecord();

            Assert.False( harness.ViewModel.IsRecording );

            harness.Select( harness.Macro );

            Assert.True( harness.RecordButton.IsEffectivelyEnabled );
        } );
    }

    [Fact]
    public Task ClickingRecord_TogglesTheLabelAndLocksTheMacroList()
    {
        return Headless.Run( () =>
        {
            using Harness harness = new();
            harness.Select( harness.Macro );

            harness.ClickRecord();

            Assert.True( harness.ViewModel.IsRecording );
            Assert.Equal( Strings.Stop, harness.RecordButton.Content );
            Assert.False( harness.MacroList.IsEffectivelyEnabled );
            Assert.True( harness.RecordButton.IsEffectivelyEnabled );

            harness.ClickRecord();

            Assert.False( harness.ViewModel.IsRecording );
            Assert.Equal( Strings.Record, harness.RecordButton.Content );
            Assert.True( harness.MacroList.IsEffectivelyEnabled );
        } );
    }

    [Fact]
    public Task RecordedCommands_AppearInTheEditor_AndCanBeUndone()
    {
        return Headless.Run( () =>
        {
            using Harness harness = new();
            harness.Macro.Macro = "Pause(500)\r\n";
            harness.Select( harness.Macro );
            harness.ClickRecord();

            // No plugin is attached, so this only reaches the observers of the internal send event.
            SEngine.SendPacketToServer( new UseObject( 0x40001234 ) );
            Headless.Settle();

            Assert.Equal( "Pause(500)\r\nUseObject(0x40001234)\r\n", harness.Macro.Macro );
            Assert.Equal( harness.Macro.Macro, harness.Editor.Document.Text );
            Assert.True( harness.Editor.CanUndo );

            harness.ClickRecord();
            SEngine.SendPacketToServer( new UseObject( 0x40001234 ) );
            Headless.Settle();

            Assert.Equal( "Pause(500)\r\nUseObject(0x40001234)\r\n", harness.Macro.Macro );
        } );
    }

    [Fact]
    public Task PlayingTheRecordedMacro_ReleasesTheButtonAndTheList()
    {
        return Headless.Run( () =>
        {
            using Harness harness = new();
            harness.Select( harness.Macro );
            harness.ClickRecord();

            MacroManager.GetInstance().OnMacroStarted( harness.Macro );
            Headless.Settle();

            Assert.False( harness.ViewModel.IsRecording );
            Assert.Equal( Strings.Record, harness.RecordButton.Content );
            Assert.True( harness.MacroList.IsEffectivelyEnabled );
        } );
    }
}
