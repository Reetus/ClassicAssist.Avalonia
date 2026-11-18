using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Hotkeys.Commands;
using ClassicAssist.Data;
using ClassicAssist.Data.BuffIcons;
using ClassicAssist.Data.Skills;
using ClassicAssist.Mcp;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UI.Misc;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;
using ClassicAssist.UO.Objects.Gumps;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.Mcp
{
    /// <summary>
    ///     The read-only game state tools an agent uses to see the world (player, backpack, nearby
    ///     mobiles/items, containers, gumps, journal, buffs, skills, target, hotkeys), driven against a
    ///     populated world. Pins the JSON shape agents parse (hex serial formatting, field names), the
    ///     paging and filtering contracts (offset/limit/truncated, journal counting back from the newest
    ///     entry), and that bad or missing arguments come back as a tool error rather than an exception.
    /// </summary>
    public class McpGameStateToolsTests : IDisposable
    {
        private readonly ObservableCollectionEx<SkillEntry> _originalSkills;
        private readonly ObservableCollectionEx<HotkeyCommand> _originalHotkeys;

        public McpGameStateToolsTests()
        {
            EngineState.Reset();

            _originalSkills = SkillManager.GetInstance().Items;
            _originalHotkeys = HotkeyManager.GetInstance().Items;

            SkillManager.GetInstance().Items = new ObservableCollectionEx<SkillEntry>();
            HotkeyManager.GetInstance().Items = new ObservableCollectionEx<HotkeyCommand>();
        }

        public void Dispose()
        {
            SkillManager.GetInstance().Items = _originalSkills;
            HotkeyManager.GetInstance().Items = _originalHotkeys;

            EngineState.Reset();
        }

        private static JObject Ok( string tool, JObject args = null )
        {
            CallToolResult result = McpTools.Invoke( tool, args ?? new JObject() );

            Assert.False( result.IsError, $"{tool} failed: {result.Content.FirstOrDefault()?.Text}" );

            return JObject.Parse( result.Content.Single().Text );
        }

        private static string Fail( string tool, JObject args = null )
        {
            CallToolResult result = McpTools.Invoke( tool, args ?? new JObject() );

            Assert.True( result.IsError, $"{tool} should have failed but returned {result.Content.FirstOrDefault()?.Text}" );

            return result.Content.Single().Text;
        }

        private static string[] Serials( JToken array )
        {
            return array.Select( t => t["serial"]?.ToString() ).ToArray();
        }

        [Theory]
        [InlineData( "getPlayer" )]
        [InlineData( "getBackpack" )]
        [InlineData( "getMobiles" )]
        [InlineData( "getItems" )]
        public void WillReportNotConnectedWhenThereIsNoPlayer( string tool )
        {
            Assert.Contains( "Not connected", Fail( tool ) );
        }

        [Fact]
        public void WillReportPlayerFieldsWithHexSerial()
        {
            PlayerMobile player = EngineState.ResetWithPlayer( 0x00abcdef );

            player.Name = "Shmoo";
            player.Hits = 50;
            player.HitsMax = 60;
            player.Strength = 100;
            player.Gold = 1234;
            player.X = 1000;
            player.Y = 2000;
            player.Z = -5;
            player.Map = Map.Trammel;
            player.Notoriety = Notoriety.Murderer;

            JObject result = Ok( "getPlayer" );

            Assert.Equal( "Shmoo", result["name"]?.ToString() );
            Assert.Equal( "0x00abcdef", result["serial"]?.ToString() );
            Assert.Equal( 50, result["hits"]?.ToObject<int>() );
            Assert.Equal( 60, result["hitsMax"]?.ToObject<int>() );
            Assert.Equal( 100, result["strength"]?.ToObject<int>() );
            Assert.Equal( 1234, result["gold"]?.ToObject<int>() );
            Assert.Equal( -5, result["z"]?.ToObject<int>() );
            Assert.Equal( "Trammel", result["map"]?.ToString() );
            Assert.Equal( "Murderer", result["notoriety"]?.ToString() );
        }

        [Fact]
        public void WillReportBackpackMissingOrClosed()
        {
            PlayerMobile player = EngineState.ResetWithPlayer();

            player.Backpack.Container = null;

            Assert.Contains( "not open", Fail( "getBackpack" ) );

            player.SetLayer( Layer.Backpack, 0 );

            Assert.Contains( "Backpack not found", Fail( "getBackpack" ) );
        }

        [Fact]
        public void WillFilterBackpackByNameCaseInsensitively()
        {
            PlayerMobile player = EngineState.ResetWithPlayer();
            ItemCollection contents = player.Backpack.Container;

            contents.Add( new Item( 0x40000010, player.Backpack.Serial ) { Name = "Black Pearl", ID = 0x0f7a, Count = 25, Hue = 0 } );
            contents.Add( new Item( 0x40000011, player.Backpack.Serial ) { Name = "Bloodmoss", ID = 0x0f7b, Count = 3 } );
            contents.Add( new Item( 0x40000012, player.Backpack.Serial ) { Name = "Bandage", ID = 0x0e21 } );

            JObject result = Ok( "getBackpack", new JObject { ["filter"] = "PEARL" } );

            JObject pearl = (JObject) Assert.Single( (JArray) result["items"] );

            Assert.Equal( "0x40000010", pearl["serial"]?.ToString() );
            Assert.Equal( "0x0f7a", pearl["graphic"]?.ToString() );
            Assert.Equal( 25, pearl["count"]?.ToObject<int>() );
            Assert.Equal( "0x40000001", result["backpackSerial"]?.ToString() );
            Assert.Equal( 1, result["total"]?.ToObject<int>() );
            Assert.False( result["truncated"]?.ToObject<bool>() );
        }

        /// <summary>
        ///     Container order is a hash order, so this checks the paging contract rather than which item
        ///     lands on which page: pages are disjoint, cover everything, and truncated is set until the end.
        /// </summary>
        [Fact]
        public void WillPageBackpackWithoutOverlapOrGaps()
        {
            PlayerMobile player = EngineState.ResetWithPlayer();

            for ( int i = 0; i < 5; i++ )
            {
                player.Backpack.Container.Add( new Item( 0x40000100 + i, player.Backpack.Serial ) { Name = $"Item {i}" } );
            }

            List<string> seen = new List<string>();

            for ( int offset = 0; offset < 5; offset += 2 )
            {
                JObject page = Ok( "getBackpack", new JObject { ["limit"] = 2, ["offset"] = offset } );

                Assert.Equal( 5, page["total"]?.ToObject<int>() );
                Assert.Equal( offset, page["offset"]?.ToObject<int>() );
                Assert.Equal( offset + 2 < 5, page["truncated"]?.ToObject<bool>() );
                Assert.Equal( page["returned"]?.ToObject<int>(), page["itemCount"]?.ToObject<int>() );

                seen.AddRange( Serials( page["items"] ) );
            }

            Assert.Equal( 5, seen.Distinct().Count() );
            Assert.Equal( 5, seen.Count );
        }

        [Fact]
        public void WillReturnEmptyPageWhenOffsetIsPastTheEnd()
        {
            PlayerMobile player = EngineState.ResetWithPlayer();

            player.Backpack.Container.Add( new Item( 0x40000100, player.Backpack.Serial ) { Name = "Only" } );

            JObject page = Ok( "getBackpack", new JObject { ["offset"] = 10 } );

            Assert.Empty( (JArray) page["items"] );
            Assert.Equal( 1, page["total"]?.ToObject<int>() );
            Assert.False( page["truncated"]?.ToObject<bool>() );
        }

        [Fact]
        public void WillClampNegativeLimitAndOffsetToZero()
        {
            PlayerMobile player = EngineState.ResetWithPlayer();

            player.Backpack.Container.Add( new Item( 0x40000100, player.Backpack.Serial ) { Name = "Only" } );

            JObject page = Ok( "getBackpack", new JObject { ["limit"] = -3, ["offset"] = -7 } );

            Assert.Equal( 0, page["offset"]?.ToObject<int>() );
            Assert.Equal( 0, page["limit"]?.ToObject<int>() );
            Assert.Empty( (JArray) page["items"] );
            Assert.True( page["truncated"]?.ToObject<bool>() );
        }

        [Fact]
        public void WillAcceptHexStringForIntegerArguments()
        {
            PlayerMobile player = EngineState.ResetWithPlayer();

            for ( int i = 0; i < 20; i++ )
            {
                player.Backpack.Container.Add( new Item( 0x40000100 + i, player.Backpack.Serial ) { Name = $"Item {i}" } );
            }

            JObject page = Ok( "getBackpack", new JObject { ["limit"] = "0x10" } );

            Assert.Equal( 16, page["limit"]?.ToObject<int>() );
            Assert.Equal( 16, page["returned"]?.ToObject<int>() );
        }

        [Fact]
        public void WillReportIntegerArgumentOverflowAsToolError()
        {
            EngineState.ResetWithPlayer();

            Fail( "getBackpack", new JObject { ["limit"] = 99999999999L } );
        }

        [Fact]
        public void WillListMobilesWithinRangeOfThePlayer()
        {
            PlayerMobile player = EngineState.ResetWithPlayer();

            player.X = 100;
            player.Y = 100;

            Engine.Mobiles.Add( new Mobile( 0x0200 ) { Name = "Near", X = 105, Y = 97, ID = 0x190, Hits = 10, HitsMax = 25, Notoriety = Notoriety.Enemy } );
            Engine.Mobiles.Add( new Mobile( 0x0201 ) { Name = "Edge", X = 90, Y = 100 } );
            Engine.Mobiles.Add( new Mobile( 0x0202 ) { Name = "Far", X = 100, Y = 111 } );

            JObject defaultRange = Ok( "getMobiles" );
            string[] serials = Serials( defaultRange["mobiles"] );

            Assert.Equal( 10, defaultRange["range"]?.ToObject<int>() );
            Assert.Contains( "0x00000200", serials );
            Assert.Contains( "0x00000201", serials );
            Assert.DoesNotContain( "0x00000202", serials );

            JObject near = (JObject) defaultRange["mobiles"]?.First( m => m["serial"]?.ToString() == "0x00000200" );

            Assert.Equal( 5, near?["distance"]?.ToObject<int>() );
            Assert.Equal( "0x0190", near?["graphic"]?.ToString() );
            Assert.Equal( "Enemy", near?["notoriety"]?.ToString() );
            Assert.Equal( 25, near?["hitsMax"]?.ToObject<int>() );

            string[] tight = Serials( Ok( "getMobiles", new JObject { ["range"] = 5 } )["mobiles"] );

            Assert.Contains( "0x00000200", tight );
            Assert.DoesNotContain( "0x00000201", tight );
        }

        [Fact]
        public void WillListItemsWithinRangeAndFlagContainers()
        {
            PlayerMobile player = EngineState.ResetWithPlayer();

            player.X = 100;
            player.Y = 100;

            Engine.Items.Add( new Item( 0x40000300 ) { Name = "Chest", X = 102, Y = 102, Container = new ItemCollection( 0x40000300 ) } );
            Engine.Items.Add( new Item( 0x40000301 ) { Name = "Gold", X = 99, Y = 100, Count = 500 } );
            Engine.Items.Add( new Item( 0x40000302 ) { Name = "Distant", X = 200, Y = 200 } );

            JObject result = Ok( "getItems", new JObject { ["range"] = 3 } );
            JToken[] items = ( (JArray) result["items"] ).ToArray();

            JToken chest = items.Single( i => i["serial"]?.ToString() == "0x40000300" );
            JToken gold = items.Single( i => i["serial"]?.ToString() == "0x40000301" );

            Assert.True( chest["isContainer"]?.ToObject<bool>() );
            Assert.False( gold["isContainer"]?.ToObject<bool>() );
            Assert.Equal( 500, gold["count"]?.ToObject<int>() );
            Assert.DoesNotContain( items, i => i["serial"]?.ToString() == "0x40000302" );
        }

        [Fact]
        public void WillReadOpenContainerAndRejectClosedOrUnknown()
        {
            EngineState.ResetWithPlayer();

            Item chest = new Item( 0x40000400 ) { Name = "Chest", Container = new ItemCollection( 0x40000400 ) };
            chest.Container.Add( new Item( 0x40000401, chest.Serial ) { Name = "Ruby", X = 44, Y = 65, ID = 0x0f13 } );
            Engine.Items.Add( chest );
            Engine.Items.Add( new Item( 0x40000402 ) { Name = "Closed" } );

            JObject result = Ok( "getContainer", new JObject { ["serial"] = "1073742848" } );
            JObject ruby = (JObject) Assert.Single( (JArray) result["items"] );

            Assert.Equal( "0x40000400", result["containerSerial"]?.ToString() );
            Assert.Equal( 44, ruby["x"]?.ToObject<int>() );
            Assert.Equal( 65, ruby["y"]?.ToObject<int>() );

            Assert.Contains( "not an open container", Fail( "getContainer", new JObject { ["serial"] = "0x40000402" } ) );
            Assert.Contains( "not an open container", Fail( "getContainer", new JObject { ["serial"] = "0x40009999" } ) );
            Assert.Contains( "Invalid serial", Fail( "getContainer", new JObject { ["serial"] = "chest" } ) );
            Assert.Contains( "'serial'", Fail( "getContainer" ) );
        }

        [Fact]
        public void WillDescribeItemWithOwnerLayerAndProperties()
        {
            EngineState.ResetWithPlayer();

            Engine.Items.Add( new Item( 0x40000500, 0x01 )
            {
                Name = "Katana",
                ID = 0x13ff,
                Hue = 0x0481,
                Layer = Layer.OneHanded,
                Properties = new[]
                {
                    new Property { Cliloc = 1060400, Text = "Hit Lightning 50%", Arguments = new[] { "50" } },
                    new Property { Cliloc = 1061168, Text = "Weapon Damage 10 - 14" }
                }
            } );

            JObject result = Ok( "getItemInfo", new JObject { ["serial"] = "0x40000500" } );

            Assert.Equal( "Item", result["type"]?.ToString() );
            Assert.Equal( "0x00000001", result["owner"]?.ToString() );
            Assert.Equal( "OneHanded", result["layer"]?.ToString() );
            Assert.Equal( 0x0481, result["hue"]?.ToObject<int>() );

            JArray properties = (JArray) result["properties"];

            Assert.Equal( 2, properties.Count );
            Assert.Equal( 1060400, properties[0]["cliloc"]?.ToObject<int>() );
            Assert.Equal( "50", properties[0]["arguments"]?[0]?.ToString() );
            Assert.Equal( JTokenType.Null, properties[1]["arguments"]?.Type );
        }

        [Fact]
        public void WillOmitOwnerForWorldItemAndReportEmptyProperties()
        {
            EngineState.ResetWithPlayer();

            Engine.Items.Add( new Item( 0x40000501 ) { Name = "Rock" } );

            JObject result = Ok( "getItemInfo", new JObject { ["serial"] = "0x40000501" } );

            Assert.Null( result["owner"] );
            Assert.Empty( (JArray) result["properties"] );
        }

        [Fact]
        public void WillDescribeMobileAndFindPlayerOutsideTheMobileList()
        {
            Engine.Mobiles.Add( new Mobile( 0x0300 ) { Name = "Orc", Hits = 3, HitsMax = 9, Notoriety = Notoriety.Murderer } );

            JObject orc = Ok( "getItemInfo", new JObject { ["serial"] = "0x300" } );

            Assert.Equal( "Mobile", orc["type"]?.ToString() );
            Assert.Equal( 9, orc["hitsMax"]?.ToObject<int>() );
            Assert.Null( orc["count"] );

            // The player is only Engine.Player here - not in Engine.Mobiles - and must still be found.
            Engine.Player = new PlayerMobile( 0x0042 ) { Name = "Me" };

            JObject me = Ok( "getItemInfo", new JObject { ["serial"] = "66" } );

            Assert.Equal( "PlayerMobile", me["type"]?.ToString() );
            Assert.Equal( "Me", me["name"]?.ToString() );
        }

        [Fact]
        public void WillReportUnknownOrMalformedSerialForItemInfo()
        {
            Assert.Contains( "not found", Fail( "getItemInfo", new JObject { ["serial"] = "0x40001234" } ) );
            Assert.Contains( "Invalid serial", Fail( "getItemInfo", new JObject { ["serial"] = "zz-top" } ) );
            Assert.Contains( "'serial'", Fail( "getItemInfo", new JObject { ["serial"] = "" } ) );
        }

        private static Gump AddGump( uint id, int serial, params string[] texts )
        {
            GumpElement[] elements = texts.Select( ( t, i ) => new GumpElement
            {
                Type = ElementType.htmlgump, Text = t, X = i * 10, Y = 5, ElementID = i
            } ).ToArray();

            Gump gump = new Gump( 50, 60, id, serial, "{ page 0 }{ htmlgump 0 5 }", texts, elements, Array.Empty<GumpPage>() );

            Engine.Gumps.Add( gump );

            return gump;
        }

        [Fact]
        public void WillListOpenGumpsWithHexIds()
        {
            AddGump( 0x1234, 0x0abc );

            JArray gumps = JArray.Parse( McpTools.Invoke( "listGumps", new JObject() ).Content.Single().Text );
            JToken gump = Assert.Single( gumps );

            Assert.Equal( "0x00001234", gump["id"]?.ToString() );
            Assert.Equal( "0x00000abc", gump["serial"]?.ToString() );
            Assert.Equal( 50, gump["x"]?.ToObject<int>() );
        }

        [Fact]
        public void WillReturnEmptyGumpListWhenNoneAreOpen()
        {
            Assert.Empty( JArray.Parse( McpTools.Invoke( "listGumps", new JObject() ).Content.Single().Text ) );
        }

        [Fact]
        public void WillFindGumpLayoutByIdOrSerial()
        {
            AddGump( 0x1234, 0x0abc, "Hello", "World" );
            AddGump( 0x5678, 0x0def, "Other" );

            JObject byId = Ok( "getGumpLayout", new JObject { ["id"] = "0x1234" } );

            Assert.Equal( "0x00000abc", byId["serial"]?.ToString() );
            Assert.Equal( new[] { "Hello", "World" }, byId["strings"]?.ToObject<string[]>() );
            Assert.Equal( 2, ( (JArray) byId["elements"] ).Count );
            Assert.Equal( "htmlgump", byId["elements"]?[1]?["type"]?.ToString() );
            Assert.Equal( "World", byId["elements"]?[1]?["text"]?.ToString() );
            Assert.Equal( 0, byId["pageCount"]?.ToObject<int>() );
            Assert.StartsWith( "{ page 0 }", byId["layout"]?.ToString() );

            JObject bySerial = Ok( "getGumpLayout", new JObject { ["serial"] = "3567" } );

            Assert.Equal( "0x00005678", bySerial["id"]?.ToString() );
        }

        [Fact]
        public void WillUseTheOnlyOpenGumpWhenNoIdIsGiven()
        {
            AddGump( 0x1234, 0x0abc, "Only" );

            Assert.Equal( "0x00001234", Ok( "getGumpLayout" )["id"]?.ToString() );
        }

        [Fact]
        public void WillRefuseToGuessBetweenSeveralGumps()
        {
            AddGump( 0x1234, 0x0abc );
            AddGump( 0x5678, 0x0def );

            Assert.Contains( "Gump not found", Fail( "getGumpLayout" ) );
            Assert.Contains( "Gump not found", Fail( "getGumpLayout", new JObject { ["id"] = "0x9999" } ) );
        }

        /// <summary>
        ///     A malformed id falls through to the "only one gump open" convenience, so the agent gets an
        ///     unrelated gump back as if it had matched.
        /// </summary>
        [Fact( Skip = "BUG: getGumpLayout returns the only open gump when the id is malformed instead of an error" )]
        public void WillRejectMalformedGumpIdEvenWhenOneGumpIsOpen()
        {
            AddGump( 0x1234, 0x0abc );

            Fail( "getGumpLayout", new JObject { ["id"] = "not-a-gump" } );
        }

        private static void WriteJournal( string text, string name = "System", JournalSpeech type = JournalSpeech.System, int serial = 0 )
        {
            Engine.Journal.Write( new JournalEntry { Text = text, Name = name, SpeechType = type, Serial = serial, Cliloc = 0 } );
        }

        [Fact]
        public void WillReturnMostRecentJournalEntriesInChronologicalOrder()
        {
            for ( int i = 1; i <= 5; i++ )
            {
                WriteJournal( $"line {i}" );
            }

            JObject result = Ok( "getJournal", new JObject { ["count"] = 2 } );
            string[] texts = result["entries"]?.Select( e => e["text"]?.ToString() ).ToArray();

            Assert.Equal( new[] { "line 4", "line 5" }, texts );
            Assert.Equal( 5, result["total"]?.ToObject<int>() );
            Assert.True( result["truncated"]?.ToObject<bool>() );
        }

        [Fact]
        public void WillSkipFromTheNewestEndWithOffset()
        {
            for ( int i = 1; i <= 5; i++ )
            {
                WriteJournal( $"line {i}" );
            }

            string[] texts = Ok( "getJournal", new JObject { ["count"] = 2, ["offset"] = 1 } )["entries"]
                ?.Select( e => e["text"]?.ToString() ).ToArray();

            Assert.Equal( new[] { "line 3", "line 4" }, texts );

            // Asking past the oldest entry returns what's left, then nothing.
            Assert.Single( (JArray) Ok( "getJournal", new JObject { ["count"] = 3, ["offset"] = 4 } )["entries"] );
            Assert.Empty( (JArray) Ok( "getJournal", new JObject { ["offset"] = 9 } )["entries"] );
        }

        [Fact]
        public void WillFilterJournalBeforeCountingAndReportAuthorAndSerial()
        {
            WriteJournal( "You see: a cow", "a cow", JournalSpeech.Label, 0x0055 );
            WriteJournal( "Unrelated" );
            WriteJournal( "Moo! said the COW", "a cow", JournalSpeech.Say, 0x0055 );
            WriteJournal( "Also unrelated" );

            JObject result = Ok( "getJournal", new JObject { ["filter"] = "cow", ["count"] = 1 } );
            JObject entry = (JObject) Assert.Single( (JArray) result["entries"] );

            Assert.Equal( "Moo! said the COW", entry["text"]?.ToString() );
            Assert.Equal( "a cow", entry["author"]?.ToString() );
            Assert.Equal( "0x00000055", entry["serial"]?.ToString() );
            Assert.Equal( "Say", entry["speechType"]?.ToString() );
            Assert.Equal( 2, result["total"]?.ToObject<int>() );

            JObject system = (JObject) Ok( "getJournal", new JObject { ["filter"] = "also" } )["entries"]?[0];

            Assert.Equal( JTokenType.Null, system?["serial"]?.Type );
        }

        [Fact]
        public void WillReturnNoJournalEntriesForNegativeCount()
        {
            WriteJournal( "line" );

            Assert.Empty( (JArray) Ok( "getJournal", new JObject { ["count"] = -1 } )["entries"] );
        }

        private static byte[] BuffPacket( int type, bool enabled, short durationSeconds )
        {
            PacketWriter writer = new PacketWriter( enabled ? 25 : 11 );

            writer.Write( (byte) 0xDF );
            writer.Write( (short) ( enabled ? 25 : 11 ) );
            writer.Write( 0x01 ); // mobile serial
            writer.Write( (short) type );
            writer.Write( (short) ( enabled ? 1 : 0 ) ); // count

            if ( enabled )
            {
                writer.Fill( 12 ); // fields before the duration
                writer.Write( durationSeconds );
            }

            return writer.ToArray();
        }

        [Fact]
        public void WillReportActiveBuffWithRemainingTime()
        {
            // The manager subscribes to the buff packet event when first created.
            BuffIconManager.GetInstance();

            // 1005 = Night Sight in Data/BuffIcons.json.
            EngineState.Receive( BuffPacket( 1005, true, 60 ) );

            try
            {
                JObject result = Ok( "getBuffs", new JObject { ["filter"] = "NIGHT" } );
                JObject buff = (JObject) Assert.Single( (JArray) result["buffs"] );

                Assert.Equal( "Night Sight", buff["name"]?.ToString() );
                Assert.Equal( 1005, buff["id"]?.ToObject<int>() );

                long remaining = buff["remainingMs"]?.ToObject<long>() ?? 0;

                Assert.InRange( remaining, 50000, 60000 );
            }
            finally
            {
                EngineState.Receive( BuffPacket( 1005, false, 0 ) );
            }

            Assert.Empty( (JArray) Ok( "getBuffs", new JObject { ["filter"] = "night" } )["buffs"] );
        }

        private static void AddSkill( int id, string name, float value, LockStatus lockStatus )
        {
            SkillManager.GetInstance().Items.Add( new SkillEntry
            {
                Skill = new Skill { ID = id, Name = name },
                Value = value,
                Base = value - 1,
                Cap = 100,
                Delta = 0.5,
                LockStatus = lockStatus
            } );
        }

        [Fact]
        public void WillListSkillsSortedByNameAndFiltered()
        {
            AddSkill( 27, "Tactics", 90.5f, LockStatus.Locked );
            AddSkill( 1, "Anatomy", 50, LockStatus.Up );
            AddSkill( 17, "Healing", 70, LockStatus.Down );

            JObject all = Ok( "getSkills" );

            Assert.Equal( new[] { "Anatomy", "Healing", "Tactics" }, all["skills"]?.Select( s => s["name"]?.ToString() ).ToArray() );

            JObject tactics = (JObject) Assert.Single( (JArray) Ok( "getSkills", new JObject { ["filter"] = "TACT" } )["skills"] );

            Assert.Equal( 27, tactics["id"]?.ToObject<int>() );
            Assert.Equal( 90.5, tactics["value"]?.ToObject<double>() ?? 0, 3 );
            Assert.Equal( 89.5, tactics["base"]?.ToObject<double>() ?? 0, 3 );
            Assert.Equal( "Locked", tactics["lock"]?.ToString() );
        }

        [Fact]
        public void WillGetSkillBySubstring()
        {
            AddSkill( 17, "Healing", 70, LockStatus.Down );
            AddSkill( 46, "Spirit Speak", 30, LockStatus.Up );

            Assert.Equal( "Spirit Speak", Ok( "getSkill", new JObject { ["name"] = "spirit" } )["name"]?.ToString() );
            Assert.Contains( "'name'", Fail( "getSkill" ) );
        }

        [Fact]
        public void WillReportPendingTargetAndPlayerTargetSerials()
        {
            PlayerMobile player = EngineState.ResetWithPlayer();

            Engine.TargetExists = true;
            Engine.TargetSerial = 0x00112233;
            Engine.TargetType = TargetType.Tile;
            Engine.TargetFlags = TargetFlags.Harmful;
            player.LastTargetSerial = 0x0500;
            player.EnemyTargetSerial = 0x0600;

            JObject result = Ok( "getTarget" );

            Assert.True( result["exists"]?.ToObject<bool>() );
            Assert.Equal( "0x00112233", result["serial"]?.ToString() );
            Assert.Equal( "Tile", result["type"]?.ToString() );
            Assert.Equal( "Harmful", result["flags"]?.ToString() );
            Assert.Equal( "0x00000500", result["lastTargetSerial"]?.ToString() );
            Assert.Equal( "0x00000600", result["enemyTargetSerial"]?.ToString() );
            Assert.Equal( JTokenType.Null, result["friendTargetSerial"]?.Type );
        }

        [Fact]
        public void WillHideTargetSerialWhenNoCursorIsUp()
        {
            Engine.TargetSerial = 0x00112233;

            JObject result = Ok( "getTarget" );

            Assert.False( result["exists"]?.ToObject<bool>() );
            Assert.Equal( JTokenType.Null, result["serial"]?.Type );
            Assert.Equal( JTokenType.Null, result["lastTargetSerial"]?.Type );
        }

        private static HotkeyCommand Hotkey( string name, Action<HotkeyEntry, object[]> action )
        {
            return new HotkeyCommand { Name = name, Action = action, IsCategory = false };
        }

        [Fact]
        public void WillListHotkeysFlatteningCategories()
        {
            HotkeyCommand category = new HotkeyCommand { Name = "Agents", IsCategory = true, Action = null };

            category.Children.Add( Hotkey( "Dress Agent", ( _, _ ) => { } ) );
            category.Children.Add( Hotkey( "Undress Agent", null ) );

            HotkeyManager.GetInstance().Items.Add( category );
            HotkeyManager.GetInstance().Items.Add( Hotkey( "Lone Hotkey", ( _, _ ) => { } ) );

            JObject all = Ok( "getHotkeys" );
            string[] names = all["hotkeys"]?.Select( h => h["name"]?.ToString() ).ToArray();

            // The category is represented by its children, not listed itself.
            Assert.Equal( new[] { "Dress Agent", "Undress Agent", "Lone Hotkey" }, names );
            Assert.Equal( 3, all["hotkeyCount"]?.ToObject<int>() );

            JObject undress = (JObject) Assert.Single( (JArray) Ok( "getHotkeys", new JObject { ["filter"] = "UNDRESS" } )["hotkeys"] );

            Assert.False( undress["hasAction"]?.ToObject<bool>() );
            Assert.Equal( nameof( HotkeyCommand ), undress["type"]?.ToString() );
        }

        [Fact]
        public void WillExecuteFirstMatchingHotkeyThatHasAnAction()
        {
            using ManualResetEventSlim ran = new ManualResetEventSlim();

            bool wrongRan = false;

            HotkeyCommand category = new HotkeyCommand { Name = "Misc", IsCategory = true };

            category.Children.Add( Hotkey( "Bandage Self", null ) );
            category.Children.Add( Hotkey( "bandage self", ( _, _ ) => ran.Set() ) );

            HotkeyManager.GetInstance().Items.Add( category );
            HotkeyManager.GetInstance().Items.Add( Hotkey( "Bandage Selfish", ( _, _ ) => wrongRan = true ) );

            CallToolResult result = McpTools.Invoke( "executeHotkey", new JObject { ["name"] = "BANDAGE SELF" } );

            Assert.False( result.IsError, result.Content.Single().Text );
            Assert.True( ran.Wait( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken ) );
            Assert.False( wrongRan );
        }

        [Fact]
        public void WillNotFailRequestWhenHotkeyActionThrows()
        {
            using ManualResetEventSlim ran = new ManualResetEventSlim();

            HotkeyManager.GetInstance().Items.Add( Hotkey( "Broken", ( _, _ ) =>
            {
                ran.Set();
                throw new InvalidOperationException( "boom" );
            } ) );

            CallToolResult result = McpTools.Invoke( "executeHotkey", new JObject { ["name"] = "Broken" } );

            Assert.False( result.IsError );
            Assert.True( ran.Wait( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken ) );
        }

        [Fact]
        public void WillReportHotkeyWithoutActionAsNotFound()
        {
            HotkeyManager.GetInstance().Items.Add( Hotkey( "Inert", null ) );

            Assert.Contains( "not found or has no action", Fail( "executeHotkey", new JObject { ["name"] = "Inert" } ) );
            Assert.Contains( "'name'", Fail( "executeHotkey" ) );
        }

        [Theory]
        [InlineData( 6 )]
        [InlineData( -1 )]
        public void WillRejectMapOutsideTheKnownFacets( int map )
        {
            Assert.Contains( "between 0 and 5", Fail( "getTileInfo", new JObject { ["x"] = 1, ["y"] = 1, ["map"] = map } ) );
        }

        [Fact]
        public void WillRequireTileCoordinates()
        {
            Assert.Contains( "'y'", Fail( "getTileInfo", new JObject { ["x"] = 1 } ) );
            Assert.Contains( "'x'", Fail( "getTileInfo", new JObject { ["x"] = 1.5, ["y"] = 1 } ) );
        }

        [Fact]
        public void WillRequireAValidClilocNumber()
        {
            Assert.Contains( "'cliloc'", Fail( "getCliloc" ) );
            Assert.Contains( "'cliloc'", Fail( "getCliloc", new JObject { ["cliloc"] = "sacred journey" } ) );
        }
    }
}
