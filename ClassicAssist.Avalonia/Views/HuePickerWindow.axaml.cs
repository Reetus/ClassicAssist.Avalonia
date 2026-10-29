using System.Collections.Generic;
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
    private PropertyChangedEventHandler _propertyChanged;

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

    /// <summary>
    ///     The hue the user accepted, or null while the dialog has not been accepted - a genuine
    ///     hue 0 and "no choice made" must stay distinguishable to callers writing settings.
    /// </summary>
    public int? SelectedHue
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

    // INotifyPropertyChanged is re-implemented explicitly: a plain `new event` here would hide
    // AvaloniaObject.PropertyChanged (an EventHandler<AvaloniaPropertyChangedEventArgs>) and leave
    // two subtly different PropertyChanged events alive on one object.
    event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
    {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
    {
        _propertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
    }

    // ReSharper disable once RedundantAssignment
    public virtual void SetProperty<T>( ref T obj, T value, [CallerMemberName] string propertyName = "" )
    {
        if ( EqualityComparer<T>.Default.Equals( obj, value ) )
        {
            return;
        }

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