using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ClassicAssist.Launcher.Models;

public class ShardEntry : INotifyPropertyChanged, IEquatable<ShardEntry>
{
    private const string RUNUO_REGEX = @".*Clients=(\d+),.*";

    [JsonProperty( "address" )]
    public string Address
    {
        get;
        set => SetProperty( ref field, value );
    }

    [JsonIgnore]
    public bool Deleted
    {
        get;
        set => SetProperty( ref field, value );
    }

    [JsonProperty( "encryption" )]
    public bool Encryption
    {
        get;
        set => SetProperty( ref field, value );
    }

    [JsonProperty( "has_status_protocol" )]
    public bool HasStatusProtocol { get; set; } = true;

    [JsonIgnore]
    public bool IsPreset { get; set; }

    [JsonProperty( "last_played" )]
    public DateTime LastPlayed
    {
        get;
        set => SetProperty( ref field, value );
    }

    [JsonProperty( "name" )]
    public string Name
    {
        get;
        set => SetProperty( ref field, value );
    }

    [JsonIgnore]
    public string Ping
    {
        get;
        set => SetProperty( ref field, value );
    }

    [JsonProperty( "port" )]
    public int Port
    {
        get;
        set => SetProperty( ref field, value );
    }

    [JsonProperty( "shard_type" )]
    public int ShardType
    {
        get;
        set => SetProperty( ref field, value );
    }

    [JsonIgnore]
    public string Status
    {
        get;
        set => SetProperty( ref field, value );
    }

    [JsonIgnore]
    public string StatusRegex { get; set; } = RUNUO_REGEX;

    [JsonProperty( "website" )]
    public string Website
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool Equals( ShardEntry other )
    {
        if ( other is null )
        {
            return false;
        }

        if ( ReferenceEquals( this, other ) )
        {
            return true;
        }

        return Name == other.Name && IsPreset == other.IsPreset;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public void SetProperty<T>( ref T obj, T value, [CallerMemberName] string propertyName = "" )
    {
        obj = value;
        OnPropertyChanged( propertyName );
    }

    protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
    {
        PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
    }

    public override bool Equals( object obj )
    {
        if ( obj is null )
        {
            return false;
        }

        if ( ReferenceEquals( this, obj ) )
        {
            return true;
        }

        if ( obj.GetType() != GetType() )
        {
            return false;
        }

        return Equals( (ShardEntry) obj );
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ( ( Name != null ? Name.GetHashCode() : 0 ) * 397 ) ^ IsPreset.GetHashCode();
        }
    }
}
