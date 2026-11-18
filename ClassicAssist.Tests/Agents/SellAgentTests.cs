using System;
using System.Collections.Generic;
using System.Text;
using ClassicAssist.Data.Vendors;
using ClassicAssist.Shared.UI.ViewModels.Agents;
using ClassicAssist.UO.Data;
using Xunit;

namespace ClassicAssist.Tests.Agents
{
    /// <summary>
    ///     The sell agent answers a vendor's sell list (0x9E) with a sell request (0x9F) for the items the
    ///     player configured. Each rule - graphic, hue, minimum price, enabled, and the amount cap - decides
    ///     whether the player's goods are sold, so a rule that stops applying sells things the player meant
    ///     to keep. The amount cap is a total across every matching stack, not a per-stack limit.
    /// </summary>
    public class SellAgentTests : IDisposable
    {
        private const int VENDOR = 0x00001234;

        private readonly VendorSellTabViewModel _viewModel;

        public SellAgentTests()
        {
            EngineState.ResetWithPlayer();

            _viewModel = new VendorSellTabViewModel();
        }

        public void Dispose()
        {
            _viewModel.Dispose();
        }

        private struct Offer
        {
            public int Serial;
            public int Graphic;
            public int Hue;
            public int Amount;
            public int Price;
        }

        /// <summary>
        ///     0x9E: id, length (2), vendor serial (4), count (2), then per item serial (4), graphic (2),
        ///     hue (2), amount (2), price (2), name length (2), name.
        /// </summary>
        private static byte[] SellList( params Offer[] offers )
        {
            const string name = "Shmoo";
            int length = 9 + offers.Length * ( 14 + name.Length );

            PacketWriter writer = new PacketWriter( length );
            writer.Write( (byte) 0x9E );
            writer.Write( (short) length );
            writer.Write( VENDOR );
            writer.Write( (short) offers.Length );

            foreach ( Offer offer in offers )
            {
                writer.Write( offer.Serial );
                writer.Write( (short) offer.Graphic );
                writer.Write( (short) offer.Hue );
                writer.Write( (short) offer.Amount );
                writer.Write( (short) offer.Price );
                writer.Write( (short) name.Length );

                byte[] nameBytes = Encoding.ASCII.GetBytes( name );
                writer.Write( nameBytes, 0, nameBytes.Length );
            }

            return writer.ToArray();
        }

        /// <summary>0x9F: id, length (2), vendor serial (4), count (2), then per item serial (4), amount (2).</summary>
        private static List<(int serial, int amount)> ParseSell( byte[] packet )
        {
            Assert.Equal( 0x9F, packet[0] );
            Assert.Equal( VENDOR, ( packet[3] << 24 ) | ( packet[4] << 16 ) | ( packet[5] << 8 ) | packet[6] );

            int count = ( packet[7] << 8 ) | packet[8];
            List<(int, int)> items = new List<(int, int)>();

            for ( int i = 0; i < count; i++ )
            {
                int offset = 9 + i * 6;
                int serial = ( packet[offset] << 24 ) | ( packet[offset + 1] << 16 ) | ( packet[offset + 2] << 8 ) |
                             packet[offset + 3];
                int amount = ( packet[offset + 4] << 8 ) | packet[offset + 5];

                items.Add( (serial, amount) );
            }

            return items;
        }

        private List<(int serial, int amount)> Run( params Offer[] offers )
        {
            using PacketRecorder recorder = new PacketRecorder();

            EngineState.Receive( SellList( offers ) );

            IReadOnlyList<byte[]> sent = recorder.SentWithId( 0x9F );

            Assert.True( sent.Count <= 1, "more than one sell request" );

            return sent.Count == 0 ? new List<(int, int)>() : ParseSell( sent[0] );
        }

        private void AddRule( int graphic, int hue = -1, int amount = -1, int minPrice = 0, bool enabled = true )
        {
            _viewModel.Items.Add( new VendorSellAgentEntry
            {
                Enabled = enabled,
                Graphic = graphic,
                Hue = hue,
                Amount = amount,
                MinPrice = minPrice,
                Name = "Shmoo"
            } );
        }

