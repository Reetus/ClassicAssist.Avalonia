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

using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;
using ClassicAssist.Avalonia.Misc;
using ClassicAssist.Data.Macros;
using Xunit;

namespace ClassicAssist.HeadlessTests;

/// <summary>
///     Undo/redo and breakpoint behaviour of the macro editor as <see cref="AvalonEditBehaviour" />
///     syncs it with the selected <see cref="MacroEntry" />.
/// </summary>
public class AvalonEditBehaviourTests
{
    private sealed class Harness
    {
        public Harness()
        {
            Editor = new TextEditor();
            Behaviour = new AvalonEditBehaviour();
            BreakpointBehaviour = new AvalonEditBreakpointMarginBehaviour();

            BehaviorCollection behaviours = Interaction.GetBehaviors( Editor );
            behaviours.Add( Behaviour );
            behaviours.Add( BreakpointBehaviour );

            Window = new Window { Content = Editor };
            Window.Show();
        }

        public TextEditor Editor { get; }
        public AvalonEditBehaviour Behaviour { get; }
        public AvalonEditBreakpointMarginBehaviour BreakpointBehaviour { get; }
        public Window Window { get; }

        public void Select( MacroEntry entry )
        {
            Behaviour.MacroEntry = entry;
            BreakpointBehaviour.Breakpoints = entry?.Breakpoints;
            Headless.Settle();
        }
    }

    [Fact]
    public Task SwitchingMacros_ClearsUndoHistory()
    {
        return Headless.Run( () =>
        {
            Harness harness = new();
            MacroEntry first = new() { Macro = "first" };
            MacroEntry second = new() { Macro = "second" };

            harness.Select( first );
            harness.Editor.Document.Insert( 5, "!" );
            harness.Editor.CaretOffset = 6;
            harness.Select( second );

            Assert.False( harness.Editor.CanUndo );
            Assert.Equal( 0, harness.Editor.CaretOffset );
            Assert.Equal( "second", harness.Editor.Document.Text );
            Assert.Equal( "first!", first.Macro );
            Assert.Equal( "second", second.Macro );

            harness.Window.Close();
        } );
    }

    [Fact]
    public Task Typing_CanBeUndoneAndRedone()
    {
        return Headless.Run( () =>
        {
            Harness harness = new();
            MacroEntry entry = new() { Macro = "a" };

            harness.Select( entry );
            harness.Editor.Document.Insert( 1, "b" );

            Assert.Equal( "ab", entry.Macro );

            harness.Editor.Undo();

            Assert.Equal( "a", entry.Macro );
            Assert.True( harness.Editor.CanRedo );

            harness.Editor.Redo();

            Assert.Equal( "ab", entry.Macro );

            harness.Window.Close();
        } );
    }

    [Fact]
    public Task ExternalChange_IsUndoable_AndShiftsBreakpoints()
    {
        return Headless.Run( () =>
        {
            Harness harness = new();
            MacroEntry entry = new() { Macro = "one\r\ntwo\r\nthree" };
            entry.Breakpoints.Add( 3 );

            harness.Select( entry );
            harness.Editor.CaretOffset = entry.Macro.Length;

            // e.g. recording, MCP or a file reload inserting a line above the breakpoint
            entry.Macro = "zero\r\none\r\ntwo\r\nthree";
            Headless.Settle();

            Assert.Equal( entry.Macro, harness.Editor.Document.Text );
            Assert.Equal( [4], entry.Breakpoints );
            Assert.Equal( entry.Macro.Length, harness.Editor.CaretOffset );

            harness.Editor.Undo();

            Assert.Equal( "one\r\ntwo\r\nthree", entry.Macro );
            Assert.Equal( [3], entry.Breakpoints );

            harness.Window.Close();
        } );
    }
}
