using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ClassicAssist.Data.Scavenger;

namespace ClassicAssist.UI.ViewModels;

public class ScavengerClilocFilterViewModel : BaseViewModel
{
    public ScavengerClilocFilterViewModel()
    {
    }

    public ScavengerClilocFilterViewModel( bool enabled, IEnumerable<ScavengerClilocFilterEntry> items )
    {
        Enabled = enabled;

        foreach ( ScavengerClilocFilterEntry item in items )
        {
            Items.Add( item );
        }

        if ( Items.Count == 0 )
        {
            Items.Add( new ScavengerClilocFilterEntry { Cliloc = 501643 } );
            Items.Add( new ScavengerClilocFilterEntry { Cliloc = 501644 } );
        }
    }

    public ICommand AddCommand => field ??= new RelayCommand( Add, o => true );

    public bool Enabled
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ObservableCollection<ScavengerClilocFilterEntry> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ICommand OkCommand => field ??= new RelayCommand( Ok, o => true );

    public ICommand RemoveCommand => field ??= new RelayCommand( Remove, o => o != null );

    public bool Result { get; set; }

    public ScavengerClilocFilterEntry SelectedItem
    {
        get;
        set => SetProperty( ref field, value );
    }

    private void Add( object obj )
    {
        Items.Add( new ScavengerClilocFilterEntry { Cliloc = 501643 } );
    }

    private void Ok( object obj )
    {
        Result = true;
    }

    private void Remove( object obj )
    {
        if ( obj is ScavengerClilocFilterEntry filterEntry )
        {
            Items.Remove( filterEntry );
        }
    }
}
