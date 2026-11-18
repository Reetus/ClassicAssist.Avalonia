using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClassicAssist.Data;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Data.Counters;
using ClassicAssist.Data.Dress;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Organizer;
using ClassicAssist.Data.Scavenger;
using ClassicAssist.Data.Vendors;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UO.Network.PacketFilter;
using Xunit;
using static ClassicAssist.Tests.MacroCommands.MacroCommandTestSupport;

namespace ClassicAssist.Tests.MacroCommands
{
    /// <summary>
    ///     The macro commands that drive agents by name (Dress, Counter, Organizer, VendorBuy) or through
    ///     the delegates the agent tabs install (Autoloot, Scavenger), plus the trade window commands.
    ///     The agents are singletons the UI owns, so each test swaps in its own entries and delegates and
    ///     puts the originals back. Name matching, the on/off/toggle parsing and the 0x6F trade packet
    ///     layouts are what a macro depends on.
    /// </summary>
    public class AgentCommandsBehaviourTests : IDisposable
    {
        private readonly AutolootManager _autoloot = AutolootManager.GetInstance();
        private readonly Action<int, bool> _originalCheckContainer;
        private readonly Func<bool> _originalAutolootIsEnabled;
        private readonly Action<bool> _originalAutolootSetEnabled;
        private readonly Options _originalOptions;
        private readonly Func<bool> _originalScavengerIsEnabled;
        private readonly Action<bool> _originalScavengerSetEnabled;
        private readonly IEnumerable<VendorBuyAgentEntry> _originalVendorBuyItems;
        private readonly List<object> _addedEntries = new List<object>();
        private readonly ScavengerManager _scavenger = ScavengerManager.GetInstance();

        public AgentCommandsBehaviourTests()
        {
            _originalOptions = Options.CurrentOptions;
            Options.CurrentOptions = FastOptions();
            EngineState.ResetWithPlayer();

            _originalCheckContainer = _autoloot.CheckContainer;
            _originalAutolootIsEnabled = _autoloot.IsEnabled;
            _originalAutolootSetEnabled = _autoloot.SetEnabled;
            _originalScavengerIsEnabled = _scavenger.IsEnabled;
            _originalScavengerSetEnabled = _scavenger.SetEnabled;
            _originalVendorBuyItems = VendorBuyManager.GetInstance().Items;
        }

        public void Dispose()
        {
            _autoloot.CheckContainer = _originalCheckContainer;
            _autoloot.IsEnabled = _originalAutolootIsEnabled;
            _autoloot.SetEnabled = _originalAutolootSetEnabled;
            _scavenger.IsEnabled = _originalScavengerIsEnabled;
            _scavenger.SetEnabled = _originalScavengerSetEnabled;
            VendorBuyManager.GetInstance().Items = _originalVendorBuyItems;

            foreach ( object entry in _addedEntries )
            {
                switch ( entry )
                {
                    case DressAgentEntry dress:
                        DressManager.GetInstance().Items.Remove( dress );
                        break;
                    case CountersAgentEntry counter:
                        CountersManager.GetInstance().Items.Remove( counter );
                        break;
                    case OrganizerEntry organizer:
                        OrganizerManager.GetInstance().Items.Remove( organizer );
                        break;
                }
            }

            Options.CurrentOptions = _originalOptions;
            EngineState.Reset();
        }

        [Fact]
        public void DressWillRunTheNamedEntryAndRejectUnknownNames()
        {
            List<string> ran = new List<string>();
            DressAgentEntry entry = new DressAgentEntry { Name = "AgentCmdDressA" };
            entry.Action = ( hotkey, args ) => ran.Add( ( (DressAgentEntry) hotkey ).Name );
            DressManager.GetInstance().Items.Add( entry );
            _addedEntries.Add( entry );

            using PacketRecorder recorder = new PacketRecorder();

            AgentCommands.Dress( "AgentCmdDressA" );
            AgentCommands.Dress( "agentcmddressa" );

            // Dress names are case sensitive, unlike the other agents.
            Assert.Equal( new[] { "AgentCmdDressA" }, ran );
            Assert.Contains( string.Format( Strings.Unknown_dress_agent___0___, "agentcmddressa" ), SystemMessages( recorder ) );
        }

