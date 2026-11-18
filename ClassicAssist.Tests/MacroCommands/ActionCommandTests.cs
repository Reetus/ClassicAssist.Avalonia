using System;
using System.Text;
using System.Threading;
using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class ActionCommandTests : IDisposable
    {
        public ActionCommandTests()
        {
            Engine.ClientVersion = new Version( 7, 0, 45, 1 );
        }

        [Fact]
        public void AttackWillSendAttackPacket()
        {
            AutoResetEvent are = new AutoResetEvent( false );

            void OnInternalPacketSentEvent( byte[] data, int length )
            {
                if ( data[0] != 0x05 )
                {
                    return;
                }

                int serial = ( data[1] << 24 ) | ( data[2] << 16 ) | ( data[3] << 8 ) | data[4];

                Assert.Equal( 0x00aabbcc, serial );

                are.Set();
            }

            Engine.InternalPacketSentEvent += OnInternalPacketSentEvent;

            ActionCommands.Attack( 0x00aabbcc );

            bool result = are.WaitOne( 5000 );

            Assert.True( result );

            Engine.InternalPacketSentEvent -= OnInternalPacketSentEvent;
        }

        [Fact]
        public void ClearHandsWillSendDragPacket()
        {
            AutoResetEvent are = new AutoResetEvent( false );

            void OnInternalPacketSentEvent( byte[] data, int length )
            {
                if ( data[0] != 0x07 )
                {
                    return;
                }

                int serial = ( data[1] << 24 ) | ( data[2] << 16 ) | ( data[3] << 8 ) | data[4];

                Assert.Equal( 0x00aabbcc, serial );

                are.Set();
            }

            Engine.InternalPacketSentEvent += OnInternalPacketSentEvent;

            Engine.Player = new PlayerMobile( 0x01 );
            Engine.Player.SetLayer( Layer.OneHanded, 0x00aabbcc );

            ActionCommands.ClearHands( "left" );

            bool result = are.WaitOne( 5000 );

            Assert.True( result );

            Engine.InternalPacketSentEvent -= OnInternalPacketSentEvent;
            Engine.Player = null;
        }

        [Fact]
        public void ClickObjectWillSendPacket()
        {
            AutoResetEvent are = new AutoResetEvent( false );

            void OnInternalPacketSentEvent( byte[] data, int length )
            {
                if ( data[0] != 0x09 )
                {
                    return;
                }

                int serial = ( data[1] << 24 ) | ( data[2] << 16 ) | ( data[3] << 8 ) | data[4];

                Assert.Equal( 0x00aabbcc, serial );

                are.Set();
            }

            Engine.InternalPacketSentEvent += OnInternalPacketSentEvent;

            AliasCommands.SetAlias( "thing", 0x00aabbcc );
            ActionCommands.ClickObject( "thing" );

            bool result = are.WaitOne( 5000 );

            Assert.True( result );

            Engine.InternalPacketSentEvent -= OnInternalPacketSentEvent;
            Engine.Player = null;
        }

        [Fact]
        public void MoveItemWillSendDragPacket()
        {
            AutoResetEvent are = new AutoResetEvent( false );

            void OnInternalPacketSentEvent( byte[] data, int length )
            {
                if ( data[0] != 0x07 )
                {
                    return;
                }

                int serial = ( data[1] << 24 ) | ( data[2] << 16 ) | ( data[3] << 8 ) | data[4];

                Assert.Equal( 0x00aabbcc, serial );

                are.Set();
            }

            Engine.InternalPacketSentEvent += OnInternalPacketSentEvent;

            Engine.Items.Add( new Item( 0x00aabbcc ) { Count = 50 } );

            ObjectCommands.MoveItem( 0x00aabbcc, 0xaabbdd );

            bool result = are.WaitOne( 5000 );

            Assert.True( result );

            Engine.InternalPacketSentEvent -= OnInternalPacketSentEvent;
        }

        [Fact]
        public void FeedWillSendDragPacket()
        {
            PlayerMobile player = new PlayerMobile( 0x01 );
            Item backpack = new Item( 0x02 )
            {
                Container = new ItemCollection( 0x02 ) { new Item( 0x00aabbcc ) { ID = 0xff, Count = 1 } },
                Owner = 0x01,
                Layer = Layer.Backpack
            };

            player.SetLayer( Layer.Backpack, 0x02 );

            Engine.Player = player;
            Engine.Items.Add( backpack );

            AutoResetEvent are = new AutoResetEvent( false );

            void OnInternalPacketSentEvent( byte[] data, int length )
            {
                if ( data[0] != 0x07 )
                {
                    return;
                }

                int serial = ( data[1] << 24 ) | ( data[2] << 16 ) | ( data[3] << 8 ) | data[4];

                Assert.Equal( 0x00aabbcc, serial );

                are.Set();
            }

            Engine.InternalPacketSentEvent += OnInternalPacketSentEvent;

            ActionCommands.Feed( 0x01, 0xff );

            bool result = are.WaitOne( 5000 );

            Assert.True( result );

            Engine.InternalPacketSentEvent -= OnInternalPacketSentEvent;

            Engine.Items.Clear();
            Engine.Player = null;
        }

        [Fact]
        public void RenameWillSendRenamePacket()
        {
            AutoResetEvent are = new AutoResetEvent( false );

            void OnInternalPacketSentEvent( byte[] data, int length )
            {
                int serial = ( data[1] << 24 ) | ( data[2] << 16 ) | ( data[3] << 8 ) | data[4];

                Assert.Equal( 0x00aabbcc, serial );

                string name = Encoding.ASCII.GetString( data, 5, 30 ).TrimEnd( '\0' );

                Assert.Equal( "Snorlax", name );

                are.Set();
            }

            Engine.InternalPacketSentEvent += OnInternalPacketSentEvent;

            ActionCommands.Rename( 0x00aabbcc, "Snorlax" );

            bool result = are.WaitOne( 5000 );

            Assert.True( result );

            Engine.InternalPacketSentEvent -= OnInternalPacketSentEvent;
        }

        [Fact]
        public void ShowNamesWillSendLookRequest()
        {
            PlayerMobile player = new PlayerMobile( 0x01 );
            Mobile mobile = new Mobile( 0x00aabbcc );

            // ShowNames looks at every known mobile/corpse, so start from empty collections rather than
            // whatever an earlier test left behind.
            Engine.Mobiles.Clear();
            Engine.Items.Clear();

            Engine.Player = player;
            Engine.Mobiles.Add( mobile );

            AutoResetEvent are = new AutoResetEvent( false );

            void OnInternalPacketSentEvent( byte[] data, int length )
            {
                Assert.Equal( 0x09, data[0] );

                int serial = ( data[1] << 24 ) | ( data[2] << 16 ) | ( data[3] << 8 ) | data[4];

                Assert.Equal( 0x00aabbcc, serial );

                are.Set();
            }

            Engine.InternalPacketSentEvent += OnInternalPacketSentEvent;

            try
            {
                ActionCommands.ShowNames( "mobiles" );

                bool result = are.WaitOne( 5000 );

                Assert.True( result );

                Engine.Mobiles.Remove( mobile );

                Engine.Items.Add( new Item( 0x00aabbcc ) { ID = 0x2006 } );

                ActionCommands.ShowNames( "corpses" );

                result = are.WaitOne( 5000 );

                Assert.True( result );
            }
            finally
            {
                // The handler asserts on every packet sent, so leaving it attached after a failure
                // would fail every later test that sends anything.
                Engine.InternalPacketSentEvent -= OnInternalPacketSentEvent;
                Engine.Player = null;
                Engine.Mobiles.Clear();
                Engine.Items.Clear();
            }
        }

        [Fact]
        public void EquipItemWillSendDragPacket()
        {
            AutoResetEvent are = new AutoResetEvent( false );

            void OnInternalPacketSentEvent( byte[] data, int length )
            {
                if ( data[0] != 0x07 )
                {
                    return;
                }

                int serial = ( data[1] << 24 ) | ( data[2] << 16 ) | ( data[3] << 8 ) | data[4];

                Assert.Equal( 0x00aabbcc, serial );

                are.Set();
            }

            Engine.InternalPacketSentEvent += OnInternalPacketSentEvent;

            Engine.Items.Add( new Item( 0x00aabbcc ) );

            Engine.Player = new PlayerMobile( 0x01 );

            ActionCommands.EquipItem( 0x00aabbcc, Layer.OneHanded );

            bool result = are.WaitOne( 5000 );

            Assert.True( result );

            Engine.InternalPacketSentEvent -= OnInternalPacketSentEvent;
            Engine.Player = null;
            Engine.Items.Clear();
        }

        [Fact]
        public void EquipTypeWillSendDragPacket()
        {
            Item backpack = new Item( 0x40000000 ) { Owner = 1, Container = new ItemCollection( 0x40000000 ) };

            Engine.Player = new PlayerMobile( 1 ) { Equipment = { backpack } };
            Engine.Player.SetLayer( Layer.Backpack, backpack.Serial );
            Engine.Items.Add( backpack );

            Item item = new Item( 0x40000001, backpack.Serial ) { ID = 0xff };
            backpack.Container.Add( item );

            AutoResetEvent are = new AutoResetEvent( false );

            void OnInternalPacketSentEvent( byte[] data, int length )
            {
                if ( data[0] != 0x07 )
                {
                    return;
                }

                int serial = ( data[1] << 24 ) | ( data[2] << 16 ) | ( data[3] << 8 ) | data[4];

                Assert.Equal( item.Serial, serial );

                are.Set();
            }

            Engine.InternalPacketSentEvent += OnInternalPacketSentEvent;

            ActionCommands.EquipType( item.ID, Layer.OneHanded );

            bool result = are.WaitOne( 5000 );

            Assert.True( result );

            Engine.InternalPacketSentEvent -= OnInternalPacketSentEvent;
            Engine.Player = null;
            Engine.Items.Clear();
        }

        [Fact]
        public void WillFindLayer()
        {
            Engine.Player = new PlayerMobile( 1 );
            Engine.Player.SetLayer( Layer.OneHanded, 0x40000001 );
            AliasCommands.SetAlias( "self", Engine.Player.Serial );

            Assert.True( ActionCommands.FindLayer( "OneHanded" ) );
            Assert.False( ActionCommands.FindLayer( "TwoHanded" ) );

            AliasCommands.UnsetAlias( "self" );
            Engine.Player = null;
        }

        public void Dispose()
        {
            ActionPacketQueue.Clear();
        }
    }
}