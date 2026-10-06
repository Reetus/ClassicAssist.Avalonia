using System.Collections.Generic;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Shared;
using ClassicAssist.UO.Network.PacketFilter;

namespace ClassicAssist.Data.Filters;

[FilterOptions( Name = "Light Level", DefaultEnabled = true )]
public class LightLevelFilter : DynamicFilterEntry
{
    public LightLevelFilter()
    {
        Options.LightLevelChanged += level =>
        {
            if ( Engine.Connected )
            {
                SendLightLevel( level );
            }
        };

        Engine.ConnectedEvent += () => SendLightLevel( Options.CurrentOptions.LightLevel );
    }

    /// <summary>0x4E (personal light) is blocked and 0x4F (global light) rewritten.</summary>
    public override IEnumerable<PacketWaitRule> GetWaitRules()
    {
        return Enabled ? [new PacketWaitRule( 0x4E ), new PacketWaitRule( 0x4F )] : [];
    }

    public override bool CheckPacket( ref byte[] packet, ref int length, PacketDirection direction )
    {
        if ( packet[0] == 0x4E && Enabled )
        {
            return true;
        }

        if ( packet[0] != 0x4F || !Enabled )
        {
            return false;
        }

        packet[1] = (byte) Options.CurrentOptions.LightLevel;

        return false;
    }

    private void SendLightLevel( int level )
    {
        if ( Enabled )
        {
            Engine.SendPacketToClient( [0x4F, (byte) level], 2 );
        }
    }
}
