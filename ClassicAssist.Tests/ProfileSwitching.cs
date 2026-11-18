using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ClassicAssist.Data;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Hotkeys.Commands;
using ClassicAssist.Data.Skills;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.Misc;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Switching profile deserializes every tab again into the same view models. The skills tab owns
    ///     a "Skills" category in the shared hotkey list; loading a second profile has to reuse it, not
    ///     add a second one - a duplicate shows every skill hotkey twice and the old copy keeps firing
    ///     the previous profile's keys.
    ///     <para>
    ///         Runs against a two-skill skills.mul written to a temp directory rather than a client
    ///         install.
    ///     </para>
    /// </summary>
    public class ProfileSwitching : IDisposable
    {
        private readonly string _directory = Path.Combine( Path.GetTempPath(), "CASkills" + Guid.NewGuid().ToString( "N" ) );
        private readonly List<HotkeyCommand> _originalHotkeys;
        private readonly ObservableCollectionEx<SkillEntry> _originalSkillItems;
        private readonly object _originalSkillData;
        private readonly object _originalSkillPath;
        private readonly FieldInfo _skillDataField = typeof( Skills ).GetField( "_lazySkillData", BindingFlags.Static | BindingFlags.NonPublic );
        private readonly FieldInfo _skillPathField = typeof( Skills ).GetField( "_dataPath", BindingFlags.Static | BindingFlags.NonPublic );
        private SkillsTabViewModel _viewModel;

        public ProfileSwitching()
        {
            EngineState.Reset();

            _originalSkillData = _skillDataField.GetValue( null );
            _originalSkillPath = _skillPathField.GetValue( null );
            _originalHotkeys = HotkeyManager.GetInstance().Items.ToList();
            _originalSkillItems = SkillManager.GetInstance().Items;

            Directory.CreateDirectory( _directory );
            WriteSkills( _directory, ( "Alchemy", false ), ( "Hiding", true ), ( "Stealth", true ) );

            Skills.Initialize( _directory );
        }

        public void Dispose()
        {
            if ( _viewModel != null )
            {
                // The view model has no Dispose; detach its static packet subscriptions by hand.
                Detach( "SkillUpdatedEvent", "OnSkillUpdatedEvent" );
                Detach( "SkillsListEvent", "OnSkillsListEvent" );
            }

            _skillDataField.SetValue( null, _originalSkillData );
            _skillPathField.SetValue( null, _originalSkillPath );
            SkillManager.GetInstance().Items = _originalSkillItems;

            ObservableCollectionEx<HotkeyCommand> hotkeys = HotkeyManager.GetInstance().Items;
            hotkeys.Clear();

            foreach ( HotkeyCommand command in _originalHotkeys )
            {
                hotkeys.Add( command );
            }

            try
            {
                Directory.Delete( _directory, true );
            }
            catch ( IOException )
            {
                // Best effort.
            }
        }

        private void Detach( string eventName, string handlerName )
        {
            EventInfo eventInfo = typeof( IncomingPacketHandlers ).GetEvent( eventName, BindingFlags.Static | BindingFlags.Public );
            Delegate handler = Delegate.CreateDelegate( eventInfo.EventHandlerType, _viewModel, handlerName );

            eventInfo.RemoveEventHandler( null, handler );
        }

        /// <summary>
        ///     Skills.idx: 12-byte entries (offset, length, extra). skills.mul: per skill an "invokable"
        ///     byte then the NUL-terminated name; the entry length counts both.
        /// </summary>
        private static void WriteSkills( string directory, params (string name, bool invokable)[] skills )
        {
            using MemoryStream index = new MemoryStream();
            using MemoryStream mul = new MemoryStream();
            using BinaryWriter indexWriter = new BinaryWriter( index );

            foreach ( (string name, bool invokable) in skills )
            {
                byte[] nameBytes = Encoding.ASCII.GetBytes( name );

                indexWriter.Write( (int) mul.Position );
                indexWriter.Write( nameBytes.Length + 2 );
                indexWriter.Write( 0 );

                mul.WriteByte( invokable ? (byte) 1 : (byte) 0 );
                mul.Write( nameBytes, 0, nameBytes.Length );
                mul.WriteByte( 0 );
            }

            indexWriter.Flush();

            File.WriteAllBytes( Path.Combine( directory, "Skills.idx" ), index.ToArray() );
            File.WriteAllBytes( Path.Combine( directory, "skills.mul" ), mul.ToArray() );
        }

        private static JObject Profile( int key )
        {
            return new JObject
            {
                {
                    "Skills",
                    new JArray
                    {
                        new JObject
                        {
                            { "Name", "Hiding" },
                            { "Keys", new JObject { { "Keys", key }, { "Modifier", 0 }, { "Mouse", 7 } } },
                            { "PassToUO", false }
                        }
                    }
                }
            };
        }

        [Fact]
        public void WontDuplicateSkillsCategory()
        {
            _viewModel = new SkillsTabViewModel();
            Options options = Options.CurrentOptions;

            _viewModel.Deserialize( Profile( 94 ), options );
            _viewModel.Deserialize( Profile( 95 ), options );

            HotkeyCommand category = Assert.Single( HotkeyManager.GetInstance().Items,
                hk => hk.IsCategory && hk.Name == Strings.Skills );

            // Only invokable skills get a hotkey, and the second profile's binding is the live one.
            Assert.Equal( new[] { "Hiding", "Stealth" }, category.Children.Select( c => c.Name ).ToArray() );

            HotkeyEntry hiding = category.Children.Single( c => c.Name == "Hiding" );

            Assert.Equal( new ShortcutKeys( Profile( 95 )["Skills"][0]["Keys"] ), hiding.Hotkey );
            Assert.False( hiding.PassToUO );
        }
    }
}
