using ClassicAssist.Shared;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Skills;
using ClassicAssist.UO.Data;
using System;
using Xunit;

namespace ClassicAssist.Tests.MacroCommands
{
    public class SkillCommandsTests : IDisposable
    {
        public void Dispose()
        {
            Engine.Journal.Clear();
        }

        private static SkillEntry AddSkill( string name, float value, float @base, float cap, double delta,
            LockStatus lockStatus = LockStatus.Up )
        {
            SkillEntry entry = new SkillEntry
            {
                Skill = new Skill { Name = name },
                Value = value,
                Base = @base,
                Cap = cap,
                Delta = delta,
                LockStatus = lockStatus
            };

            SkillManager.GetInstance().Items.Add( entry );

            return entry;
        }

        [Fact]
        public void SkillWillReturnValueForMatchingSkill()
        {
            SkillEntry entry = AddSkill( "Anatomy", 85.5f, 80.0f, 100.0f, 5.5 );

            Assert.Equal( 85.5, SkillCommands.Skill( "anatomy" ), 0.001 );
            Assert.Equal( 80.0, SkillCommands.Skill( "anatomy", true ), 0.001 );

            SkillManager.GetInstance().Items.Remove( entry );
        }

        [Fact]
        public void SkillWillReturnZeroForUnknownSkill()
        {
            Assert.Equal( 0, SkillCommands.Skill( "ThisSkillDoesNotExist" ) );
        }

        [Fact]
        public void SkillDeltaWillReturnDelta()
        {
            SkillEntry entry = AddSkill( "Tactics", 90, 90, 100, 3.2 );

            Assert.Equal( 3.2, SkillCommands.SkillDelta( "tactics" ), 0.001 );

            SkillManager.GetInstance().Items.Remove( entry );
        }

        [Fact]
        public void SkillCapWillReturnCap()
        {
            SkillEntry entry = AddSkill( "Healing", 50, 50, 120, 0 );

            Assert.Equal( 120, SkillCommands.SkillCap( "healing" ), 0.001 );

            SkillManager.GetInstance().Items.Remove( entry );
        }

        [Fact]
        public void SkillStateWillReturnLockStatus()
        {
            SkillEntry entry = AddSkill( "Magery", 90, 90, 100, 0, LockStatus.Locked );

            Assert.Equal( "locked", SkillCommands.SkillState( "magery" ) );

            SkillManager.GetInstance().Items.Remove( entry );
        }

        [Fact]
        public void SkillStateWillDefaultToUpForUnknownSkill()
        {
            Assert.Equal( "up", SkillCommands.SkillState( "ThisSkillDoesNotExist" ) );
        }

        [Fact]
        public void SetSkillWillSendChangeSkillLockPacket()
        {
            SkillEntry entry = AddSkill( "Musicianship", 40, 40, 100, 0 );
            entry.Skill = new Skill { Name = "Musicianship", ID = 15 };

            byte[] sent = null;

            void OnSent( byte[] data, int length )
            {
                if ( data[0] == 0x3A )
                {
                    sent = data;
                }
            }

            Engine.InternalPacketSentEvent += OnSent;

            SkillCommands.SetSkill( "musicianship", "down" );

            Engine.InternalPacketSentEvent -= OnSent;

            Assert.NotNull( sent );
            Assert.Equal( 15, sent[4] );
            Assert.Equal( (byte) LockStatus.Down, sent[5] );

            SkillManager.GetInstance().Items.Remove( entry );
        }

        [Fact]
        public void SetSkillWillMessageForUnknownSkill()
        {
            SkillCommands.SetSkill( "ThisSkillDoesNotExist", "up" );

            Assert.True( JournalCommands.InJournal( "Invalid skill name", "system" ) );
        }

        [Fact]
        public void SetStatusWillSendChangeStatLockPacket()
        {
            byte[] sent = null;

            void OnSent( byte[] data, int length )
            {
                if ( data[0] == 0xBF && data[4] == 0x1A )
                {
                    sent = data;
                }
            }

            Engine.InternalPacketSentEvent += OnSent;

            SkillCommands.SetStatus( "str", "locked" );

            Engine.InternalPacketSentEvent -= OnSent;

            Assert.NotNull( sent );
            Assert.Equal( (byte) StatType.Str, sent[5] );
            Assert.Equal( (byte) LockStatus.Locked, sent[6] );
        }

        [Fact]
        public void UseLastSkillWillSendUseSkillPacketForLastSkillID()
        {
            Engine.LastSkillID = 33;

            byte[] sent = null;

            void OnSent( byte[] data, int length )
            {
                if ( data[0] == 0x12 && data[3] == 0x24 )
                {
                    sent = data;
                }
            }

            Engine.InternalPacketSentEvent += OnSent;

            SkillCommands.UseLastSkill();

            Engine.InternalPacketSentEvent -= OnSent;

            Assert.NotNull( sent );

            string args = System.Text.Encoding.ASCII.GetString( sent, 4, sent.Length - 5 );

            Assert.Equal( "33 0", args );
        }
    }
}
