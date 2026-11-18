using System.Collections.Generic;
using ClassicAssist.Data.Skills;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using Xunit;

namespace ClassicAssist.Tests.Network.Incoming
{
    /// <summary>
    ///     0x3A skills: the full list (type 0x00 without caps, 0x02 with caps; skill ids 1-based and the list
    ///     ends with id 0) and the single-skill update (type 0xFF without cap, 0xDF with; id 0-based). The
    ///     skills tab, the skill gain journal and every SkillCap / Skill macro command read these, and getting
    ///     the id base or the cap field wrong moves every value onto the neighbouring skill.
    /// </summary>
    public class SkillPacketTests : IncomingPacketTestBase
    {
        [Fact]
        public void WillListSkillsWithoutCapsOnFullList()
        {
            SkillInfo[] skills = ReceiveList( 0x00, false, ( 1, 1000, 950, LockStatus.Up, 0 ),
                ( 26, 805, 800, LockStatus.Locked, 0 ) );

            Assert.Equal( 2, skills.Length );

            Assert.Equal( 0, skills[0].ID );
            Assert.Equal( 100.0f, skills[0].Value );
            Assert.Equal( 95.0f, skills[0].BaseValue );
            Assert.Equal( LockStatus.Up, skills[0].LockStatus );
            Assert.Equal( 100.0f, skills[0].SkillCap );

            Assert.Equal( 25, skills[1].ID );
            Assert.Equal( 80.5f, skills[1].Value );
            Assert.Equal( 80.0f, skills[1].BaseValue );
            Assert.Equal( LockStatus.Locked, skills[1].LockStatus );
        }

        [Fact]
        public void WillReadCapsOnFullListWithCaps()
        {
            SkillInfo[] skills = ReceiveList( 0x02, true, ( 1, 1000, 950, LockStatus.Up, 1200 ),
                ( 2, 500, 500, LockStatus.Down, 1100 ), ( 58, 0, 0, LockStatus.Up, 1000 ) );

            Assert.Equal( 3, skills.Length );
            Assert.Equal( 120.0f, skills[0].SkillCap );
            Assert.Equal( 1, skills[1].ID );
            Assert.Equal( LockStatus.Down, skills[1].LockStatus );
            Assert.Equal( 110.0f, skills[1].SkillCap );
            Assert.Equal( 57, skills[2].ID );
        }

        [Fact]
        public void WillRaiseSkillUpdateWithDefaultCapOnSingleUpdate()
        {
            List<(int ID, float Value, float Base, LockStatus Lock, float Cap)> raised = ReceiveUpdate( 0xFF, false,
                31, 653, 650, LockStatus.Down, 0 );

            Assert.Equal( ( 31, 65.3f, 65.0f, LockStatus.Down, 100.0f ), Assert.Single( raised ) );
        }

        [Fact]
        public void WillRaiseSkillUpdateWithCapOnSingleUpdateWithCap()
        {
            List<(int ID, float Value, float Base, LockStatus Lock, float Cap)> raised = ReceiveUpdate( 0xDF, true,
                0, 1201, 1200, LockStatus.Locked, 1200 );

            Assert.Equal( ( 0, 120.1f, 120.0f, LockStatus.Locked, 120.0f ), Assert.Single( raised ) );
        }

        private static SkillInfo[] ReceiveList( byte type, bool withCaps,
            params (int ID, int Value, int Base, LockStatus Lock, int Cap)[] skills )
        {
            List<SkillInfo[]> raised = new List<SkillInfo[]>();

            void OnList( SkillInfo[] list )
            {
                raised.Add( list );
            }

            IncomingPacketHandlers.SkillsListEvent += OnList;

            try
            {
                Receive( Dynamic( 0x3A, w =>
                {
                    w.Write( type );

                    foreach ( (int id, int value, int baseValue, LockStatus lockStatus, int cap) in skills )
                    {
                        w.Write( (short) id );
                        w.Write( (short) value );
                        w.Write( (short) baseValue );
                        w.Write( (byte) lockStatus );

                        if ( withCaps )
                        {
                            w.Write( (short) cap );
                        }
                    }

                    w.Write( (short) 0 );
                } ) );
            }
            finally
            {
                IncomingPacketHandlers.SkillsListEvent -= OnList;
            }

            return Assert.Single( raised );
        }

        private static List<(int, float, float, LockStatus, float)> ReceiveUpdate( byte type, bool withCap, int id,
            int value, int baseValue, LockStatus lockStatus, int cap )
        {
            List<(int, float, float, LockStatus, float)> raised = new List<(int, float, float, LockStatus, float)>();

            void OnUpdate( int skill, float skillValue, float skillBase, LockStatus skillLock, float skillCap )
            {
                raised.Add( ( skill, skillValue, skillBase, skillLock, skillCap ) );
            }

            IncomingPacketHandlers.SkillUpdatedEvent += OnUpdate;

            try
            {
                Receive( Dynamic( 0x3A, w =>
                {
                    w.Write( type );
                    w.Write( (short) id );
                    w.Write( (short) value );
                    w.Write( (short) baseValue );
                    w.Write( (byte) lockStatus );

                    if ( withCap )
                    {
                        w.Write( (short) cap );
                    }
                } ) );
            }
            finally
            {
                IncomingPacketHandlers.SkillUpdatedEvent -= OnUpdate;
            }

            return raised;
        }
    }
}