        [Fact]
        public void WontSellIncorrectHue()
        {
            AddRule( 0xFF, 0xFF );

            Assert.Empty( Run( new Offer { Serial = 0x40000010, Graphic = 0xFF, Hue = 0, Amount = 1, Price = 9 } ) );
        }

        [Fact]
        public void WillSellMatchingHue()
        {
            AddRule( 0xFF, 0x21 );

            Assert.Equal( new[] { (0x40000010, 1) },
                Run( new Offer { Serial = 0x40000010, Graphic = 0xFF, Hue = 0x21, Amount = 1, Price = 9 } ) );
        }

        [Fact]
        public void WontSellUnderMinPrice()
        {
            AddRule( 0xFF, minPrice: 10 );

            Assert.Empty( Run( new Offer { Serial = 0x40000010, Graphic = 0xFF, Amount = 1, Price = 9 } ) );
        }

        [Fact]
        public void WillSellMinPrice()
        {
            AddRule( 0xFF, minPrice: 9 );

            Assert.Equal( new[] { (0x40000010, 1) },
                Run( new Offer { Serial = 0x40000010, Graphic = 0xFF, Amount = 1, Price = 9 } ) );
        }

        [Fact]
        public void WontSellDisabledEntry()
        {
            AddRule( 0xFF, enabled: false );

            Assert.Empty( Run( new Offer { Serial = 0x40000010, Graphic = 0xFF, Amount = 1, Price = 9 } ) );
        }

        [Fact]
        public void WontSellMoreThanAmount()
        {
            AddRule( 0xFF, amount: 10 );

            Assert.Equal( new[] { (0x40000010, 10) },
                Run( new Offer { Serial = 0x40000010, Graphic = 0xFF, Amount = 100, Price = 9 } ) );
        }

        /// <summary>
        ///     The cap is shared: 6 + 6 offered against a cap of 10 sells 6 then 4, and a third stack
        ///     after the cap is used up is left out of the request entirely.
        /// </summary>
        [Fact]
        public void WillShareAmountAcrossStacks()
        {
            AddRule( 0xFF, amount: 10 );

            List<(int serial, int amount)> sold = Run(
                new Offer { Serial = 0x40000010, Graphic = 0xFF, Amount = 6, Price = 9 },
                new Offer { Serial = 0x40000011, Graphic = 0xFF, Amount = 6, Price = 9 },
                new Offer { Serial = 0x40000012, Graphic = 0xFF, Amount = 6, Price = 9 } );

            Assert.Equal( new[] { (0x40000010, 6), (0x40000011, 4) }, sold );
        }

        [Fact]
        public void WillSellOnlyMatchingGraphicsFromAMixedList()
        {
            AddRule( 0xFF );

            List<(int serial, int amount)> sold = Run(
                new Offer { Serial = 0x40000010, Graphic = 0xFF, Amount = 2, Price = 9 },
                new Offer { Serial = 0x40000011, Graphic = 0x100, Amount = 3, Price = 9 } );

            Assert.Equal( new[] { (0x40000010, 2) }, sold );
        }

        [Fact]
        public void MatchAnyWillSellEverything()
        {
            _viewModel.InsertMatchAnyCommand.Execute( null );

            List<(int serial, int amount)> sold = Run(
                new Offer { Serial = 0x40000010, Graphic = 0xFF, Hue = 5, Amount = 2, Price = 1 },
                new Offer { Serial = 0x40000011, Graphic = 0x100, Amount = 3, Price = 0 } );

            Assert.Equal( new[] { (0x40000010, 2), (0x40000011, 3) }, sold );
        }

        /// <summary>
        ///     With a sell container set that the player does not have, nothing is sold - selling from the
        ///     whole backpack instead would sell exactly what the container was meant to protect.
        /// </summary>
        [Fact]
        public void WontSellWhenTheSellContainerIsMissing()
        {
            AddRule( 0xFF );
            _viewModel.ContainerSerial = 0x40000099;

            Assert.Empty( Run( new Offer { Serial = 0x40000010, Graphic = 0xFF, Amount = 1, Price = 9 } ) );
        }
    }
}
