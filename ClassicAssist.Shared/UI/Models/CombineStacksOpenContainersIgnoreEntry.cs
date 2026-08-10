using ClassicAssist.Shared.UI;

namespace ClassicAssist.UI.Models;

public class CombineStacksOpenContainersIgnoreEntry : SetPropertyNotifyChanged
{
    public int Cliloc
    {
        get;
        set => SetProperty( ref field, value );
    } = -1;

    public int Hue
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int ID
    {
        get;
        set => SetProperty( ref field, value );
    }
}
