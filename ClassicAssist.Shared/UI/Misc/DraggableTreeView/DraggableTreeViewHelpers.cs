using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ClassicAssist.UI.Misc.DraggableTreeView;

public static class DraggableTreeViewHelpers
{
    public static int GetIndex( IDraggable draggable, ObservableCollection<IDraggable> collection )
    {
        ObservableCollection<IDraggable> parent = GetParent( draggable, collection );

        return parent?.IndexOf( draggable ) ?? collection.IndexOf( draggable );
    }

    private static IEnumerable<IDraggableGroup> GetGroups( IEnumerable<IDraggable> collection )
    {
        return collection.Where( i => i is IDraggableGroup ).Cast<IDraggableGroup>();
    }

    private static ObservableCollection<IDraggable> GetParent( IDraggable draggable,
        ObservableCollection<IDraggable> parent )
    {
        if ( parent.Contains( draggable ) )
        {
            return parent;
        }

        IEnumerable<IDraggableGroup> groups = GetGroups( parent );

        return groups.Select( draggableGroup => GetParent( draggable, draggableGroup.Children ) )
            .FirstOrDefault( childParent => childParent != null );
    }
}
