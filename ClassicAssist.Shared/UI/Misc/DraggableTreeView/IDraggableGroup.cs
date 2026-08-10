using System.Collections.ObjectModel;

namespace ClassicAssist.UI.Misc.DraggableTreeView;

public interface IDraggableGroup : IDraggable
{
    ObservableCollection<IDraggable> Children { get; set; }
}
