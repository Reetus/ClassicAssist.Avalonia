using System;
using System.Collections.ObjectModel;
using System.Linq;
using ClassicAssist.Misc;
using ClassicAssist.Shared.UI;
using ClassicAssist.UI.Models;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Data.Misc
{
    /// <summary>
    ///     Per-window Entity Collection Viewer settings, persisted to
    ///     <c>EntityCollectionViewerOptions.json</c> in the same shape base ClassicAssist uses (see
    ///     <see cref="Serialize" />/<see cref="Deserialize" />) - a file written by either side loads
    ///     cleanly in the other, aside from <c>Assemblies</c> (see the note there).
    /// </summary>
    public class EntityCollectionViewerOptions : SetPropertyNotifyChanged
    {
        private bool _alwaysOnTop;
        private ObservableCollection<CombineStacksOpenContainersIgnoreEntry> _combineStacksIgnore =
            new ObservableCollection<CombineStacksOpenContainersIgnoreEntry>();
        private ObservableCollection<ContainerSet> _containerSets = new ObservableCollection<ContainerSet>();
        private bool _enableHotkeys;
        private bool _hideLockedItems;
        private ObservableCollection<int> _lockedItems = new ObservableCollection<int>();
        private ObservableCollection<CombineStacksOpenContainersIgnoreEntry> _openContainersIgnore =
            new ObservableCollection<CombineStacksOpenContainersIgnoreEntry>();
        private bool _openContainersOnlyKnownContainers;
        private bool _showChildItems;
        private EntityCollectionSortStyle _sortStyle;

        public bool AlwaysOnTop
        {
            get => _alwaysOnTop;
            set => SetProperty( ref _alwaysOnTop, value );
        }

        public ObservableCollection<CombineStacksOpenContainersIgnoreEntry> CombineStacksIgnore
        {
            get => _combineStacksIgnore;
            set => SetProperty( ref _combineStacksIgnore, value );
        }

        public ObservableCollection<ContainerSet> ContainerSets
        {
            get => _containerSets;
            set => SetProperty( ref _containerSets, value );
        }

        public bool EnableHotkeys
        {
            get => _enableHotkeys;
            set => SetProperty( ref _enableHotkeys, value );
        }

        /// <summary>Debounces <see cref="EntityCollectionViewerViewModel.SaveOptions" /> against no-op writes.</summary>
        public string Hash { get; set; }

        public bool HideLockedItems
        {
            get => _hideLockedItems;
            set => SetProperty( ref _hideLockedItems, value );
        }

        public ObservableCollection<int> LockedItems
        {
            get => _lockedItems;
            set => SetProperty( ref _lockedItems, value );
        }

        public ObservableCollection<CombineStacksOpenContainersIgnoreEntry> OpenContainersIgnore
        {
            get => _openContainersIgnore;
            set => SetProperty( ref _openContainersIgnore, value );
        }

        public bool OpenContainersOnlyKnownContainers
        {
            get => _openContainersOnlyKnownContainers;
            set => SetProperty( ref _openContainersOnlyKnownContainers, value );
        }

        public bool ShowChildItems
        {
            get => _showChildItems;
            set => SetProperty( ref _showChildItems, value );
        }

        public EntityCollectionSortStyle SortStyle
        {
            get => _sortStyle;
            set => SetProperty( ref _sortStyle, value );
        }

        public static EntityCollectionViewerOptions Deserialize( JObject config )
        {
            EntityCollectionViewerOptions options = new EntityCollectionViewerOptions();

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
                                   new ObservableCollection<int>();

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
            JObject config = new JObject
            {
                { "AlwaysOnTop", options.AlwaysOnTop },
                { "ShowChildItems", options.ShowChildItems },
                { "HideLockedItems", options.HideLockedItems },
                { "EnableHotkeys", options.EnableHotkeys },
                { "SortStyle", options.SortStyle.ToString() },
                { "OpenContainersOnlyKnownContainers", options.OpenContainersOnlyKnownContainers }
            };

            config.Add( "LockedItems", new JArray( options.LockedItems ?? new ObservableCollection<int>() ) );

            config.Add( "CombineStacksIgnore", new JArray( ( options.CombineStacksIgnore ??
                new ObservableCollection<CombineStacksOpenContainersIgnoreEntry>() ).Select( entry =>
                new JObject { { "ID", entry.ID }, { "Cliloc", entry.Cliloc }, { "Hue", entry.Hue } } ) ) );

            config.Add( "OpenContainersIgnore", new JArray( ( options.OpenContainersIgnore ??
                new ObservableCollection<CombineStacksOpenContainersIgnoreEntry>() ).Select( entry =>
                new JObject { { "ID", entry.ID }, { "Cliloc", entry.Cliloc }, { "Hue", entry.Hue } } ) ) );

            config.Add( "ContainerSets", new JArray( ( options.ContainerSets ?? new ObservableCollection<ContainerSet>() )
                .Select( set => (JToken) new JObject { { set.Name, new JArray( set.Items ) } } ) ) );

            return config;
        }
    }
}
