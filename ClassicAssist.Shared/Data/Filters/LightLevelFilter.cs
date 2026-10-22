using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UI;
using ClassicAssist.Shared.UI.ViewModels.Filters;
using ClassicAssist.UO.Network.PacketFilter;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Data.Filters;

[FilterOptions( Name = "Light Level", DefaultEnabled = true )]
public class LightLevelFilter : DynamicFilterEntry, IConfigurableFilter
{
    public const int MAX_LIGHT_LEVEL = 31;

    private static int _serverLightLevel;

    static LightLevelFilter()
    {
        FilterOptions = new LightLevelOptions();
    }

    public LightLevelFilter()
    {
        Engine.ConnectedEvent += SendLightLevel;
    }

    public static LightLevelOptions FilterOptions
    {
        get;
        set
        {
            field?.PropertyChanged -= OnOptionsChanged;

            field = value;

            field?.PropertyChanged += OnOptionsChanged;

            SendLightLevel();
        }
    }

    public static bool IsEnabled { get; set; }

    public async Task Configure()
    {
        LightLevelFilterConfigureViewModel vm = new( FilterOptions );

        await Engine.UIInvoker.InvokeDialog( "LightLevelFilterConfigureWindow", dataContext: vm );
    }

    public void Deserialize( JToken token )
    {
        if ( token == null )
        {
            return;
        }

        LightLevelOptions options = new();

        if ( Enum.TryParse( token["Mode"]?.ToString(), out LightLevelMode mode ) )
        {
            options.Mode = mode;
        }

        options.FixedLevel = token["FixedLevel"]?.ToObject<int>() ?? options.FixedLevel;
        options.MinLevel = token["MinLevel"]?.ToObject<int>() ?? options.MinLevel;
        options.MaxLevel = token["MaxLevel"]?.ToObject<int>() ?? options.MaxLevel;

        FilterOptions = options;
    }

    public JObject Serialize()
    {
        return new JObject
        {
            { "Mode", FilterOptions.Mode.ToString() },
            { "FixedLevel", FilterOptions.FixedLevel },
            { "MinLevel", FilterOptions.MinLevel },
            { "MaxLevel", FilterOptions.MaxLevel }
        };
    }

    public void ResetOptions()
    {
        FilterOptions = new LightLevelOptions();
    }

    /// <summary>Profiles saved before the light level became a filter option stored it as General.LightLevel.</summary>
    public static void MigrateLegacyLightLevel( JToken token )
    {
        if ( token == null )
        {
            return;
        }

        FilterOptions = new LightLevelOptions { FixedLevel = token.ToObject<int>() };
    }

    /// <summary>
    ///     Called by <see cref="ClassicAssist.UO.Network.IncomingPacketFilters" /> for every 0x4F, enabled or
    ///     not - <see cref="CheckPacket" /> only runs while the filter is on, and the server's level is
    ///     needed to restore it when the filter is turned off.
    /// </summary>
    public static void SetServerLightLevel( int level )
    {
        _serverLightLevel = level;
    }

    protected override void OnChanged( bool enabled )
    {
        IsEnabled = enabled;

        if ( !Engine.Connected )
        {
            return;
        }

        // Restore the server light level when the filter is turned off
        byte level = (byte) ( enabled ? GetLightLevel( _serverLightLevel ) : _serverLightLevel );

        Engine.SendPacketToClient( [0x4F, level], 2 );
    }

    /// <summary>0x4E (personal light) is blocked and 0x4F (global light) rewritten.</summary>
    public override IEnumerable<PacketWaitRule> GetWaitRules()
    {
        return Enabled ? [new PacketWaitRule( 0x4E ), new PacketWaitRule( 0x4F )] : [];
    }

    public override bool CheckPacket( ref byte[] packet, ref int length, PacketDirection direction )
    {
        if ( !IsEnabled )
        {
            return false;
        }

        if ( packet[0] == 0x4E )
        {
            return true;
        }

        if ( packet[0] != 0x4F )
        {
            return false;
        }

        packet[1] = (byte) GetLightLevel( packet[1] );

        return false;
    }

    private static void OnOptionsChanged( object sender, PropertyChangedEventArgs e )
    {
        SendLightLevel();
    }

    private static void SendLightLevel()
    {
        if ( !IsEnabled || !Engine.Connected )
        {
            return;
        }

        Engine.SendPacketToClient( [0x4F, (byte) GetLightLevel( _serverLightLevel )], 2 );
    }

    private static int GetLightLevel( int serverLevel )
    {
        if ( FilterOptions.Mode == LightLevelMode.Fixed )
        {
            return FilterOptions.FixedLevel;
        }

        return Math.Min( Math.Max( serverLevel, FilterOptions.MinLevel ), FilterOptions.MaxLevel );
    }
}

public enum LightLevelMode
{
    Fixed,
    MinMax
}

public class LightLevelOptions : SetPropertyNotifyChanged
{
    public int FixedLevel
    {
        get;
        set => SetProperty( ref field, Clamp( value ) );
    } = LightLevelFilter.MAX_LIGHT_LEVEL;

    public bool IsFixed
    {
        get => Mode == LightLevelMode.Fixed;
        set
        {
            if ( value )
            {
                Mode = LightLevelMode.Fixed;
            }
        }
    }

    public bool IsMinMax
    {
        get => Mode == LightLevelMode.MinMax;
        set
        {
            if ( value )
            {
                Mode = LightLevelMode.MinMax;
            }
        }
    }

    /// <summary>Darkest allowed level; pushes <see cref="MinLevel" /> along rather than crossing it.</summary>
    public int MaxLevel
    {
        get;
        set
        {
            SetProperty( ref field, Clamp( value ) );

            if ( MinLevel > field )
            {
                MinLevel = field;
            }
        }
    } = LightLevelFilter.MAX_LIGHT_LEVEL;

    /// <summary>Brightest allowed level; pushes <see cref="MaxLevel" /> along rather than crossing it.</summary>
    public int MinLevel
    {
        get;
        set
        {
            SetProperty( ref field, Clamp( value ) );

            if ( MaxLevel < field )
            {
                MaxLevel = field;
            }
        }
    }

    public LightLevelMode Mode
    {
        get;
        set
        {
            SetProperty( ref field, value );
            OnPropertyChanged( nameof( IsFixed ) );
            OnPropertyChanged( nameof( IsMinMax ) );
        }
    } = LightLevelMode.Fixed;

    private static int Clamp( int value )
    {
        return Math.Min( Math.Max( value, 0 ), LightLevelFilter.MAX_LIGHT_LEVEL );
    }
}
