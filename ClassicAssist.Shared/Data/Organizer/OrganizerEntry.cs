using System;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.UI.Misc;

namespace ClassicAssist.Data.Organizer;

public class OrganizerEntry : HotkeyEntry
{
    public Func<bool> IsRunning;

    public bool Complete
    {
        get;
        set
        {
            SetProperty( ref field, value );

            if ( !value )
            {
                ReturnExcess = false;
            }
        }
    }

    public int DestinationContainer { get; set; }

    public ObservableCollectionEx<OrganizerItem> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public int SourceContainer { get; set; }

    public bool Stack
    {
        get;
        set => SetProperty( ref field, value );
    } = true;

    public bool ReturnExcess
    {
        get;
        set => SetProperty( ref field, value );
    }

    public override string ToString()
    {
        return Name;
    }
}