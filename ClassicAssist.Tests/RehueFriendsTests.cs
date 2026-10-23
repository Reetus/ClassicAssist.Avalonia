using System.Collections.Generic;
using ClassicAssist.Data;
using ClassicAssist.Data.Friends;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Shared;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class RehueFriendsTests
    {
        private const int FriendSerial = 0x00005678;
        private const int FriendHue = 0x0123;

        private bool _previousRehue;
        private bool _previousBody;
        private int _previousHue;
        private HashSet<Layer> _previousExcluded;

        [TestInitialize]
        public void Initialize()
        {
            IncomingPacketFilters.Initialize();

            Options options = Options.CurrentOptions;

            _previousRehue = options.RehueFriends;
            _previousBody = options.RehueFriendsBody;
            _previousHue = options.RehueFriendsHue;
            _previousExcluded = options.RehueFriendsExcludedLayers;

            options.RehueFriends = true;
            options.RehueFriendsBody = true;
            options.RehueFriendsHue = FriendHue;
            options.RehueFriendsExcludedLayers = [];
            options.Friends.Add( new FriendEntry { Name = "Friend", Serial = FriendSerial } );
        }

        [TestCleanup]
        public void Cleanup()
        {
            Options options = Options.CurrentOptions;

            options.RehueFriends = _previousRehue;
            options.RehueFriendsBody = _previousBody;
            options.RehueFriendsHue = _previousHue;
            options.RehueFriendsExcludedLayers = _previousExcluded;

            for ( int i = options.Friends.Count - 1; i >= 0; i-- )
            {
                if ( options.Friends[i].Serial == FriendSerial )
                {
                    options.Friends.RemoveAt( i );
                }
            }
        }

        private static byte[] WornItem( int mobileSerial, Layer layer, int hue )
        {
            return new byte[]
            {
                0x2E, 0x40, 0x00, 0x00, 0x01, 0x0F, 0x51, 0x00, (byte) layer, (byte) ( mobileSerial >> 24 ),
                (byte) ( mobileSerial >> 16 ), (byte) ( mobileSerial >> 8 ), (byte) mobileSerial, (byte) ( hue >> 8 ),
                (byte) hue
            };
        }

        private static byte[] CheckWornItem( byte[] packet, out bool rehued )
        {
            byte[] sent = null;

            void OnSent( byte[] data, int length )
            {
                sent = data;
            }

            Engine.InternalPacketReceivedEvent += OnSent;

            try
            {
                rehued = Engine.RehueList.CheckWornItem( packet, packet.Length );
            }
            finally
            {
                Engine.InternalPacketReceivedEvent -= OnSent;
            }

            return sent;
        }

        [TestMethod]
        public void WornItemOnFriendIsSentRehuedAndOriginalKept()
        {
            byte[] packet = WornItem( FriendSerial, Layer.OneHanded, 0x0020 );

            byte[] sent = CheckWornItem( packet, out bool rehued );

            Assert.IsTrue( rehued );
            Assert.IsNotNull( sent );
            Assert.AreEqual( FriendHue, ( sent[13] << 8 ) | sent[14] );
            Assert.AreEqual( 0x0020, ( packet[13] << 8 ) | packet[14] );
        }

        [TestMethod]
        public void WornItemOnExcludedLayerIsLeftAlone()
        {
            Options.CurrentOptions.RehueFriendsExcludedLayers = [Layer.OneHanded];

            byte[] sent = CheckWornItem( WornItem( FriendSerial, Layer.OneHanded, 0x0020 ), out bool rehued );

            Assert.IsFalse( rehued );
            Assert.IsNull( sent );
        }

        [TestMethod]
        public void WornItemOnStrangerIsLeftAlone()
        {
            byte[] sent = CheckWornItem( WornItem( FriendSerial + 1, Layer.OneHanded, 0x0020 ), out bool rehued );

            Assert.IsFalse( rehued );
            Assert.IsNull( sent );
        }

        [TestMethod]
        public void WornItemWaitsOnlyForFriends()
        {
            PacketWaitRuleSet rules = PacketWaitRuleSet.Create( PacketWaitRegistry.BuildRules() );

            Assert.IsTrue( rules.MustWait( WornItem( FriendSerial, Layer.OneHanded, 0 ), false ) );
            Assert.IsFalse( rules.MustWait( WornItem( FriendSerial + 1, Layer.OneHanded, 0 ), false ) );

            Options.CurrentOptions.RehueFriends = false;

            rules = PacketWaitRuleSet.Create( PacketWaitRegistry.BuildRules() );

            Assert.IsFalse( rules.MustWait( WornItem( FriendSerial, Layer.OneHanded, 0 ), false ) );
        }

        [TestMethod]
        public void MobileIncomingSkipsExcludedLayersAndBody()
        {
            Mobile mobile = new Mobile( FriendSerial ) { ID = 0x190, Hue = 0x0010 };
            ItemCollection equipment = new ItemCollection( FriendSerial );
            equipment.Add( new Item( 0x40000001 ) { ID = 0x0F51, Layer = Layer.OneHanded, Hue = 0x0020 } );
            equipment.Add( new Item( 0x40000002 ) { ID = 0x3EA2, Layer = Layer.Mount, Hue = 0x0030 } );

            byte[] packet = new MobileIncoming( mobile, equipment, FriendHue, new HashSet<Layer> { Layer.Mount }, false )
                .ToArray();

            // Body hue at 15, then 9 bytes per item with the hue at item offset 7
            Assert.AreEqual( 0x0010, ( packet[15] << 8 ) | packet[16] );
            Assert.AreEqual( FriendHue, ( packet[26] << 8 ) | packet[27] );
            Assert.AreEqual( 0x0030, ( packet[35] << 8 ) | packet[36] );
        }
    }
}
