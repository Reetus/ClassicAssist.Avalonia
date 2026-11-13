using System.Collections;
using System.Collections.Generic;
using ClassicAssist.Shared;

namespace ClassicAssist.UO.Objects;

public sealed class MobileCollection : EntityCollection<Mobile>, IEnumerable<Mobile>
{
    private readonly ItemCollection _linkedItemCollection;

    public MobileCollection( ItemCollection linkedItemCollection )
    {
        _linkedItemCollection = linkedItemCollection;
    }

    public IEnumerator<Mobile> GetEnumerator()
    {
        return EntityList.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public Mobile GetMobile( int serial )
    {
        return serial == Engine.Player?.Serial ? Engine.Player : GetEntity( serial );
    }

    public bool GetMobile( int serial, out Mobile mobile )
    {
        mobile = GetEntity( serial );
        return mobile != null;
    }

    public Mobile[] GetMobiles()
    {
        return GetEntities();
    }

    public override void RemoveByDistance( int maxDistance, int x, int y )
    {
        bool changed = false;

        // Never the player: 0x20 / 0x77 put it in this collection, and x / y are read on the tick
        // thread while the packet thread may be applying a teleport, so it can look out of range of
        // itself. Sweeping it removed everything it owns, the backpack and its contents included.
        int playerSerial = Engine.Player?.Serial ?? 0;

        Mobile[] mobiles = SelectEntities( m =>
        {
            if ( m.Serial == playerSerial )
            {
                return false;
            }

            double d = UOMath.Distance( x, y, m.X, m.Y );
            return d > maxDistance;
        } );

        if ( mobiles == null )
        {
            return;
        }

        foreach ( Mobile m in mobiles )
        {
            Remove( m.Serial );
            _linkedItemCollection.RemoveByOwner( m.Serial );
            changed = true;
        }

        if ( changed )
        {
            OnCollectionChanged( false, mobiles );
        }
    }
}