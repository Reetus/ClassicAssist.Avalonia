using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ClassicAssist.Data;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Data.Dress;
using ClassicAssist.Data.Friends;
using ClassicAssist.Data.Organizer;
using ClassicAssist.Data.Scavenger;
using ClassicAssist.Data.Vendors;
using ClassicAssist.Mcp;
using ClassicAssist.UI.Misc;
using ClassicAssist.UO.Data;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.Mcp
{
    /// <summary>
    ///     listAgents / getAgent against the real agent managers. Agents ask these to answer questions like
    ///     "is this mobile a friend?" by filtering on a hex serial, so the serial formatting, the filter
    ///     (name or serial, case-insensitive), the agent-name normalisation ("Vendor Buy", "vendor-buy")
    ///     and the zero-means-unset container fields are what's pinned here. Every manager's contents are
    ///     swapped out in the constructor and restored in Dispose.
    /// </summary>
    public class McpAgentToolsTests : IDisposable
    {
        private readonly Options _originalOptions;
        private readonly ObservableCollectionEx<DressAgentEntry> _originalDress;
        private readonly ObservableCollectionEx<OrganizerEntry> _originalOrganizer;
        private readonly ObservableCollection<ScavengerEntry> _originalScavenger;
        private readonly IEnumerable<VendorBuyAgentEntry> _originalVendorBuy;
        private readonly Func<List<AutolootEntry>> _originalAutoloot;

        private readonly List<AutolootEntry> _autoloot = new List<AutolootEntry>();

        public McpAgentToolsTests()
        {
            EngineState.Reset();

            _originalOptions = Options.CurrentOptions;
            _originalDress = DressManager.GetInstance().Items;
            _originalOrganizer = OrganizerManager.GetInstance().Items;
            _originalScavenger = ScavengerManager.GetInstance().Items;
            _originalVendorBuy = VendorBuyManager.GetInstance().Items;
            _originalAutoloot = AutolootManager.GetInstance().GetEntries;

            Options.CurrentOptions = new Options();
            DressManager.GetInstance().Items = new ObservableCollectionEx<DressAgentEntry>();
            OrganizerManager.GetInstance().Items = new ObservableCollectionEx<OrganizerEntry>();
            ScavengerManager.GetInstance().Items = new ObservableCollection<ScavengerEntry>();
            VendorBuyManager.GetInstance().Items = new List<VendorBuyAgentEntry>();
            AutolootManager.GetInstance().GetEntries = () => _autoloot;
        }

        public void Dispose()
        {
            Options.CurrentOptions = _originalOptions;
            DressManager.GetInstance().Items = _originalDress;
            OrganizerManager.GetInstance().Items = _originalOrganizer;
            ScavengerManager.GetInstance().Items = _originalScavenger;
            VendorBuyManager.GetInstance().Items = _originalVendorBuy;
            AutolootManager.GetInstance().GetEntries = _originalAutoloot;
        }

        private static JObject GetAgent( string agent, string filter = null )
        {
            JObject args = new JObject { ["agent"] = agent };

            if ( filter != null )
            {
                args["filter"] = filter;
            }

            CallToolResult result = McpTools.Invoke( "getAgent", args );

            Assert.False( result.IsError, result.Content.Single().Text );

            return JObject.Parse( result.Content.Single().Text );
        }

        private static string[] Names( JObject result )
        {
            return result["entries"]?.Select( e => e["name"]?.ToString() ).ToArray();
        }

        private void AddFriends()
        {
            Options.CurrentOptions.Friends.Add( new FriendEntry { Name = "Alice", Serial = 0x006ec0dc } );
            Options.CurrentOptions.Friends.Add( new FriendEntry { Name = "Bob", Serial = 0x00000123 } );
            Options.CurrentOptions.Friends.Add( new FriendEntry { Name = "Malice", Serial = 0x00000456 } );
        }

        [Fact]
        public void WillListEveryAgentWithItsEntryCount()
        {
            AddFriends();
            ScavengerManager.GetInstance().Items.Add( new ScavengerEntry { Name = "Gold", Graphic = 0x0eed } );
            _autoloot.Add( new AutolootEntry { Name = "Gems" } );
            _autoloot.Add( new AutolootEntry { Name = "Scrolls" } );

            CallToolResult result = McpTools.Invoke( "listAgents", new JObject() );
            Dictionary<string, int> counts = JObject.Parse( result.Content.Single().Text )["agents"]
                ?.ToDictionary( a => a["agent"]?.ToString(), a => a["entryCount"]?.ToObject<int>() ?? -1 );

            Assert.Equal( new[] { "dress", "organizer", "friends", "vendorbuy", "scavenger", "autoloot" }, counts?.Keys.ToArray() );
            Assert.Equal( 3, counts?["friends"] );
            Assert.Equal( 1, counts?["scavenger"] );
            Assert.Equal( 2, counts?["autoloot"] );
            Assert.Equal( 0, counts?["dress"] );
        }

        [Fact]
        public void WillFindFriendBySerialHex()
        {
            AddFriends();

            JObject result = GetAgent( "friends", "0x006EC0DC" );
            JObject alice = (JObject) Assert.Single( (JArray) result["entries"] );

            Assert.Equal( "Alice", alice["name"]?.ToString() );
            Assert.Equal( "0x006ec0dc", alice["serial"]?.ToString() );
            Assert.Equal( 1, result["entryCount"]?.ToObject<int>() );
        }

        [Fact]
        public void WillFilterByNameSubstringCaseInsensitively()
        {
            AddFriends();

            Assert.Equal( new[] { "Alice", "Malice" }, Names( GetAgent( "friends", "ALICE" ) ) );
            Assert.Empty( (JArray) GetAgent( "friends", "Charlie" )["entries"] );
            Assert.Equal( 3, ( (JArray) GetAgent( "friends", "" )["entries"] ).Count );
        }

        [Theory]
        [InlineData( "vendorbuy" )]
        [InlineData( "Vendor Buy" )]
        [InlineData( "VENDOR-BUY" )]
        [InlineData( "vendor_buy" )]
        [InlineData( "venderbuy" )]
        [InlineData( "  Friends " )]
        public void WillNormaliseAgentNameSpellings( string agent )
        {
            JObject result = GetAgent( agent );

            Assert.Contains( result["agent"]?.ToString(), new[] { "vendorbuy", "friends" } );
        }

        [Fact]
        public void WillRejectUnknownOrMissingAgent()
        {
            CallToolResult unknown = McpTools.Invoke( "getAgent", new JObject { ["agent"] = "Trap Pouch" } );

            Assert.True( unknown.IsError );
            Assert.Contains( "Unknown agent 'trappouch'", unknown.Content.Single().Text );
            Assert.Contains( "dress, organizer, friends, vendorbuy, scavenger, autoloot", unknown.Content.Single().Text );

            CallToolResult missing = McpTools.Invoke( "getAgent", new JObject() );

            Assert.True( missing.IsError );
            Assert.Contains( "'agent'", missing.Content.Single().Text );
        }

        [Fact]
        public void WillDescribeDressEntriesAndTreatZeroAsUnset()
        {
            DressManager.GetInstance().Items.Add( new DressAgentEntry
            {
                Name = "PvP",
                UndressContainer = 0x40001000,
                Items = new List<DressAgentItem>
                {
                    new DressAgentItem { Name = "Robe", Serial = 0x40002000, ID = 0x1f03, Layer = Layer.OuterTorso },
                    new DressAgentItem { Name = "Any shield", Serial = 0, ID = 0x1b76, Layer = Layer.TwoHanded }
                }
            } );
            DressManager.GetInstance().Items.Add( new DressAgentEntry { Name = "Empty", Items = null } );

            JArray entries = (JArray) GetAgent( "dress" )["entries"];
            JToken pvp = entries.Single( e => e["name"]?.ToString() == "PvP" );
            JToken empty = entries.Single( e => e["name"]?.ToString() == "Empty" );

            Assert.Equal( "0x40001000", pvp["undressContainer"]?.ToString() );
            Assert.Equal( 2, pvp["itemCount"]?.ToObject<int>() );
            Assert.Equal( "0x40002000", pvp["items"]?[0]?["serial"]?.ToString() );
            Assert.Equal( "OuterTorso", pvp["items"]?[0]?["layer"]?.ToString() );
            Assert.Equal( JTokenType.Null, pvp["items"]?[1]?["serial"]?.Type );
            Assert.Equal( "0x1b76", pvp["items"]?[1]?["graphic"]?.ToString() );

            Assert.Equal( JTokenType.Null, empty["undressContainer"]?.Type );
            Assert.Equal( 0, empty["itemCount"]?.ToObject<int>() );
        }

        [Fact]
        public void WillDescribeOrganizerEntries()
        {
            OrganizerEntry entry = new OrganizerEntry
            {
                Name = "Regs", SourceContainer = 0x40003000, DestinationContainer = 0, Stack = true, ReturnExcess = false
            };

            entry.Items.Add( new OrganizerItem { Item = "Black Pearl", ID = 0x0f7a, Hue = 0, Amount = 100 } );
            OrganizerManager.GetInstance().Items.Add( entry );

            JToken regs = Assert.Single( (JArray) GetAgent( "organizer" )["entries"] );

            Assert.Equal( "0x40003000", regs["sourceContainer"]?.ToString() );
            Assert.Equal( JTokenType.Null, regs["destinationContainer"]?.Type );
            Assert.True( regs["stack"]?.ToObject<bool>() );
            Assert.Equal( "0x0f7a", regs["items"]?[0]?["id"]?.ToString() );
            Assert.Equal( 100, regs["items"]?[0]?["amount"]?.ToObject<int>() );
        }

        [Fact]
        public void WillDescribeScavengerVendorBuyAndAutolootEntries()
        {
            ScavengerManager.GetInstance().Items.Add( new ScavengerEntry { Name = "Gold", Graphic = 0x0eed, Hue = 0, Enabled = true } );

            VendorBuyAgentEntry buy = new VendorBuyAgentEntry { Name = "Bandages", Enabled = true };
            buy.Items.Add( new VendorBuyAgentItem { Name = "Bandage", Graphic = 0x0e21, Amount = 200, Enabled = true } );
            VendorBuyManager.GetInstance().Items = new List<VendorBuyAgentEntry> { buy };

            AutolootEntry loot = new AutolootEntry { Name = "Slayers", Enabled = true, Autoloot = false };
            loot.Constraints.Add( new AutolootConstraintEntry() );
            loot.Constraints.Add( new AutolootConstraintEntry() );
            _autoloot.Add( loot );

            JToken gold = Assert.Single( (JArray) GetAgent( "scavenger" )["entries"] );

            Assert.Equal( "0x0eed", gold["graphic"]?.ToString() );
            Assert.True( gold["enabled"]?.ToObject<bool>() );

            JToken bandages = Assert.Single( (JArray) GetAgent( "vendorbuy" )["entries"] );

            Assert.Equal( 1, bandages["itemCount"]?.ToObject<int>() );
            Assert.Equal( 200, bandages["items"]?[0]?["amount"]?.ToObject<int>() );

            JToken slayers = Assert.Single( (JArray) GetAgent( "autoloot" )["entries"] );

            Assert.Equal( 2, slayers["constraintCount"]?.ToObject<int>() );
            Assert.False( slayers["autoloot"]?.ToObject<bool>() );
        }

        [Fact]
        public void WillReportEmptyAgentsWhenManagersHaveNoCollection()
        {
            ScavengerManager.GetInstance().Items = null;
            VendorBuyManager.GetInstance().Items = null;
            AutolootManager.GetInstance().GetEntries = () => null;

            Assert.Empty( (JArray) GetAgent( "scavenger" )["entries"] );
            Assert.Empty( (JArray) GetAgent( "vendorbuy" )["entries"] );
            Assert.Empty( (JArray) GetAgent( "autoloot" )["entries"] );
        }
    }
}
