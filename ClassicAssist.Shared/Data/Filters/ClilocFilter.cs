using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UI.ViewModels.Filters;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Network.Packets;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Data.Filters;

[FilterOptions( Name = "Cliloc Filter", DefaultEnabled = false )]
public class ClilocFilter : FilterEntry, IConfigurableFilter
{
    public static ObservableCollection<FilterClilocEntry> Filters { get; set; } =
        [];

    public static bool IsEnabled { get; set; }

    public async Task Configure()
    {
        ClilocFilterConfigureViewModel vm = new();

        await Engine.UIInvoker.InvokeDialog( "ClilocFilterConfigureWindow", dataContext: vm );
    }

    public void Deserialize( JToken token )
    {
        if ( token?["Filters"] == null )
        {
            return;
        }

        foreach ( JToken filterToken in token["Filters"] )
        {
            FilterClilocEntry entry = new()
            {
                Cliloc = filterToken["Key"]?.ToObject<int>() ?? -1,
                Replacement = filterToken["Value"]?.ToObject<string>(),
                Hue = filterToken["Hue"]?.ToObject<int>() ?? -1,
                ShowOverhead = filterToken["ShowOverhead"]?.ToObject<bool>() ?? false
            };

            if ( Filters.All( e => e.Cliloc != entry.Cliloc ) )
            {
                Filters.Add( entry );
            }
        }
    }

    public JObject Serialize()
    {
        JArray itemsArray = [];

        foreach ( FilterClilocEntry filter in Filters )
        {
            itemsArray.Add( new JObject
            {
                { "Key", filter.Cliloc },
                { "Value", filter.Replacement },
                { "Hue", filter.Hue },
                { "ShowOverhead", filter.ShowOverhead }
            } );
        }

        return new JObject { { "Filters", itemsArray } };
    }

    public void ResetOptions()
    {
        Filters.Clear();
    }

    protected override void OnChanged( bool enabled )
    {
        IsEnabled = enabled;
    }

    public static bool CheckMessage( JournalEntry journalEntry )
    {
        if ( !IsEnabled || journalEntry.Cliloc == 0 )
        {
            return false;
        }

        FilterClilocEntry match = FindByCliloc( journalEntry.Cliloc );

        if ( match == null )
        {
            return false;
        }

        int serial = journalEntry.Serial;
        int id = journalEntry.ID;

        // A system message has no source mobile; to show it overhead it has to be re-attributed to
        // the player, otherwise the client has nothing to draw it above.
        if ( serial == -1 && match.ShowOverhead )
        {
            serial = Engine.Player.Serial;
            id = Engine.Player.ID;
        }

        Engine.SendPacketToClient( new UnicodeText( serial, id, journalEntry.SpeechType,
            match.Hue == -1 ? journalEntry.SpeechHue : match.Hue, journalEntry.SpeechFont, Strings.UO_LOCALE,
            journalEntry.Name, match.Replacement ) );

        return true;
    }

    public static bool CheckMessageAffix( JournalEntry journalEntry, MessageAffixType affixType, string affix )
    {
        if ( !IsEnabled || journalEntry.Cliloc == 0 )
        {
            return false;
        }

        FilterClilocEntry match = FindByCliloc( journalEntry.Cliloc );

        if ( match == null )
        {
            return false;
        }

        string text = affixType.HasFlag( MessageAffixType.Prepend )
            ? $"{affix}{match.Replacement}"
            : $"{match.Replacement}{affix}";

        Engine.SendPacketToClient( new UnicodeText( journalEntry.Serial, journalEntry.ID, JournalSpeech.Say,
            match.Hue == -1 ? journalEntry.SpeechHue : match.Hue, journalEntry.SpeechFont, Strings.UO_LOCALE,
            journalEntry.Name, text ) );

        return true;
    }

    private static FilterClilocEntry FindByCliloc( int cliloc )
    {
        for ( int i = 0; i < Filters.Count; i++ )
        {
            if ( Filters[i].Cliloc == cliloc )
            {
                return Filters[i];
            }
        }

        return null;
    }
}
