using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Network;

namespace ClassicAssist.Shared.UI.ViewModels.Debug;

public class DebugJournalViewModel : BaseViewModel
{
    public ICommand ClearCommand => field ??= new RelayCommand( Clear, o => true );

    public ICommand CopyCommand => field ??= new RelayCommand( Copy, o => o != null );

    /// <summary>
    ///     Off by default, as in the WPF build: capture only starts when the box is ticked, so the tab
    ///     costs nothing while the Debug Window is merely open on another tab.
    /// </summary>
    public bool Enabled
    {
        get;
        set
        {
            if ( value != field )
            {
                if ( value )
                {
                    SetEnabled();
                }
                else
                {
                    SetDisabled();
                }
            }

            SetProperty( ref field, value );
        }
    }

    public ObservableCollection<string> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public string SelectedItem
    {
        get;
        set => SetProperty( ref field, value );
    }

    private void SetEnabled()
    {
        // Cleared first, unlike WPF, which appends the buffer every time it is enabled and so shows
        // every entry twice after a tick-untick-tick.
        Items.Clear();

        foreach ( JournalEntry journalEntry in Engine.Journal.GetEntireBuffer() )
        {
            Items.Add( GetString( journalEntry ) );
        }

        IncomingPacketHandlers.JournalEntryAddedEvent += OnJournalEntryAddedEvent;
    }

    private void SetDisabled()
    {
        IncomingPacketHandlers.JournalEntryAddedEvent -= OnJournalEntryAddedEvent;
    }

    private static void Copy( object obj )
    {
        if ( obj is not string text )
        {
            return;
        }

        Engine.UIInvoker?.SetClipboardText( text );
    }

    private void Clear( object obj )
    {
        Items.Clear();
    }

    private static string GetString( JournalEntry journalEntry )
    {
        StringBuilder sb = new();

        sb.AppendLine( $"Name: {journalEntry.Name}" );
        sb.AppendLine( $"Serial: 0x{journalEntry.Serial:x8}" );
        sb.AppendLine( $"ID: 0x{journalEntry.ID:x4}" );
        sb.AppendLine( $"Cliloc: {journalEntry.Cliloc}" );
        sb.AppendLine( $"Text: {journalEntry.Text}" );
        sb.AppendLine( $"Arguments: {string.Join( ",", journalEntry.Arguments ?? [] )}" );
        sb.AppendLine( $"Language: {journalEntry.SpeechLanguage}" );
        sb.AppendLine( $"Type: {journalEntry.SpeechType}" );

        return sb.ToString();
    }

    private void OnJournalEntryAddedEvent( JournalEntry je )
    {
        _dispatcher.Invoke( () => { Items.Add( GetString( je ) ); } );
    }
}