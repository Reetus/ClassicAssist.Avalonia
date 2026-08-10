using System;
using System.Collections.ObjectModel;
using System.Linq;
using ClassicAssist.Misc;
using ClassicAssist.Shared.UI;
using ClassicAssist.UI.Models;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Data.Misc;

/// <summary>
///     Per-window Entity Collection Viewer settings, persisted to
///     <c>EntityCollectionViewerOptions.json</c> in the same shape base ClassicAssist uses (see
///     <see cref="Serialize" />/<see cref="Deserialize" />) - a file written by either side loads
///     cleanly in the other, aside from <c>Assemblies</c> (see the note there).
/// </summary>
public class EntityCollectionViewerOptions : SetPropertyNotifyChanged
{
    public bool AlwaysOnTop
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ObservableCollection<CombineStacksOpenContainersIgnoreEntry> CombineStacksIgnore
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ObservableCollection<ContainerSet> ContainerSets
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public bool EnableHotkeys
    {
        get;
        set => SetProperty( ref field, value );
    }

    /// <summary>Debounces <see cref="EntityCollectionViewerViewModel.SaveOptions" /> against no-op writes.</summary>
    public string Hash { get; set; }

    public bool HideLockedItems
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ObservableCollection<int> LockedItems
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ObservableCollection<CombineStacksOpenContainersIgnoreEntry> OpenContainersIgnore
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public bool OpenContainersOnlyKnownContainers
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool ShowChildItems
    {
        get;
        set => SetProperty( ref field, value );
    }

    public EntityCollectionSortStyle SortStyle
    {
        get;
        set => SetProperty( ref field, value );
    }

    public static EntityCollectionViewerOptions Deserialize( JObject config )
    {
        EntityCollectionViewerOptions options = new();

        if ( config == null )
        {
            return options;
        }

        options.AlwaysOnTop = config["AlwaysOnTop"]?.ToObject<bool>() ?? false;
        options.ShowChildItems = config["ShowChildItems"]?.ToObject<bool>() ?? false;
        options.HideLockedItems = config["HideLockedItems"]?.ToObject<bool>() ?? false;
        options.EnableHotkeys = config["EnableHotkeys"]?.ToObject<bool>() ?? false;
        options.SortStyle = Enum.TryParse( config["SortStyle"]?.ToObject<string>(),
            out EntityCollectionSortStyle sortStyle )
            ? sortStyle
            : EntityCollectionSortStyle.None;
        options.OpenContainersOnlyKnownContainers =
            config["OpenContainersOnlyKnownContainers"]?.ToObject<bool>() ?? false;

        options.LockedItems = config["LockedItems"]?.ToObject<ObservableCollection<int>>() ??
                               [];

        foreach ( JToken entry in config["CombineStacksIgnore"] ?? Enumerable.Empty<JToken>() )
        {
            options.CombineStacksIgnore.Add( new CombineStacksOpenContainersIgnoreEntry
            {
                ID = entry["ID"]?.ToObject<int>() ?? 0,
                Cliloc = entry["Cliloc"]?.ToObject<int>() ?? -1,
                Hue = entry["Hue"]?.ToObject<int>() ?? -1
            } );
        }

        foreach ( JToken entry in config["OpenContainersIgnore"] ?? Enumerable.Empty<JToken>() )
        {
            options.OpenContainersIgnore.Add( new CombineStacksOpenContainersIgnoreEntry
            {
                ID = entry["ID"]?.ToObject<int>() ?? 0,
                Cliloc = entry["Cliloc"]?.ToObject<int>() ?? -1,
                Hue = entry["Hue"]?.ToObject<int>() ?? -1
            } );
        }

        foreach ( JToken set in config["ContainerSets"] ?? Enumerable.Empty<JToken>() )
        {
            foreach ( JProperty property in set.Children<JProperty>() )
            {
                options.ContainerSets.Add( new ContainerSet
                {
                    Name = property.Name,
                    Items = property.Value.ToObject<ObservableCollection<int>>()
                } );
            }
        }

        // Assemblies (custom filter/autoloot constraint DLLs) is deliberately not round-tripped -
        // Assembly.LoadFile-ing arbitrary plugin DLLs from a Windows-style path list doesn't carry
        // over cleanly, and nothing on this side consumes them yet (see ECV_TODO.md). A key present
        // in a base-ClassicAssist-written file is simply dropped on next save from here.

        options.Hash = config.ToString().SHA1();

        return options;
    }

    public static JToken Serialize( EntityCollectionViewerOptions options )
    {
        JObject config = new()
        {
            { "AlwaysOnTop", options.AlwaysOnTop },
            { "ShowChildItems", options.ShowChildItems },
            { "HideLockedItems", options.HideLockedItems },
            { "EnableHotkeys", options.EnableHotkeys },
            { "SortStyle", options.SortStyle.ToString() },
            { "OpenContainersOnlyKnownContainers", options.OpenContainersOnlyKnownContainers },
            { "LockedItems", new JArray( options.LockedItems ?? [] ) },
            {
                "CombineStacksIgnore",
                new JArray( ( options.CombineStacksIgnore ??
            [] ).Select( entry =>
            new JObject { { "ID", entry.ID }, { "Cliloc", entry.Cliloc }, { "Hue", entry.Hue } } ) )
            },
            {
                "OpenContainersIgnore",
                new JArray( ( options.OpenContainersIgnore ??
            [] ).Select( entry =>
            new JObject { { "ID", entry.ID }, { "Cliloc", entry.Cliloc }, { "Hue", entry.Hue } } ) )
            },
            {
                "ContainerSets",
                new JArray( ( options.ContainerSets ?? [] )
            .Select( set => (JToken) new JObject { { set.Name, new JArray( set.Items ) } } ) )
            }
        };

        return config;
    }
}
