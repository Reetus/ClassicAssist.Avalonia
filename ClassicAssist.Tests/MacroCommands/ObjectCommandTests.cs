using System.Threading;
using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using ClassicAssist.UO.Objects;
using System;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class ObjectCommandTests : IDisposable
    {
        [Fact]
        public void WillFindTypeGround()
        {
            Engine.Items.Add( new Item( 0x40000001 ) { ID = 0xdda } );

            bool result = ObjectCommands.FindType( 0xdda );

            Assert.True( result );

            int val = AliasCommands.GetAlias( "found" );

            Assert.Equal( 0x40000001, val );

            Engine.Items.Clear();
        }

        [Fact]
        public void WillFindTypeGroundMobile()
        {
            Engine.Mobiles.Add( new Mobile( 0x02 ) { ID = 0x190 } );

            bool result = ObjectCommands.FindType( 0x190 );

            Assert.True( result );

            int val = AliasCommands.GetAlias( "found" );

            Assert.Equal( 0x02, val );

            Engine.Mobiles.Clear();
        }

        [Fact]
        public void WillFindTypeSubcontainer()
        {
            Item container = new Item( 1 )
            {
                Container = new ItemCollection( 1 ) { new Item( 0x02 ) { ID = 0xdda, Owner = 1 } }
            };

            Engine.Items.Add( container );

            AliasCommands.SetAlias( "cont", 1 );

            bool result = ObjectCommands.FindType( 0xdda, -1, "cont" );

            Assert.True( result );

            int val = AliasCommands.GetAlias( "found" );

            Assert.Equal( 0x02, val );

            Engine.Items.Clear();
        }

        [Fact]
        public void WillFindTypeInRange()
        {
            Engine.Player = new PlayerMobile( 1 );
            Engine.Mobiles.Add( new Mobile( 0x02 ) { ID = 0x190, X = 5, Y = 5 } );

            bool result = ObjectCommands.FindType( 0x190, 8 );

            Assert.True( result );

            Engine.Mobiles.Clear();
            Engine.Player = null;
        }

        [Fact]
        public void WontFindTypeOutOfRange()
        {
            Engine.Player = new PlayerMobile( 1 );
            Engine.Mobiles.Add( new Mobile( 0x02 ) { ID = 0x190, X = 5, Y = 5 } );

            bool result = ObjectCommands.FindType( 0x190, 1 );

            Assert.False( result );

            Engine.Mobiles.Clear();
            Engine.Player = null;
        }

        [Fact]
        public void WillIgnoreObject()
        {
            Engine.Mobiles.Add( new Mobile( 0x02 ) { ID = 0x190, X = 5, Y = 5 } );

            bool result = ObjectCommands.FindType( 0x190 );

            Assert.True( result );

            ObjectCommands.IgnoreObject( "found" );

            result = ObjectCommands.FindType( 0x190 );

            Assert.False( result );

            Engine.Mobiles.Clear();
            ObjectCommands.ClearIgnoreList();
        }

        [Fact]
        public void WillCountType()
        {
            PlayerMobile player = new PlayerMobile( 1 );
            Item backpack =
                new Item( 0x40000001 ) { ID = 0xff, Owner = 0x01, Container = new ItemCollection( 0x40000001 ) };

            Engine.Player = player;
            Engine.Items.Add( backpack );

            player.SetLayer( Layer.Backpack, 0x40000001 );

            backpack.Container.Add( new Item( 0x40000002 ) { ID = 0xFE, Owner = 0x40000001 } );

            int count = ObjectCommands.CountType( 0xfe, 0x40000001 );

            Assert.Equal( 1, count );

            Engine.Items.Clear();
            Engine.Player = null;
        }

        [Fact]
        public void WillCountMobileGround()
        {
            Engine.Player = new PlayerMobile( 0x01 );

            Mobile mobile = new Mobile( 0x02 ) { ID = 0x190 };
            Mobile mobile2 = new Mobile( 0x03 ) { ID = 0x191 };

            Engine.Mobiles.Add( mobile );
            Engine.Mobiles.Add( mobile2 );

            int count = ObjectCommands.CountTypeGround( 0x190, -1, 5 );

            Assert.Equal( 1, count );

            Engine.Mobiles.Clear();
            Engine.Player = null;
        }

        [Fact]
        public void WillSendUseObjectPacket()
        {
            AutoResetEvent are = new AutoResetEvent( false );

            void InternalPacketSentEvent( byte[] data, int length )
            {
                if ( data[0] != 0x06 )
                {
                    Assert.Fail();
                }

                int serial = ( data[1] << 24 ) | ( data[2] << 16 ) | ( data[3] << 8 ) | data[4];

                Assert.Equal( 0x40000001, serial );

                are.Set();
            }

            Engine.InternalPacketSentEvent += InternalPacketSentEvent;

            Engine.Items.Add( new Item( 0x40000001 ) );

            AliasCommands.SetAlias( "item", 0x40000001 );

            ObjectCommands.UseObject( "item" );

            bool result = are.WaitOne( 5000 );

            Assert.True( result );

            Engine.InternalPacketSentEvent -= InternalPacketSentEvent;
            Engine.Items.Clear();
            AliasCommands.UnsetAlias( "item" );
        }

        [Fact]
        public void WillFindObjectInContainer()
        {
            Item container = new Item( 0x55 ) { Container = new ItemCollection( 0x55 ) };

            Engine.Items.Add( container );

            container.Container.Add( new Item( 0x56 ) { Owner = 0x55 } );

            bool result = ObjectCommands.FindObject( 0x56, -1, 0x55 );

            Assert.True( result );

            Item subContainer = new Item( 0x57 ) { Container = new ItemCollection( 0x57 ), Owner = 0x55 };

            container.Container.Add( subContainer );
            subContainer.Container.Add( new Item( 0x58 ) { Owner = 0x57 } );

            result = ObjectCommands.FindObject( 0x57, -1, 0x55 );

            Assert.True( result );

            Engine.Items.Remove( container );
        }

        [Fact]
        public void WillFindObjectGround()
        {
            Engine.Player = new PlayerMobile( 0x01 ) { X = 0, Y = 0 };

            Item item1 = new Item( 0x40000001 );
            Item item2 = new Item( 0x40000002 ) { X = 100, Y = 100 };

            Engine.Items.Add( item1 );
            Engine.Items.Add( item2 );

            Assert.True( ObjectCommands.FindObject( item1.Serial ) );
            Assert.True( ObjectCommands.FindObject( item2.Serial ) );
            Assert.False( ObjectCommands.FindObject( item2.Serial, 5 ) );

            Engine.Items.Remove( item2 );
            Engine.Items.Remove( item1 );

            Engine.Player = null;
        }

        public void Dispose()
        {
            ActionPacketQueue.Clear();
        }
    }
}