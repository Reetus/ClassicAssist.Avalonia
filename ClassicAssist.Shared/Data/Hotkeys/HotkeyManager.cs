using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ClassicAssist.Data.Hotkeys.Commands;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Misc;
using ClassicAssist.UI.Misc;

namespace ClassicAssist.Data.Hotkeys;

public class HotkeyManager : INotifyPropertyChanged
{
    private static HotkeyManager _instance;
    private static readonly Lock _instanceLock = new();
    private readonly Lock _lock = new();

    private readonly Key[] _modifierKeys =
    [
        Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LeftAlt, Key.RightAlt
    ];

    private HotkeyManager()
    {
    }

    public Action ClearAllHotkeys { get; set; }

    public delegate void dHotkeysStatus( bool enabled );

    public bool Enabled
    {
        get;
        set
        {
            if ( field != value )
            {
                HotkeysStatusChanged?.Invoke( value );
            }

            SetProperty( ref field, value );
        }
    } = true;

    public ObservableCollectionEx<HotkeyCommand> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public static event dHotkeysStatus HotkeysStatusChanged;

    public event PropertyChangedEventHandler PropertyChanged;

    public void AddCategory( HotkeyCommand item, IComparer<HotkeyEntry> comparer = null )
    {
        if ( Items.Contains( item ) )
        {
            Items.Remove( item );
        }

        comparer ??= Comparer<HotkeyEntry>.Default;

        int i = 0;

        while ( i < Items.Count && comparer.Compare( Items[i], item ) < 0 )
        {
            i++;
        }

        Items.Insert( i, item );
    }

    public void ClearPreviousHotkey( ShortcutKeys keys )
    {
        foreach ( HotkeyCommand hotkeyEntry in Items )
        {
            if ( hotkeyEntry.Children == null )
            {
                continue;
            }

            foreach ( HotkeyEntry hotkeyEntryChild in hotkeyEntry.Children )
            {
                if ( Equals( hotkeyEntryChild.Hotkey, keys ) )
                {
                    hotkeyEntryChild.Hotkey = ShortcutKeys.Default;
                }
            }
        }
    }

    public static HotkeyManager GetInstance()
    {
        // ReSharper disable once InvertIf
        if ( _instance == null )
        {
            lock ( _instanceLock )
            {
                _instance ??= new HotkeyManager();
            }
        }

        return _instance;
    }

    protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
    {
        PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
    }

    // ReSharper disable once RedundantAssignment
    public void SetProperty<T>( ref T obj, T value, [CallerMemberName] string propertyName = "" )
    {
        obj = value;
        OnPropertyChanged( propertyName );
    }

    /// <summary>
    ///     Looks up and runs the hotkey bound to <paramref name="keys" />/<paramref name="modifier" />.
    /// </summary>
    /// <param name="noexecute">
    ///     Match the hotkey and report it, but don't run its action - used by the
    ///     <see cref="Options.LimitHotkeyTrigger" /> throttle, which still has to swallow the key from
    ///     the client (a bound key must not leak through to UO just because it retriggered too fast).
    /// </param>
    /// <returns>
    ///     <c>found</c>: a hotkey matched. <c>filter</c>: the key should be withheld from the client.
    /// </returns>
    public (bool found, bool filter) OnHotkeyPressed( Key keys, Key modifier, bool noexecute = false )
    {
        lock ( _lock )
        {
            bool filter = false;
            bool found = false;

            // Sanity check / modifier-only press, nothing to look up
            if ( keys == Key.None || _modifierKeys.Contains( keys ) )
            {
                return (false, false);
            }

            foreach ( HotkeyCommand hke in Items )
            {
                if ( hke.Children == null )
                {
                    continue;
                }

                try
                {
                    IEnumerable<HotkeyEntry> hotkeyEntries = hke.Children.Where( t =>
                        t.Hotkey.Modifier == modifier && t.Hotkey.Key == keys &&
                        t.Hotkey.Mouse == MouseOptions.None );

                    foreach ( HotkeyEntry hks in hotkeyEntries )
                    {
                        if ( hks.Disableable && !Enabled )
                        {
                            continue;
                        }

                        filter = !hks.PassToUO;
                        found = true;

                        if ( !noexecute )
                        {
                            AliasCommands.SetDefaultAliases();

                            Task.Run( () => hks.Action.Invoke( hks, null ) );
                        }

                        break;
                    }
                }
                catch ( InvalidOperationException )
                {
                    // When spamming keys
                }
            }

            return (found, filter);
        }
    }

    public void OnMouseAction( MouseOptions mouse )
    {
        // Sanity check
        if ( mouse == MouseOptions.None )
        {
            return;
        }

        lock ( _lock )
        {
            foreach ( HotkeyCommand hke in Items )
            {
                if ( hke.Children == null )
                {
                    continue;
                }

                try
                {
                    Key modifier = Key.None;
                    //Key modifier = _modifierKeys.FirstOrDefault( key =>
                    //    Engine.Dispatcher.Invoke( () => Keyboard.IsKeyDown( key ) ) );

                    IEnumerable<HotkeyEntry> hotkeyEntries = hke.Children.Where( t =>
                        t.Hotkey.Modifier == modifier && t.Hotkey.Key == Key.None && t.Hotkey.Mouse == mouse );

                    foreach ( HotkeyEntry hks in hotkeyEntries )
                    {
                        if ( hks.Disableable && !Enabled )
                        {
                            continue;
                        }

                        AliasCommands.SetDefaultAliases();

                        Task.Run( () => hks.Action.Invoke( hks, null ) );

                        break;
                    }
                }
                catch ( InvalidOperationException )
                {
                    // When spamming wheel
                }
            }
        }
    }

    public void ClearItems()
    {
        foreach ( HotkeyEntry entry in Items )
        {
            ClearHotkeys( entry );
        }

        Items.Clear();
    }

    private void ClearHotkeys( HotkeyEntry entry )
    {
        entry.Hotkey = ShortcutKeys.Default;

        if ( !entry.IsCategory )
        {
            return;
        }

        foreach ( HotkeyEntry hotkeyEntry in entry.Children )
        {
            ClearHotkeys( hotkeyEntry );
        }
    }
}