using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;

namespace ClassicAssist.Avalonia.Views;

public partial class HuePickerWindow : Window, INotifyPropertyChanged
{
    private RelayCommand _okCommand;

    public HuePickerWindow()
    {
        InitializeComponent();

        for ( int i = 0; i < 3000; i++ )
        {
            Items.Add( new HuePickerEntry { Index = i + 1, Entry = Hues._lazyHueEntries.Value[i] } );
        }

        ApplyFilter( FilterText );
    }

    public ObservableCollection<HuePickerEntry> FilteredItems
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public string FilterText
    {
        get;
        set
        {
            SetProperty( ref field, value );
            ApplyFilter( value );
        }
    }

    public ObservableCollection<HuePickerEntry> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ICommand OKCommand => _okCommand ??= new RelayCommand( OK, o => SelectedItem != null );

    public int SelectedHue
    {
        get;
        set => SetProperty( ref field, value );
    }

    public HuePickerEntry SelectedItem
    {
        get;
        set
        {
            SetProperty( ref field, value );

            // No CommandManager.RequerySuggested in Avalonia, so OK has to be invalidated by hand.
            _okCommand?.RaiseCanExecuteChanged();
        }
    }

    public new event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
    {
        PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
    }

    // ReSharper disable once RedundantAssignment
    public virtual void SetProperty<T>( ref T obj, T value, [CallerMemberName] string propertyName = "" )
    {
        obj = value;
        OnPropertyChanged( propertyName );
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }

    private void OK( object obj )
    {
        if ( obj is not HuePickerEntry entry )
        {
            return;
        }

        SelectedHue = entry.Index;
    }

    private void ApplyFilter( string value )
    {
        FilteredItems = new ObservableCollection<HuePickerEntry>( Items.Where( i =>
            string.IsNullOrEmpty( value ) || i.Index.ToString().StartsWith( value ) ) );
    }
}

public class HuePickerEntry
{
    public HueEntry Entry { get; set; }
    public string EntryName => Entry.Name ?? "Unknown";
    public int Index { get; set; }
}