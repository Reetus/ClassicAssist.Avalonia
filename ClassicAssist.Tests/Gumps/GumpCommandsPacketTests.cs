using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network.PacketFilter;
using ClassicAssist.UO.Objects.Gumps;
using Xunit;

namespace ClassicAssist.Tests.Gumps
{
    /// <summary>
    ///     Drives the gump macro commands end to end: a server gump arrives as a real 0xB0 / 0xDD packet,
    ///     goes through the incoming handlers into <see cref="Engine.Gumps" /> and
    ///     <see cref="Engine.GumpList" />, and the commands then query it and answer it with a 0xB1 whose
    ///     bytes are checked against the protocol. These are the commands most scripts depend on, and the
    ///     path crosses packet decoding, parsing and packet building - any one of which can break them.
    /// </summary>
    public class GumpCommandsPacketTests : IDisposable
    {
        private const uint GumpId = 0x1A2B3C4D;
        private const int GumpSerial = 0x00ABCDEF;

        public GumpCommandsPacketTests()
        {
            EngineState.ResetWithPlayer();
            Engine.GumpList.Clear();
            Cliloc.Initialize( () => new Dictionary<int, string> { { 1000001, "Cliloc Text" } } );
        }

        public void Dispose()
        {
            Engine.GumpList.Clear();
            EngineState.Reset();
        }

        [Fact]
        public void WillRegisterGenericGumpFromPacket()
        {
            EngineState.Receive( GumpPackets.BuildGeneric( GumpSerial, GumpId, 120, 80,
                "{ page 0 }{ text 10 10 0 0 }{ button 20 20 1 2 1 0 7 }", "Hello world" ) );

            Assert.True( GumpCommands.GumpExists( GumpId ) );
            Assert.Equal( GumpSerial, Engine.GumpList[GumpId] );

            Assert.True( Engine.Gumps.GetGump( GumpId, out Gump gump ) );
            Assert.Equal( GumpSerial, gump.Serial );
            Assert.Equal( 120, gump.X );
            Assert.Equal( 80, gump.Y );
            Assert.Equal( "Hello world", gump.GetElementByXY( 10, 10 ).Text );
        }

        [Fact]
        public void WillRegisterCompressedGumpFromPacket()
        {
            EngineState.Receive( GumpPackets.BuildCompressed( GumpSerial, GumpId, 5, 6,
                "{ page 0 }{ text 10 10 0 1 }{ xmfhtmlgump 1 1 100 20 1000001 0 0 }{ htmlgump 2 2 3 4 0 0 0 }",
                "First line", "Second line" ) );

            Assert.True( GumpCommands.GumpExists( GumpId ) );
            Assert.True( Engine.Gumps.GetGump( GumpId, out Gump gump ) );
            Assert.Equal( new[] { "First line", "Second line" }, gump.Strings );
            Assert.Equal( "Second line", gump.GetElementByXY( 10, 10 ).Text );
            Assert.Equal( "Cliloc Text", gump.GetElementByXY( 1, 1 ).Text );
            Assert.Equal( "First line", gump.GetElementByXY( 2, 2 ).Text );
        }

        [Fact]
        public void WillRegisterCompressedGumpWithoutTextLines()
        {
            EngineState.Receive( GumpPackets.BuildCompressed( GumpSerial, GumpId, 0, 0,
                "{ button 20 20 1 2 1 0 7 }" ) );

            Assert.True( Engine.Gumps.GetGump( GumpId, out Gump gump ) );
            Assert.Empty( gump.Strings );
            Assert.Equal( 7, Assert.Single( gump.GumpElements ).ElementID );
        }

        [Fact]
        public void WillTrackGumpIdButNotParseAnEmptyLayout()
        {
            EngineState.Receive( GumpPackets.BuildGeneric( GumpSerial, GumpId, 0, 0, string.Empty ) );

            Assert.True( GumpCommands.GumpExists( GumpId ) );
            Assert.False( Engine.Gumps.GetGump( GumpId, out Gump _ ) );
        }

        [Fact]
        public void WontRegisterGumpWithMalformedLayoutButKeepsProcessing()
        {
            // The parse failure is reported, not thrown into the packet path.
            EngineState.Receive( GumpPackets.BuildGeneric( GumpSerial, GumpId, 0, 0, "{ button 1 2 x }" ) );

            Assert.False( Engine.Gumps.GetGump( GumpId, out Gump _ ) );
        }

