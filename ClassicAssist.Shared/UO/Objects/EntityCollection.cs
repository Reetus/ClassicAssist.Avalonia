using System;
using System.Collections.Concurrent;
using System.Linq;

namespace ClassicAssist.UO.Objects;

public abstract class EntityCollection<T> where T : Entity
{
    public delegate void dCollectionChanged( int totalCount, bool added, T[] entities );

    protected ConcurrentDictionary<int, T> EntityList;

    protected EntityCollection() : this( short.MaxValue )
    {
    }

    protected EntityCollection( int capacity )
    {
        EntityList = new ConcurrentDictionary<int, T>( 16, capacity );
    }

    public T this[int s]
    {
        get => EntityList.ContainsKey( s ) ? EntityList[s] : null;
        set => EntityList[s] = value;
    }

    public virtual bool Add( T entity )
    {
        bool changed = EntityList.AddOrUpdate( entity.Serial, entity, ( k, v ) => entity ) != null;

        if ( changed && _collectionChanged != null )
        {
            OnCollectionChanged( true, [entity] );
        }

        return changed;
    }

    public virtual bool Add( T[] entities )
    {
        bool changed = false;

        foreach ( T entity in entities )
        {
            if ( EntityList.AddOrUpdate( entity.Serial, entity, ( k, v ) => entity ) == null )
            {
                continue;
            }

            changed = true;
        }

        if ( changed && _collectionChanged != null )
        {
            OnCollectionChanged( true, entities );
        }

        return changed;
    }

    protected T[] GetEntities()
    {
        T[] entityArray = new T[EntityList.Values.Count];
        EntityList.Values.CopyTo( entityArray, 0 );
        return entityArray;
    }

    public virtual bool Remove( T entity )
    {
        return Remove( entity.Serial );
    }

    public virtual bool Remove( int serial )
    {
        EntityList.TryRemove( serial, out T val );

        if ( val != null && _collectionChanged != null )
        {
            OnCollectionChanged( false, [val] );
        }

        return val != null;
    }

    public virtual bool Remove( T[] entities )
    {
        bool changed = false;

        foreach ( T entity in entities )
        {
            if ( Remove( entity.Serial ) )
            {
                changed = true;
            }
        }

        return changed;
    }

    internal virtual void Clear()
    {
        T[] all = [.. EntityList.Values];

        EntityList.Clear();

        OnCollectionChanged( false, all );
    }

    private dCollectionChanged _collectionChanged;

    /// <summary>
    ///     Sticky flag (per entity type) so hot paths can skip the entire notification machinery unless
    ///     something has ever subscribed to any collection of this type. It never goes back to false:
    ///     one subscription permanently arms the (still cheap) subscriber checks.
    /// </summary>
    internal static bool HasEverSubscribed { get; private set; }

    internal bool HasSubscribers => _collectionChanged != null;

    public event dCollectionChanged CollectionChanged
    {
        add
        {
            _collectionChanged += value;
            HasEverSubscribed = true;
        }
        remove => _collectionChanged -= value;
    }

    public virtual void OnCollectionChanged( bool added, T[] entities )
    {
        _collectionChanged?.Invoke( EntityList.Count, added, entities );
    }

    public virtual void RemoveByDistance( int maxDistance, int x, int y )
    {
        T[] items = SelectEntities( i =>
            i is Item item && item.Owner == 0 && UOMath.Distance( x, y, item.X, item.Y ) > maxDistance );

        if ( items == null )
        {
            return;
        }

        bool changed = Remove( items );

        if ( changed )
        {
            OnCollectionChanged( false, items );
        }
    }

    public virtual T SelectEntity( Func<T, bool> func )
    {
        if ( func == null )
        {
            return null;
        }

        T entity = EntityList.Select( m => m.Value ).FirstOrDefault( func );

        return entity;
    }

    public virtual T[] SelectEntities( Func<T, bool> func )
    {
        return [.. EntityList.Select( m => m.Value ).Where( func )];
    }

    protected T GetEntity( int key )
    {
        EntityList.TryGetValue( key, out T val );

        return val;
    }
}