        [Fact]
        public void DressWithoutANameWillNeedATemporaryLayout()
        {
            DressManager manager = DressManager.GetInstance();
            DressAgentEntry original = manager.TemporaryDress;

            try
            {
                manager.TemporaryDress = null;

                using PacketRecorder recorder = new PacketRecorder();

                AgentCommands.Dress();

                Assert.Contains( Strings.No_temporary_dress_layout_configured___, SystemMessages( recorder ) );
            }
            finally
            {
                manager.TemporaryDress = original;
            }
        }

        [Fact]
        public void CounterWillMatchNamesCaseInsensitively()
        {
            CountersAgentEntry entry = new CountersAgentEntry { Name = "AgentCmdReagents", Graphic = 0x0F7A, Color = -1 };
            CountersManager.GetInstance().Items.Add( entry );
            _addedEntries.Add( entry );
            entry.Count = 42;

            using PacketRecorder recorder = new PacketRecorder();

            Assert.Equal( 42, AgentCommands.Counter( "agentcmdreagents" ) );
            Assert.Empty( SystemMessages( recorder ) );

            Assert.Equal( 0, AgentCommands.Counter( "AgentCmdNoSuchCounter" ) );
            Assert.Contains( Strings.Invalid_counter_agent_name___, SystemMessages( recorder ) );
        }

        [Theory]
        [InlineData( "on", false, true )]
        [InlineData( " ON ", true, true )]
        [InlineData( "off", true, false )]
        [InlineData( "toggle", true, false )]
        [InlineData( "toggle", false, true )]
        [InlineData( "sideways", true, true )]
        public void SetAutolootWillDriveTheAgentsEnabledState( string onOff, bool before, bool after )
        {
            bool enabled = before;
            _autoloot.IsEnabled = () => enabled;
            _autoloot.SetEnabled = value => enabled = value;

            using PacketRecorder recorder = new PacketRecorder();

            AgentCommands.SetAutoloot( onOff );

            Assert.Equal( after, enabled );
            Assert.Contains( string.Format( Strings._0__agent_is_now__1_, Strings.Autoloot, after ? Strings.Enabled : Strings.Disabled ),
                SystemMessages( recorder ) );
        }

        [Fact]
        public void SetScavengerToggleWillFlipTheAgent()
        {
            bool enabled = true;
            _scavenger.IsEnabled = () => enabled;
            _scavenger.SetEnabled = value => enabled = value;

            AgentCommands.SetScavenger();

            Assert.False( enabled );

            AgentCommands.SetScavenger( "on" );

            Assert.True( enabled );
        }

        [Fact]
        public void AutolootWillAskTheAgentToCheckTheResolvedContainer()
        {
            List<(int, bool)> checks = new List<(int, bool)>();
            _autoloot.CheckContainer = ( serial, force ) => checks.Add( ( serial, force ) );
            AliasCommands.SetAlias( "corpse", 0x40000099 );

            AgentCommands.Autoloot( "corpse" );

            Assert.Equal( new[] { ( 0x40000099, true ) }, checks );
        }

        [Fact( Skip = "BUG: Autoloot reports an invalid serial but has no return, so it still checks container 0" )]
        public void AutolootWontCheckAnythingForAnInvalidSerial()
        {
            List<int> checks = new List<int>();
            _autoloot.CheckContainer = ( serial, _ ) => checks.Add( serial );

            AgentCommands.Autoloot( 0 );

            Assert.Empty( checks );
        }

        [Fact]
        public void SetOrganizerContainersWillSetBothContainersOnTheNamedEntry()
        {
            OrganizerEntry entry = new OrganizerEntry { Name = "AgentCmdOrganizer" };
            OrganizerManager.GetInstance().Items.Add( entry );
            _addedEntries.Add( entry );

            using PacketRecorder recorder = new PacketRecorder();

            AgentCommands.SetOrganizerContainers( "agentcmdORGANIZER", 0x40000010, 0x40000020 );

            Assert.Equal( 0x40000010, entry.SourceContainer );
            Assert.Equal( 0x40000020, entry.DestinationContainer );

            AgentCommands.SetOrganizerContainers( "AgentCmdNoSuchOrganizer", 1, 2 );

            Assert.Contains( Strings.Invalid_organizer_agent_name___, SystemMessages( recorder ) );
            Assert.Equal( 0x40000010, entry.SourceContainer );
        }

