using System.Collections.ObjectModel;
using ClassicAssist.Shared.UI;

namespace ClassicAssist.UI.Models;

public class ContainerSet : SetPropertyNotifyChanged
{
    public ObservableCollection<int> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public string Name
    {
        get;
        set => SetProperty( ref field, value );
    }
}
