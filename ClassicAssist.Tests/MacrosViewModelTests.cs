using System;
using System.IO;
using System.Linq;
using ClassicAssist.Data.Macros;
using ClassicAssist.Shared;
using ClassicAssist.UI.Misc;
using ClassicAssist.UI.ViewModels;
using Xunit;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     The macros tab's New and Remove commands, and that a macro it creates is wired to run through
    ///     <see cref="MacroManager" /> - the tab is the only place macros come from, so a new macro that
    ///     cannot be played (or that the manager does not know about) is a macro the player cannot use.
    /// </summary>
    public class MacrosViewModelTests : IDisposable
    {
        private readonly IDispatcher _originalDispatcher;
        private readonly Func<bool> _originalIsRecording;
        private readonly Action<string> _originalInsertDocument;
        private readonly Action<string, string> _originalNewMacro;
        private readonly ObservableCollectionEx<MacroEntry> _originalItems;
        private readonly string _originalStartupPath;
        private readonly string _startupPath = Path.Combine( Path.GetTempPath(), "CAMacrosVM" + Guid.NewGuid().ToString( "N" ) );
        private readonly MacrosTabViewModel _model;

        public MacrosViewModelTests()
        {
            EngineState.Reset();

            MacroManager manager = MacroManager.GetInstance();

            _originalDispatcher = Engine.Dispatcher;
            _originalIsRecording = manager.IsRecording;
            _originalInsertDocument = manager.InsertDocument;
            _originalNewMacro = manager.NewMacro;
            _originalItems = manager.Items;
            _originalStartupPath = Engine.StartupPath;

            // An empty startup directory: no Assistant.json, as on a first run.
            Directory.CreateDirectory( _startupPath );
            Engine.StartupPath = _startupPath;
            Engine.Dispatcher = new InlineDispatcher();

            _model = new MacrosTabViewModel();
        }

        public void Dispose()
        {
            foreach ( MacroEntry entry in _model.Items )
            {
                entry.MacroInvoker.Stop();
                entry.MacroInvoker.Thread?.Join( 5000 );
            }

            _model.Dispose();

            MacroManager manager = MacroManager.GetInstance();

            manager.IsRecording = _originalIsRecording;
            manager.InsertDocument = _originalInsertDocument;
            manager.NewMacro = _originalNewMacro;
            manager.Items = _originalItems;
            manager.CurrentMacro = null;

            Engine.Dispatcher = _originalDispatcher;
            Engine.StartupPath = _originalStartupPath;

            try
            {
                Directory.Delete( _startupPath, true );
            }
            catch ( IOException )
            {
                // Best effort.
            }
        }

        [Fact]
        public void NewCommandWillAddItem()
        {
            _model.NewMacroCommand.Execute( null );

            MacroEntry entry = Assert.Single( _model.Items );

            Assert.Same( entry, _model.SelectedItem );
            Assert.Equal( "Macro-1", entry.Name );
            Assert.Contains( entry, MacroManager.GetInstance().Items );
        }

        /// <summary>
        ///     Names come from the count, so a second New after a removal must still be unique.
        /// </summary>
        [Fact]
        public void NewCommandWillKeepNamesUnique()
        {
            _model.NewMacroCommand.Execute( null );
            _model.NewMacroCommand.Execute( null );
            _model.RemoveMacroCommand.Execute( _model.Items.First() );
            _model.NewMacroCommand.Execute( null );

            Assert.Equal( 2, _model.Items.Count );
            Assert.Equal( _model.Items.Count, _model.Items.Select( i => i.Name ).Distinct().Count() );
        }

        [Fact]
        public void RemoveCommandWillRemoveItem()
        {
            _model.NewMacroCommand.Execute( null );

            MacroEntry item = _model.Items.FirstOrDefault();

            _model.RemoveMacroCommand.Execute( item );

            Assert.Empty( _model.Items );
        }

        [Fact]
        public void NewMacroWillRunThroughTheManager()
        {
            _model.NewMacroCommand.Execute( null );

            MacroEntry entry = _model.Items.Single();
            entry.Macro = "SetAlias('vm', 1)";

            entry.Action( entry, null );

            Assert.Same( entry, MacroManager.GetInstance().CurrentMacro );
            Assert.True( entry.MacroInvoker.Thread?.Join( 15000 ) ?? false );
            Assert.False( entry.MacroInvoker.IsFaulted, entry.MacroInvoker.Exception?.ToString() );
        }
    }
}
