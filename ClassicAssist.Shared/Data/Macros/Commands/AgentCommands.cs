using System.Linq;
using ClassicAssist.Shared;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Data.Counters;
using ClassicAssist.Data.Dress;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;
using UOC = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.Data.Macros.Commands
{
    public static class AgentCommands
    {
        [CommandsDisplay( Category = nameof( Strings.Agents ),
            Parameters = new[] { nameof( ParameterType.AgentEntryName ) } )]
        public static void Dress( string name = null )
        {
            DressManager manager = DressManager.GetInstance();

            DressAgentEntry dressAgentEntry;

            if ( string.IsNullOrEmpty( name ) )
            {
                if ( manager.TemporaryDress == null )
                {
                    UOC.SystemMessage( Strings.No_temporary_dress_layout_configured___ );
                    return;
                }

                dressAgentEntry = manager.TemporaryDress;
            }
            else
            {
                dressAgentEntry = manager.Items.FirstOrDefault( dae => dae.Name == name );
            }

            if ( dressAgentEntry == null )
            {
                UOC.SystemMessage( string.Format( Strings.Unknown_dress_agent___0___, name ) );
                return;
            }

            dressAgentEntry.Action( dressAgentEntry );
        }

        [CommandsDisplay( Category = nameof( Strings.Agents ),
            Parameters = new[] { nameof( ParameterType.AgentEntryName ) } )]
        public static void Undress( string name )
        {
            DressManager manager = DressManager.GetInstance();

            DressAgentEntry dressAgentEntry = manager.Items.FirstOrDefault( dae => dae.Name == name );

            if ( dressAgentEntry == null )
            {
                UOC.SystemMessage( string.Format( Strings.Unknown_dress_agent___0___, name ) );
                return;
            }

            dressAgentEntry.Undress().Wait();
        }

        [CommandsDisplay( Category = nameof( Strings.Agents ) )]
        public static bool Dressing()
        {
            DressManager manager = DressManager.GetInstance();

            return manager.IsDressing;
        }

        [CommandsDisplay( Category = nameof( Strings.Agents ) )]
        public static void DressConfig()
        {
            DressManager manager = DressManager.GetInstance();
            manager.TemporaryDress = new DressAgentEntry();
            manager.TemporaryDress.Action = async hks => await manager.DressAllItems( manager.TemporaryDress, false );
            manager.ImportItems( manager.TemporaryDress );
        }

        [CommandsDisplay( Category = nameof( Strings.Agents ),
            Parameters = new[] { nameof( ParameterType.AgentEntryName ) } )]
        public static int Counter( string name )
        {
            CountersManager manager = CountersManager.GetInstance();

            CountersAgentEntry entry = manager.Items.FirstOrDefault( cae => cae.Name.ToLower() == name.ToLower() );

            if ( entry != null )
            {
                return entry.Count;
            }

            UOC.SystemMessage( Strings.Invalid_counter_agent_name___ );
            return 0;
        }

        [CommandsDisplay( Category = nameof( Strings.Agents ),
            Parameters = new[] { nameof( ParameterType.SerialOrAlias ) } )]
        public static void SetAutolootContainer( object obj )
        {
            int serial = AliasCommands.ResolveSerial( obj );

            AutolootHelpers.SetAutolootContainer?.Invoke( serial );
        }
        [CommandsDisplay( Category = nameof( Strings.Trade ) )]
        public static void TradeAccept()
        {
            PacketWriter writer = new PacketWriter( 12 );
            writer.Write( (byte) 0x6F );
            writer.Write( (short) 12 );
            writer.Write( (byte) TradeAction.Update );
            writer.Write( Engine.Trade.Serial );
            writer.Write( 1 );
            Engine.SendPacketToServer( writer );
        }

        [CommandsDisplay( Category = nameof( Strings.Trade ) )]
        public static void TradeReject()
        {
            PacketWriter writer = new PacketWriter( 12 );
            writer.Write( (byte) 0x6F );
            writer.Write( (short) 12 );
            writer.Write( (byte) TradeAction.Update );
            writer.Write( Engine.Trade.Serial );
            writer.Write( 0 );
            Engine.SendPacketToServer( writer );
        }

        [CommandsDisplay( Category = nameof( Strings.Trade ) )]
        public static void TradeClose()
        {
            PacketWriter writer = new PacketWriter( 12 );
            writer.Write( (byte) 0x6F );
            writer.Write( (short) 8 );
            writer.Write( (byte) TradeAction.Cancel );
            writer.Write( Engine.Trade.Serial );
            Engine.SendPacketToServer( writer );
        }

        [CommandsDisplay( Category = nameof( Strings.Trade ) )]
        public static void TradeCurrency( int gold, int platinum = 0 )
        {
            PacketWriter writer = new PacketWriter( 12 );
            writer.Write( (byte) 0x6F );
            writer.Write( (short) 16 );
            writer.Write( (byte) TradeAction.Gold );
            writer.Write( Engine.Trade.ContainerLocal );
            writer.Write( gold );
            writer.Write( platinum );
            Engine.SendPacketToServer( writer );
        }
    }
}