        [Fact]
        public void SetVendorBuyAutoBuyWillSwitchTheNamedList()
        {
            VendorBuyAgentEntry entry = new VendorBuyAgentEntry { Name = " Regs ", Enabled = false };
            VendorBuyManager.GetInstance().Items = new[] { entry };

            using PacketRecorder recorder = new PacketRecorder();

            AgentCommands.SetVendorBuyAutoBuy( "regs", "on" );
            Assert.True( entry.Enabled );

            AgentCommands.SetVendorBuyAutoBuy( "REGS" );
            Assert.False( entry.Enabled );

            AgentCommands.SetVendorBuyAutoBuy( "regs", "maybe" );
            Assert.False( entry.Enabled );

            AgentCommands.SetVendorBuyAutoBuy( "nope", "on" );

            IReadOnlyList<string> messages = SystemMessages( recorder );
            Assert.Contains( Strings.Invalid_state_name___on____off___or__toggle____, messages );
            Assert.Contains( Strings.Invalid_VendorBuy_list_name___, messages );
        }

        [Fact]
        public void TradeAcceptAndRejectWillSendAnUpdateForTheTrade()
        {
            Engine.Trade.Serial = 0x40000060;

            using PacketRecorder recorder = new PacketRecorder();

            AgentCommands.TradeAccept();
            AgentCommands.TradeReject();

            IReadOnlyList<byte[]> sent = recorder.SentWithId( 0x6F );
            Assert.Equal( 2, sent.Count );

            foreach ( byte[] packet in sent )
            {
                Assert.Equal( 12, packet.Length );
                Assert.Equal( 12, ReadShort( packet, 1 ) );
                Assert.Equal( 2, packet[3] );
                Assert.Equal( 0x40000060, ReadInt( packet, 4 ) );
            }

            Assert.Equal( 1, ReadInt( sent[0], 8 ) );
            Assert.Equal( 0, ReadInt( sent[1], 8 ) );
        }

        [Fact]
        public void TradeCloseAndCurrencyWillMatchTheirDeclaredLengths()
        {
            Engine.Trade.Serial = 0x40000060;
            Engine.Trade.ContainerLocal = 0x40000061;

            using PacketRecorder recorder = new PacketRecorder();

            AgentCommands.TradeClose();
            AgentCommands.TradeCurrency( 5000, 2 );

            IReadOnlyList<byte[]> sent = recorder.SentWithId( 0x6F );

            Assert.Equal( 8, sent[0].Length );
            Assert.Equal( 8, ReadShort( sent[0], 1 ) );
            Assert.Equal( 1, sent[0][3] );
            Assert.Equal( 0x40000060, ReadInt( sent[0], 4 ) );

            Assert.Equal( 16, sent[1].Length );
            Assert.Equal( 16, ReadShort( sent[1], 1 ) );
            Assert.Equal( 3, sent[1][3] );
            Assert.Equal( 0x40000061, ReadInt( sent[1], 4 ) );
            Assert.Equal( 5000, ReadInt( sent[1], 8 ) );
            Assert.Equal( 2, ReadInt( sent[1], 12 ) );
        }

        [Fact]
        public async Task WaitForTradeWindowWillReturnOnATradeStartOnly()
        {
            Task<bool> wait = Task.Run( () => AgentCommands.WaitForTradeWindow( 5000 ), TestContext.Current.CancellationToken );

            Assert.True( await WaitUntilAsync( () => Engine.PacketWaitEntries.GetEntries().Length == 1 ) );

            // 0x6F action at offset 3: 2 (update) is not a new window, 0 (start) is.
            Engine.PacketWaitEntries.CheckWait( new byte[] { 0x6F, 0x00, 0x10, 0x02, 0, 0, 0, 0 }, PacketDirection.Incoming );
            Assert.False( wait.IsCompleted );

            Engine.PacketWaitEntries.CheckWait( new byte[] { 0x6F, 0x00, 0x10, 0x00, 0, 0, 0, 0 }, PacketDirection.Incoming );

            Assert.True( await wait );
            Assert.Empty( Engine.PacketWaitEntries.GetEntries() );
        }

        [Fact( Skip = "BUG: WaitForTradeWindow leaves its wait entry registered when it times out" )]
        public void WaitForTradeWindowWillRemoveItsWaitEntryOnTimeout()
        {
            Assert.False( AgentCommands.WaitForTradeWindow( 20 ) );
            Assert.Empty( Engine.PacketWaitEntries.GetEntries() );
        }
    }
}