        [Fact]
        public void WillMatchInGumpTextCaseInsensitivelyAcrossElementKinds()
        {
            EngineState.Receive( GumpPackets.BuildCompressed( GumpSerial, GumpId, 0, 0,
                "{ text 10 10 0 0 }{ xmfhtmlgump 1 1 100 20 1000001 0 0 }", "Bank Balance: 1000" ) );

            Assert.True( GumpCommands.InGump( GumpId, "bank balance" ) );
            Assert.True( GumpCommands.InGump( GumpId, "CLILOC" ) );
            Assert.False( GumpCommands.InGump( GumpId, "vendor" ) );
        }

        [Fact( Skip = "BUG: InGump searches Gump.GumpElements, which GumpParser fills with the last page only " )]
        public void WillMatchInGumpTextOnPageZeroOfAMultiPageGump()
        {
            EngineState.Receive( GumpPackets.BuildGeneric( GumpSerial, GumpId, 0, 0,
                "{ page 0 }{ text 10 10 0 0 }{ page 1 }{ text 10 40 0 1 }", "Runebook", "Charges: 5" ) );

            Assert.True( GumpCommands.InGump( GumpId, "Charges" ) );
            Assert.True( GumpCommands.InGump( GumpId, "Runebook" ) );
        }

        [Fact]
        public void WillReplyWithButtonAndSwitchesAndCloseClientGump()
        {
            Engine.GumpList[GumpId] = GumpSerial;

            using PacketRecorder recorder = new PacketRecorder();

            GumpCommands.ReplyGump( GumpId, 7, new[] { 3, 0x01020304 } );

            byte[] packet = Assert.Single( recorder.SentWithId( 0xB1 ) );

            byte[] expected =
            {
                0xB1, 0x00, 0x1F, // id, length 31
                0x00, 0xAB, 0xCD, 0xEF, // gump serial (the sender serial from the server)
                0x1A, 0x2B, 0x3C, 0x4D, // gump id
                0x00, 0x00, 0x00, 0x07, // button id
                0x00, 0x00, 0x00, 0x02, // switch count
                0x00, 0x00, 0x00, 0x03, // switch 1
                0x01, 0x02, 0x03, 0x04, // switch 2
                0x00, 0x00, 0x00, 0x00 // text entry count
            };

            Assert.Equal( expected, packet );

            // The server-side gump is gone and the client is told to close its copy.
            Assert.False( GumpCommands.GumpExists( GumpId ) );

            byte[] close = Assert.Single( recorder.ReceivedWithId( 0xBF ) );
            Assert.Equal( new byte[] { 0xBF, 0x00, 0x0D, 0x00, 0x04, 0x1A, 0x2B, 0x3C, 0x4D, 0, 0, 0, 0 }, close );
        }

        [Fact]
        public void WontReplyToUnknownGump()
        {
            using PacketRecorder recorder = new PacketRecorder();

            GumpCommands.ReplyGump( GumpId, 1 );

            Assert.Empty( recorder.Sent );
            Assert.Empty( recorder.Received );
        }

        [Fact]
        public void WillWriteTextEntryIdAndBigEndianText()
        {
            Engine.GumpList[GumpId] = GumpSerial;

            using PacketRecorder recorder = new PacketRecorder();

            GumpCommands.ReplyGump( GumpId, 1, null, new Dictionary<int, string> { { 6, "Hi" } } );

            byte[] packet = Assert.Single( recorder.SentWithId( 0xB1 ) );

            Assert.Equal( packet.Length, BinaryPrimitives.ReadUInt16BigEndian( packet.AsSpan( 1 ) ) );
            Assert.Equal( 0, BinaryPrimitives.ReadInt32BigEndian( packet.AsSpan( 15 ) ) ); // no switches
            Assert.Equal( 1, BinaryPrimitives.ReadInt32BigEndian( packet.AsSpan( 19 ) ) ); // one entry
            Assert.Equal( 6, BinaryPrimitives.ReadUInt16BigEndian( packet.AsSpan( 23 ) ) ); // entry id
            Assert.Equal( "Hi", Encoding.BigEndianUnicode.GetString( packet, 27, 4 ) );
        }

