#region License

// Copyright (C) 2026 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Macros;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.ViewModels;

namespace ClassicAssist.Shared.UI.ViewModels.Debug
{
    /// <summary>
    ///     Lets extra .dll assemblies be loaded so their public static classes with a parameterless
    ///     static <c>Initialize()</c> method run, and so any classes under a <c>*.Macros.Commands</c>
    ///     namespace they contain become importable from macros - see
    ///     <see cref="AssistantOptions.Assemblies" /> and <see cref="MacroInvoker.InitializeImports" />.
    /// </summary>
    public class DebugAssembliesViewModel : BaseViewModel
    {
        private ObservableCollection<Assembly> _items = new ObservableCollection<Assembly>();
        private ICommand _loadCommand;
        private ICommand _removeCommand;
        private ICommand _saveCommand;
        private Assembly _selectedItem;

        public DebugAssembliesViewModel()
        {
            foreach ( string assemblyPath in AssistantOptions.Assemblies ?? Array.Empty<string>() )
            {
                Assembly assembly = LoadAssembly( assemblyPath );

                if ( assembly != null )
                {
                    Items.Add( assembly );
                }
            }
        }

        public ObservableCollection<Assembly> Items
        {
            get => _items;
            set => SetProperty( ref _items, value );
        }

        public ICommand LoadCommand => _loadCommand ?? ( _loadCommand = new RelayCommandAsync( Load, o => true ) );

        public ICommand RemoveCommand =>
            _removeCommand ?? ( _removeCommand = new RelayCommandAsync( Remove, o => SelectedItem != null ) );

        public ICommand SaveCommand => _saveCommand ?? ( _saveCommand = new RelayCommandAsync( Save, o => true ) );

        public Assembly SelectedItem
        {
            get => _selectedItem;
            set => SetProperty( ref _selectedItem, value );
        }

        private async Task Load( object arg )
        {
            string fileName = await Engine.UIInvoker.ShowOpenFileDialogAsync( Strings.Additional_Assemblies,
                "DLL Files", new[] { "*.dll" } );

            if ( string.IsNullOrEmpty( fileName ) )
            {
                return;
            }

            try
            {
                Assembly assembly = LoadAssembly( fileName );

                if ( assembly != null )
                {
                    _dispatcher.Invoke( () => Items.Add( assembly ) );
                }
            }
            catch ( Exception e )
            {
                Engine.MessageBoxProvider?.Show( string.Format( Strings.Error_loading_assembly___0_, e.Message ) );
            }
        }

        private static Assembly LoadAssembly( string fileName )
        {
            return File.Exists( fileName ) ? Assembly.LoadFile( fileName ) : null;
        }

        private Task Remove( object arg )
        {
            Assembly assembly = SelectedItem;

            if ( assembly != null )
            {
                _dispatcher.Invoke( () => Items.Remove( assembly ) );
            }

            return Task.CompletedTask;
        }

        private Task Save( object arg )
        {
            AssistantOptions.Assemblies = Items.Select( a => a.Location ).ToArray();
            AssistantOptions.Save();

            MacroInvoker.ResetImportCache();

            return Task.CompletedTask;
        }
    }
}
