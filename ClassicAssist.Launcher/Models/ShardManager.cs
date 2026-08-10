using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ClassicAssist.Launcher.Models;

public class ShardManager : INotifyPropertyChanged
{
    private static ShardManager _instance;
    private static readonly Lock _lock = new();
    private readonly ShardEntryComparer _comparer = new();

    private ShardManager()
    {
        Shards.AddSorted( new ShardEntry
        {
            Name = "The Crossroads (ML)",
            Address = "login.uocrossroads.net",
            Port = 2593,
            IsPreset = true,
            HasStatusProtocol = true,
            Website = "https://www.uocrossroads.net/"
        }, _comparer );

        Shards.AddSorted(
            new ShardEntry
            {
                Name = "Official EA Servers",
                Address = "login.ultimaonline.com",
                Port = 7775,
                IsPreset = true,
                HasStatusProtocol = false,
                Encryption = true,
                Website = "http://www.uo.com/"
            }, _comparer );

        Shards.AddSorted(
            new ShardEntry
            {
                Name = "UOGamers: Demise",
                Address = "login.uogdemise.com",
                Port = 2593,
                IsPreset = true,
                Website = "https://uogdemise.com/"
            }, _comparer );

        Shards.AddSorted(
            new ShardEntry
            {
                Name = "Heritage UO",
                Address = "play.trueuo.com",
                Port = 2593,
                IsPreset = true,
                Website = "https://trueuo.com/"
            }, _comparer );

        Shards.AddSorted(
            new ShardEntry
            {
                Name = "UO Forever",
                Address = "login.uoforever.com",
                Port = 2599,
                IsPreset = true,
                Website = "https://www.uoforever.com/"
            }, _comparer );

        Shards.AddSorted(
            new ShardEntry
            {
                Name = "UO Elemental",
                Address = "login.uoelemental.com",
                Port = 2593,
                IsPreset = true,
                HasStatusProtocol = true,
                Website = "https://uoelemental.com/"
            }, _comparer );

        Shards.AddSorted(
            new ShardEntry
            {
                Name = "UO:Renaissance",
                Address = "login.uorenaissance.com",
                Port = 2593,
                IsPreset = true,
                HasStatusProtocol = true,
                Website = "http://www.uorenaissance.com/"
            }, _comparer );

        Shards.AddSorted(
            new ShardEntry
            {
                Name = "UO Evolution",
                Address = "play.uoevolution.com",
                Port = 2593,
                IsPreset = true,
                HasStatusProtocol = true,
                Website = "http://uoevolution.com/"
            }, _comparer );

        Shards.AddSorted(
            new ShardEntry
            {
                Name = "NoTramAos",
                Address = "notramaos.servegame.com",
                Port = 2593,
                IsPreset = true,
                HasStatusProtocol = true,
                Website = "http://notramaos.com/"
            }, _comparer );

        Shards.AddSorted(
            new ShardEntry
            {
                Name = "UOAlive",
                Address = "login.uoalive.com",
                Port = 2593,
                IsPreset = true,
                HasStatusProtocol = true,
                Website = "https://uoalive.com/"
            }, _comparer );

        // VisibleShards is recomputed on every access rather than cached, so nothing refreshes
        // the DataGrid bound to it unless something raises this PropertyChanged - both when the
        // Shards collection itself changes AND when an individual entry's Deleted flag flips
        // (e.g. hiding a preset via ShardsViewModel.Remove), which is a property change on the
        // ShardEntry, not on Shards, and would otherwise never notify.
        Shards.CollectionChanged += Shards_CollectionChanged;

        foreach ( ShardEntry shard in Shards )
        {
            shard.PropertyChanged += Shard_PropertyChanged;
        }
    }

    private void Shards_CollectionChanged( object sender, NotifyCollectionChangedEventArgs args )
    {
        if ( args.OldItems != null )
        {
            foreach ( ShardEntry removed in args.OldItems )
            {
                removed.PropertyChanged -= Shard_PropertyChanged;
            }
        }

        if ( args.NewItems != null )
        {
            foreach ( ShardEntry added in args.NewItems )
            {
                added.PropertyChanged += Shard_PropertyChanged;
            }
        }

        OnPropertyChanged( nameof( VisibleShards ) );
    }

    private void Shard_PropertyChanged( object sender, PropertyChangedEventArgs args )
    {
        if ( args.PropertyName == nameof( ShardEntry.Deleted ) )
        {
            OnPropertyChanged( nameof( VisibleShards ) );
        }
    }

    public bool OverridePresets { get; set; }

    public ObservableCollection<ShardEntry> Shards { get; set; } = [];

    public ObservableCollection<ShardEntry> VisibleShards =>
        new( Shards.Where( e => !e.Deleted ) );

    public event PropertyChangedEventHandler PropertyChanged;

    public static ShardManager GetInstance()
    {
        // ReSharper disable once InvertIf
        if ( _instance == null )
        {
            lock ( _lock )
            {
                if ( _instance != null )
                {
                    return _instance;
                }

                _instance = new ShardManager();
                return _instance;
            }
        }

        return _instance;
    }

    protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
    {
        PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
    }

    public void ImportPresets( List<ShardEntry> shards )
    {
        OverridePresets = true;

        IEnumerable<ShardEntry> deletedShards = Shards.Where( e => e.IsPreset && !shards.Contains( e ) ).ToList();

        foreach ( ShardEntry deletedShard in deletedShards )
        {
            Shards.Remove( deletedShard );
        }

        foreach ( ShardEntry shardEntry in shards )
        {
            ShardEntry existing = Shards.FirstOrDefault( e => e.Equals( shardEntry ) );

            if ( existing != null )
            {
                existing.Address = shardEntry.Address;
                existing.Port = shardEntry.Port;
                existing.HasStatusProtocol = shardEntry.HasStatusProtocol;
                existing.Website = shardEntry.Website;
                existing.Encryption = shardEntry.Encryption;
            }
            else
            {
                Shards.AddSorted( shardEntry, _comparer );
            }
        }
    }
}
