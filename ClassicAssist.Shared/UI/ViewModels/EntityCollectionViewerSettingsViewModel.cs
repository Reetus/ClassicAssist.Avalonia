using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data.Misc;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.Models;
using Commands = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.UI.ViewModels
{
    /// <summary>
    ///     Backs the Entity Collection Viewer's settings window - editing for the three lists on
    ///     <see cref="EntityCollectionViewerOptions" /> that don't have anywhere else to live (Combine
    ///     Stacks / Open All Containers ignore lists, Container Sets). Unlike old, which splits this
    ///     across three sub-view-models (one per list), everything lives here - the lists are small and
    ///     independent enough that the split bought old-side reuse this port doesn't need.
    /// </summary>
    public class EntityCollectionViewerSettingsViewModel : BaseViewModel
    {
        private ICommand _addCombineStacksEntryCommand;
        private ICommand _addContainerSetCommand;
        private ICommand _addContainerSetItemCommand;
        private ICommand _addOpenContainersEntryCommand;
        private EntityCollectionViewerOptions _options;
        private ICommand _removeCombineStacksEntryCommand;
        private ICommand _removeContainerSetCommand;
        private ICommand _removeContainerSetItemCommand;
        private ICommand _removeOpenContainersEntryCommand;
        private CombineStacksOpenContainersIgnoreEntry _selectedCombineStacksEntry;
        private ContainerSet _selectedContainerSet;
        private int _selectedContainerSetItem;
        private CombineStacksOpenContainersIgnoreEntry _selectedOpenContainersEntry;

        public ICommand AddCombineStacksEntryCommand =>
            _addCombineStacksEntryCommand ?? ( _addCombineStacksEntryCommand = new RelayCommand( o =>
                Options.CombineStacksIgnore.Add( new CombineStacksOpenContainersIgnoreEntry() ), o => true ) );

        public ICommand AddContainerSetCommand =>
            _addContainerSetCommand ?? ( _addContainerSetCommand = new RelayCommand( o =>
            {
                ContainerSet set = new ContainerSet { Name = "New Set" };

                Options.ContainerSets.Add( set );

                SelectedContainerSet = set;
            }, o => true ) );

        public ICommand AddContainerSetItemCommand =>
            _addContainerSetItemCommand ?? ( _addContainerSetItemCommand =
                new RelayCommandAsync( AddContainerSetItem, o => SelectedContainerSet != null ) );

        public ICommand AddOpenContainersEntryCommand =>
            _addOpenContainersEntryCommand ?? ( _addOpenContainersEntryCommand = new RelayCommand( o =>
                Options.OpenContainersIgnore.Add( new CombineStacksOpenContainersIgnoreEntry() ), o => true ) );

        public EntityCollectionViewerOptions Options
        {
            get => _options;
            set => SetProperty( ref _options, value );
        }

        public ICommand RemoveCombineStacksEntryCommand =>
            _removeCombineStacksEntryCommand ?? ( _removeCombineStacksEntryCommand = new RelayCommand(
                o => Options.CombineStacksIgnore.Remove( SelectedCombineStacksEntry ),
                o => SelectedCombineStacksEntry != null ) );

        public ICommand RemoveContainerSetCommand =>
            _removeContainerSetCommand ?? ( _removeContainerSetCommand = new RelayCommand( o =>
            {
                Options.ContainerSets.Remove( SelectedContainerSet );
                SelectedContainerSet = null;
            }, o => SelectedContainerSet != null ) );

        public ICommand RemoveContainerSetItemCommand =>
            _removeContainerSetItemCommand ?? ( _removeContainerSetItemCommand = new RelayCommand(
                o => SelectedContainerSet?.Items.Remove( SelectedContainerSetItem ),
                o => SelectedContainerSet != null ) );

        public ICommand RemoveOpenContainersEntryCommand =>
            _removeOpenContainersEntryCommand ?? ( _removeOpenContainersEntryCommand = new RelayCommand(
                o => Options.OpenContainersIgnore.Remove( SelectedOpenContainersEntry ),
                o => SelectedOpenContainersEntry != null ) );

        public CombineStacksOpenContainersIgnoreEntry SelectedCombineStacksEntry
        {
            get => _selectedCombineStacksEntry;
            set => SetProperty( ref _selectedCombineStacksEntry, value );
        }

        public ContainerSet SelectedContainerSet
        {
            get => _selectedContainerSet;
            set => SetProperty( ref _selectedContainerSet, value );
        }

        public int SelectedContainerSetItem
        {
            get => _selectedContainerSetItem;
            set => SetProperty( ref _selectedContainerSetItem, value );
        }

        public CombineStacksOpenContainersIgnoreEntry SelectedOpenContainersEntry
        {
            get => _selectedOpenContainersEntry;
            set => SetProperty( ref _selectedOpenContainersEntry, value );
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
}
