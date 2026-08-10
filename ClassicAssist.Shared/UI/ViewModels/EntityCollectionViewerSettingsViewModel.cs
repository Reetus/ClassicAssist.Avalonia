using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.Models;
using Commands = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.UI.ViewModels;

/// <summary>
///     Backs the Entity Collection Viewer's settings window - editing for the three lists on
///     <see cref="EntityCollectionViewerOptions" /> that don't have anywhere else to live (Combine
///     Stacks / Open All Containers ignore lists, Container Sets). Unlike old, which splits this
///     across three sub-view-models (one per list), everything lives here - the lists are small and
///     independent enough that the split bought old-side reuse this port doesn't need.
/// </summary>
public class EntityCollectionViewerSettingsViewModel : BaseViewModel
{
    public ICommand AddCombineStacksEntryCommand => field ??= new RelayCommand( o =>
            Options.CombineStacksIgnore.Add( new CombineStacksOpenContainersIgnoreEntry() ), o => true );

    public ICommand AddContainerSetCommand => field ??= new RelayCommand( o =>
        {
            ContainerSet set = new() { Name = "New Set" };

            Options.ContainerSets.Add( set );

            SelectedContainerSet = set;
        }, o => true );

    public ICommand AddContainerSetItemCommand => field ??=
            new RelayCommandAsync( AddContainerSetItem, o => SelectedContainerSet != null );

    public ICommand AddOpenContainersEntryCommand => field ??= new RelayCommand( o =>
            Options.OpenContainersIgnore.Add( new CombineStacksOpenContainersIgnoreEntry() ), o => true );

    public EntityCollectionViewerOptions Options
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand RemoveCombineStacksEntryCommand => field ??= new RelayCommand(
            o => Options.CombineStacksIgnore.Remove( SelectedCombineStacksEntry ),
            o => SelectedCombineStacksEntry != null );

    public ICommand RemoveContainerSetCommand => field ??= new RelayCommand( o =>
        {
            Options.ContainerSets.Remove( SelectedContainerSet );
            SelectedContainerSet = null;
        }, o => SelectedContainerSet != null );

    public ICommand RemoveContainerSetItemCommand => field ??= new RelayCommand(
            o => SelectedContainerSet?.Items.Remove( SelectedContainerSetItem ),
            o => SelectedContainerSet != null );

    public ICommand RemoveOpenContainersEntryCommand => field ??= new RelayCommand(
            o => Options.OpenContainersIgnore.Remove( SelectedOpenContainersEntry ),
            o => SelectedOpenContainersEntry != null );

    public CombineStacksOpenContainersIgnoreEntry SelectedCombineStacksEntry
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ContainerSet SelectedContainerSet
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int SelectedContainerSetItem
    {
        get;
        set => SetProperty( ref field, value );
    }

    public CombineStacksOpenContainersIgnoreEntry SelectedOpenContainersEntry
    {
        get;
        set => SetProperty( ref field, value );
    }

    private async Task AddContainerSetItem( object arg )
    {
        int serial = await Commands.GetTargetSerialAsync( Strings.Target_container___ );

        if ( serial == 0 || SelectedContainerSet == null || SelectedContainerSet.Items.Contains( serial ) )
        {
            return;
        }

        SelectedContainerSet.Items.Add( serial );
    }
}
