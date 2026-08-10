using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using ClassicAssist.Launcher.ViewModels;
using ClassicAssist.Updater.Services;

namespace ClassicAssist.Updater.ViewModels;

public class ProcessesViewModel : BaseViewModel
{
    public ProcessesViewModel()
    {
    }

    public ProcessesViewModel( IEnumerable<Process> processes )
    {
        Processes = [.. processes.Select( RunningClients.Describe )];
    }

    /// <summary>
    ///     False unless OK was pressed, so closing the dialog by any other route - the title bar, Esc,
    ///     the window manager - cancels the update rather than proceeding to kill the clients.
    /// </summary>
    public bool Accepted { get; private set; }

    public ICommand OKCommand => field ??= new RelayCommand( OK, _ => true );

    public ObservableCollection<string> Processes
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    private void OK( object obj )
    {
        Accepted = true;
    }
}
