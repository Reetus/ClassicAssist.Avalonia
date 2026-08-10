#region License

// Copyright (C) 2020 Reetus
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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json;

namespace ClassicAssist.Shared.UI.ViewModels.Autoloot;

public class CustomPropertiesViewModel : BaseViewModel
{
    private readonly string _propertiesFileCustom =
        Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "Data", "Properties.Custom.json" );

    /// <summary>
    ///     Raised after <see cref="SaveCustomProperties" /> writes <c>Properties.Custom.json</c>, so
    ///     long-lived consumers (an already-open EntityCollectionViewer window) can reload their
    ///     constraint list instead of waiting for the next time they're constructed.
    /// </summary>
    public static event EventHandler Saved;

    public CustomPropertiesViewModel()
    {
        LoadCustomProperties();
    }

    public ICommand ChooseFromClilocCommand => field ??= new RelayCommandAsync( ChooseFromCliloc, o => true );

    public ICommand ChooseFromItemCommand => field ??= new RelayCommandAsync( ChooseFromItem, o => true );

    public ObservableCollection<CustomProperty> Properties
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ICommand RemoveCommand => field ??= new RelayCommand( Remove, o => SelectedProperty != null );

    public ICommand SaveCommand => field ??= new RelayCommand( Save, o => true );

    public CustomProperty SelectedProperty
    {
        get;
        set => SetProperty( ref field, value );
    }

    private async Task ChooseFromCliloc( object obj )
    {
        ClilocSelectionViewModel vm = new();

        // Must be awaited: InvokeDialog completes when the dialog closes, so without this the
        // DialogResult check below runs before the user has even seen the window and always
        // takes the early return.
        await Engine.UIInvoker.InvokeDialog( "ClilocSelectionWindow", dataContext: vm );

        if ( vm.DialogResult != MessageBoxResult.OK )
        {
            return;
        }

        if ( Properties.Any( p => p.Cliloc == vm.SelectedCliloc.Key ) )
        {
            return;
        }

        Properties.AddSorted( new CustomProperty
        {
            Cliloc = vm.SelectedCliloc.Key,
            Name = vm.SelectedCliloc.Value,
            Arguments = vm.SelectedCliloc.Value.Contains( "~" )
        } );
    }

    private async Task ChooseFromItem( object obj )
    {
        int serial = await Commands.GetTargetSerialAsync( Strings.Target_object___, 90000 );

        if ( serial == 0 )
        {
            Commands.SystemMessage( Strings.Cannot_find_item___ );
            return;
        }

        Item item = Engine.Items.GetItem( serial );

        if ( item == null )
        {
            Commands.SystemMessage( Strings.Cannot_find_item___ );
            return;
        }

        if ( item.Properties == null )
        {
            Commands.SystemMessage( Strings.Item_properties_null_or_not_loaded___ );
            return;
        }

        PropertySelectionViewModel vm = new( item.Properties );
        await Engine.UIInvoker.InvokeDialog( "PropertySelectionWindow", dataContext: vm );

        if ( vm.DialogResult != MessageBoxResult.OK )
        {
            return;
        }

        IEnumerable<SelectProperties> selectedProperties = vm.Properties.Where( p => p.Selected );

        foreach ( SelectProperties property in selectedProperties )
        {
            if ( Properties.Any( p => p.Cliloc == property.Property.Cliloc ) )
            {
                continue;
            }

            Properties.AddSorted( new CustomProperty
            {
                Name = property.Name,
                Cliloc = property.Property.Cliloc,
                Arguments = property.Property.Arguments != null && property.Property.Arguments.Length > 0,
                ArgumentIndex = property.Property.Arguments != null ? 0 : -1
            } );
        }
    }

    private void Remove( object obj )
    {
        if ( SelectedProperty == null )
        {
            return;
        }

        Properties.Remove( SelectedProperty );
    }

    private void Save( object obj )
    {
        SaveCustomProperties();
        Saved?.Invoke( this, EventArgs.Empty );
    }

    private void SaveCustomProperties()
    {
        List<PropertyEntry> properties = [.. Properties.Select( property => new PropertyEntry
        {
            ClilocIndex = property.ArgumentIndex,
            Clilocs = [property.Cliloc],
            ConstraintType = 0,
            Name = property.Name
        } )];

        File.WriteAllText( _propertiesFileCustom, JsonConvert.SerializeObject( properties ) );
    }

    private void LoadCustomProperties()
    {
        if ( !File.Exists( _propertiesFileCustom ) )
        {
            return;
        }

        JsonSerializer serializer = new();

        using StreamReader sr = new( _propertiesFileCustom );
        using JsonTextReader reader = new( sr );
        PropertyEntry[] constraints = serializer.Deserialize<PropertyEntry[]>( reader );

        foreach ( PropertyEntry constraint in constraints )
        {
            CustomProperty customProperty = new()
            {
                Name = constraint.Name,
                Cliloc = constraint.Clilocs[0],
                Arguments = constraint.ClilocIndex >= 0,
                ArgumentIndex = constraint.ClilocIndex
            };

            Properties.AddSorted( customProperty );
        }
    }
}

public class CustomProperty : IComparable<CustomProperty>, INotifyPropertyChanged
{
    public int ArgumentIndex
    {
        get;
        set => SetField( ref field, value );
    } = -1;

    public bool Arguments
    {
        get;
        set
        {
            switch ( value )
            {
                case false when ArgumentIndex != -1:
                    ArgumentIndex = -1;
                    break;
                case true when ArgumentIndex < 0:
                    ArgumentIndex = 0;
                    break;
            }

            SetField( ref field, value );
        }
    }

    public int Cliloc { get; set; }
    public string Name { get; set; }

    public int CompareTo( CustomProperty other )
    {
        if ( ReferenceEquals( this, other ) )
        {
            return 0;
        }

        return other is null
            ? 1
            : string.Compare( Name, other.Name, StringComparison.InvariantCultureIgnoreCase );
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
    {
        PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
    }

    protected bool SetField<T>( ref T field, T value, [CallerMemberName] string propertyName = null )
    {
        if ( EqualityComparer<T>.Default.Equals( field, value ) )
        {
            return false;
        }

        field = value;
        OnPropertyChanged( propertyName );
        return true;
    }
}