        [Fact( Skip = "BUG: GumpButtonClick writes text entry length as bytes (chars*2) and pads to 4 bytes per char " )]
        public void WillWriteTextEntryLengthInCharacters()
        {
            Engine.GumpList[GumpId] = GumpSerial;

            using PacketRecorder recorder = new PacketRecorder();

            GumpCommands.ReplyGump( GumpId, 1, null, new Dictionary<int, string> { { 6, "Hi" } } );

            byte[] packet = Assert.Single( recorder.SentWithId( 0xB1 ) );

            // Header 15 + switch count 4 + entry count 4 + id 2 + length 2 + "Hi" 4 = 31 bytes.
            Assert.Equal( 2, BinaryPrimitives.ReadUInt16BigEndian( packet.AsSpan( 25 ) ) );
            Assert.Equal( 31, packet.Length );
        }

        [Fact]
        public void WillRaiseResponseOnGumpWhenClientRepliesWithText()
        {
            EngineState.Receive( GumpPackets.BuildGeneric( GumpSerial, GumpId, 0, 0,
                "{ textentry 1 2 3 4 0 6 0 }{ button 5 5 1 2 1 0 9 }", "start" ) );

            Assert.True( GumpCommands.GumpExists( GumpId ) );

            EngineState.Send( GumpPackets.BuildResponse( GumpSerial, GumpId, 9, new[] { 4 }, ( 6, "typed text" ) ) );

            // The client's own reply closes the gump on both sides.
            Assert.False( GumpCommands.GumpExists( GumpId ) );
            Assert.False( Engine.Gumps.GetGump( GumpId, out Gump _ ) );
        }

        [Fact]
        public void WillCloseGumpBySerial()
        {
            EngineState.Receive( GumpPackets.BuildGeneric( GumpSerial, GumpId, 0, 0, "{ text 1 1 0 0 }", "x" ) );

            using PacketRecorder recorder = new PacketRecorder();

            GumpCommands.CloseGump( GumpSerial );

            Assert.False( Engine.Gumps.GetGump( GumpId, out Gump _ ) );
            byte[] close = Assert.Single( recorder.ReceivedWithId( 0xBF ) );
            Assert.Equal( GumpId, BinaryPrimitives.ReadUInt32BigEndian( close.AsSpan( 5 ) ) );
        }

        [Fact]
        public void WillTimeOutWaitForGumpWithMessage()
        {
            Assert.False( GumpCommands.WaitForGump( GumpId, 50 ) );
            Assert.Contains( Engine.Journal.GetEntireBuffer(), j => j.Text == Strings.Timeout___ );
        }

        [Fact]
        public async Task WillCompleteWaitForGumpOnlyForMatchingGumpId()
        {
            // WaitForGump waits on the uint gump id at offset 7 of either a 0xB0 or a 0xDD.
            byte[] other = GumpPackets.BuildGeneric( GumpSerial, GumpId + 1, 0, 0, "{ text 1 1 0 0 }", "x" );
            byte[] wanted = GumpPackets.BuildCompressed( GumpSerial, GumpId, 0, 0, "{ text 1 1 0 0 }", "x" );

            PacketWaitEntries entries = Engine.PacketWaitEntries;
            using CountdownEvent registered = new CountdownEvent( 2 );

            void OnAdded( PacketWaitEntry entry )
            {
                registered.Signal();
            }

            entries.WaitEntryAddedEvent += OnAdded;

            try
            {
                Task<bool> wait = Task.Run( () => GumpCommands.WaitForGump( GumpId, 5000 ),
                    TestContext.Current.CancellationToken );

                Assert.True( registered.Wait( 5000, TestContext.Current.CancellationToken ) );

                entries.CheckWait( other, PacketDirection.Incoming );

                Assert.NotSame( wait,
                    await Task.WhenAny( wait, Task.Delay( 100, TestContext.Current.CancellationToken ) ) );

                entries.CheckWait( wanted, PacketDirection.Incoming );

                Assert.Same( wait,
                    await Task.WhenAny( wait, Task.Delay( 5000, TestContext.Current.CancellationToken ) ) );
                Assert.True( await wait );
            }
            finally
            {
                entries.WaitEntryAddedEvent -= OnAdded;
            }

            Assert.DoesNotContain( Engine.Journal.GetEntireBuffer(), j => j.Text == Strings.Timeout___ );
        }
    }
}
