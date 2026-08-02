using System.Collections.ObjectModel;

namespace ClassicAssist.UI.Models
{
    /// <summary>
    ///     A collapsible group of rows in the Object Inspector, keyed by <see cref="ObjectInspectorData.Category" />.
    ///     WPF grouped <c>Items</c> live via <c>CollectionViewSource</c>; Avalonia has no equivalent for a plain
    ///     ItemsControl, so the view model builds the groups itself as items are added.
    /// </summary>
    public class ObjectInspectorCategory
    {
        public bool IsExpanded { get; set; } = true;
        public ObservableCollection<ObjectInspectorData> Items { get; } = new ObservableCollection<ObjectInspectorData>();
        public string Name { get; set; }
    }
}
