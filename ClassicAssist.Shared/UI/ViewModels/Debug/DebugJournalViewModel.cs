using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Network;

namespace ClassicAssist.Shared.UI.ViewModels.Debug;

public class DebugJournalViewModel : BaseViewModel
{
    public DebugJournalViewModel()
    {
        JournalEntry[] buffer = Engine.Journal.GetEntireBuffer();

        foreach ( JournalEntry journalEntry in buffer )
        {
            Items.Add( GetString( journalEntry ) );
        }

        IncomingPacketHandlers.JournalEntryAddedEvent += OnJournalEntryAddedEvent;
    }

    public ICommand ClearCommand => field ??= new RelayCommand( Clear, o => true );

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