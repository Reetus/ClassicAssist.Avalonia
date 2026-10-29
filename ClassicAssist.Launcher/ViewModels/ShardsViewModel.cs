using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Launcher.Models;

namespace ClassicAssist.Launcher.ViewModels;

public class ShardsViewModel : BaseViewModel
{
    public ShardsViewModel()
    {
        Refresh( this );
    }

    public ICommand AddCommand => field ??= new RelayCommand( Add, o => true );

    public ICommand CancelCommand => field ??= new RelayCommand( Cancel, o => true );

    public bool DialogResult { get; set; }

    /// <summary>Raised by OK/Cancel once DialogResult is set - the window closes in response,
    /// never the other way around, so callers awaiting ShowDialog never race the close against
    /// DialogResult still being set (see ShardsWindow.axaml.cs).</summary>
    public event Action CloseRequested;

    public bool IsRefreshing
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand OKCommand => field ??= new RelayCommand( OK, o => true );

    public ICommand OpenWebsiteCommand => field ??= new RelayCommand( OpenWebsite, o => true );

    public ICommand RefreshCommand => field ??= new RelayCommand( Refresh, o => !IsRefreshing );

    public ICommand RemoveCommand => field ??= new RelayCommand( Remove, o => SelectedShard != null );

    public ShardEntry SelectedShard
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ShardManager ShardManager => ShardManager.GetInstance();

    private static void OpenWebsite( object obj )
    {
        if ( obj is not ShardEntry shardEntry )
        {
            return;
        }

        ProcessStartInfo psi = new() { FileName = shardEntry.Website, UseShellExecute = true };

        // Disposing the Process only releases our handle to it - the launched browser keeps
        // running (and Process.Start can legitimately return null for shell-executed URLs).
        Process.Start( psi )?.Dispose();
    }

    private void Remove( object obj )
    {
        if ( obj is not ShardEntry entry )
        {
            return;
        }

        if ( entry.IsPreset )
        {
            entry.Deleted = true;
            return;
        }

        ShardManager.Shards.Remove( entry );
    }

    private void Add( object obj )
    {
        ShardManager.Shards.Add( new ShardEntry { Name = "Shard Name", Address = "localhost", Port = 2593 } );
    }

    private void Refresh( object obj )
    {
        try
        {
            foreach ( ShardEntry shard in ShardManager.Shards )
            {
                Task.Run( async () =>
                {
                    if ( !shard.HasStatusProtocol )
                    {
                        return "-";
                    }

                    string status = await GetStatus( shard );

                    return status;
                } ).ContinueWith( t =>
                {
                    if ( !string.IsNullOrEmpty( shard.StatusRegex ) )
                    {
                        Match matches = Regex.Match( t.Result, shard.StatusRegex );

                        shard.Status = matches.Success ? matches.Groups[1].Value : "-";
                    }
                    else
                    {
                        shard.Status = t.Result;
                    }

                    NotifyPropertyChanged( nameof( ShardManager.Shards ) );
                } );

                Task.Run( async () => await GetPing( shard ) ).ContinueWith( result =>
                {
                    if ( result.Status != TaskStatus.RanToCompletion || result.Result == null )
                    {
                        return;
                    }

                    shard.Ping = result.Result;
                    NotifyPropertyChanged( nameof( ShardManager.Shards ) );
                } );
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void OK( object obj )
    {
        DialogResult = true;
        CloseRequested?.Invoke();
    }

    public async Task<string> GetStatus( ShardEntry shard )
    {
        if ( !shard.HasStatusProtocol )
        {
            return "Unknown";
        }

        using TcpClient client = new();

        Task connectTask = client.ConnectAsync( shard.Address, shard.Port );

        await Task.WhenAny( connectTask, Task.Delay( TimeSpan.FromSeconds( 5 ) ) );

        if ( !client.Connected )
        {
            return "Unknown";
        }

        byte[] packet = [0x7F, 0x00, 0x00, 0x7F, 0xF1, 0x00, 0x04, 0xFF];
        client.Client.Send( packet );

        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[256];

        // NetworkStream.ReadTimeout only bounds the synchronous Read - the previous
        // stream.ReadTimeout = 60000 never applied to this await, so a server that accepted and
        // then went silent hung this status task until TCP gave up. Bound the read with
        // Task.WhenAny the same way the connect above is bounded, keeping the original 60 s budget.
        Task<int> readTask = stream.ReadAsync( buffer, 0, buffer.Length );

        await Task.WhenAny( readTask, Task.Delay( TimeSpan.FromSeconds( 60 ) ) );

        // Same policy as the connect guard above: never await a task that may have faulted - a
        // timed out, reset or refused read is just "no status" (disposing the client when this
        // method returns also releases the abandoned read).
        if ( readTask.Status != TaskStatus.RanToCompletion )
        {
            return "Unknown";
        }

        int read = readTask.Result;

        return Encoding.ASCII.GetString( buffer, 0, read ).TrimEnd( '\0' );
    }

    public async Task<string> GetPing( ShardEntry entry )
    {
        IPAddress ip = await Utility.ResolveAddress( entry.Address );

        if ( ip == null )
        {
            return "-";
        }

        // Ping is a Component (IDisposable) - the send has completed by this point, and disposing
        // only releases its handles, never the reply already held in result.
        using Ping ping = new();

        PingReply result = ping.Send( ip.ToString() );

        return result?.Status == IPStatus.Success ? result.RoundtripTime.ToString() : "-";
    }

    private void Cancel( object obj )
    {
        DialogResult = false;
        CloseRequested?.Invoke();
    }
}
