using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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
    // LockedObservableCollection rather than the plain one: this list is mutated from the configure
    // dialog on the UI thread while CheckMessage/FindByCliloc enumerate it on the packet thread.
    public static ObservableCollection<FilterClilocEntry> Filters { get; set; } =
        new LockedObservableCollection<FilterClilocEntry>();

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

        // Snapshot: profile saves run this on RPC threads while the UI edits the list.
        foreach ( FilterClilocEntry filter in Filters.ToArray() )
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
        // LINQ enumerates through IEnumerable<T>, which LockedObservableCollection maps to a
        // snapshot - this runs on the packet thread while the UI edits the list.
        return Filters.FirstOrDefault( f => f.Cliloc == cliloc );
    }
}

/// <summary>
///     An <see cref="ObservableCollection{T}" /> that can be edited on one thread while another
///     enumerates it. The filter lists are mutated from the UI (the configure dialogs) but read
///     from the packet threads, where a racing enumeration used to throw
///     <see cref="InvalidOperationException" /> and cost the packet being processed.
///     <para>
///         Callers keep using the ordinary <see cref="ObservableCollection{T}" /> surface: every
///         mutator (<c>Add</c>, <c>Remove</c>, <c>Clear</c>, the indexer) funnels through the
///         protected <c>InsertItem</c>/<c>RemoveItem</c>/<c>SetItem</c>/<c>ClearItems</c>/
///         <c>MoveItem</c> hooks, which are serialised on a private lock below.
///     </para>
///     <para>
///         Enumeration is snapshot-based and cannot observe a concurrent edit.
///         <see cref="IEnumerable{T}" /> and <see cref="ICollection{T}" /> are re-implemented so
///         that interface-dispatched enumeration - <c>foreach</c> over the interface, LINQ and
///         <c>ToArray()</c>, the only way the callers outside this assembly reach these lists from
///         other threads - gets a private copy. The base's <c>GetEnumerator</c>/<c>CopyTo</c> are
///         sealed interface implementations and cannot be overridden, so re-implementation is the
///         only hook; note that a plain <c>foreach</c> over an
///         <see cref="ObservableCollection{T}" />-typed reference binds to the base enumerator
///         directly and is therefore iterated as <c>...ToArray()</c> or through LINQ instead.
///     </para>
/// </summary>
internal sealed class LockedObservableCollection<T> : ObservableCollection<T>, IEnumerable<T>, ICollection<T>
{
    private readonly Lock _lock = new();

    protected override void ClearItems()
    {
        lock ( _lock )
        {
            base.ClearItems();
        }
    }

    protected override void InsertItem( int index, T item )
    {
        lock ( _lock )
        {
            base.InsertItem( index, item );
        }
    }

    protected override void MoveItem( int oldIndex, int newIndex )
    {
        lock ( _lock )
        {
            base.MoveItem( oldIndex, newIndex );
        }
    }

    protected override void RemoveItem( int index )
    {
        lock ( _lock )
        {
            base.RemoveItem( index );
        }
    }

    protected override void SetItem( int index, T item )
    {
        lock ( _lock )
        {
            base.SetItem( index, item );
        }
    }

    void ICollection<T>.CopyTo( T[] array, int index )
    {
        lock ( _lock )
        {
            // ToArray() sizes its buffer from Count in a separate call, so the collection can grow
            // before the copy runs; copy what fits instead of throwing like List<T>.CopyTo would,
            // restarting the very exception this type exists to stop.
            int count = Math.Min( Items.Count, array.Length - index );

            for ( int i = 0; i < count; i++ )
            {
                array[index + i] = Items[i];
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ( (IEnumerable<T>) this ).GetEnumerator();
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        lock ( _lock )
        {
            return new List<T>( Items ).GetEnumerator();
        }
    }
}
