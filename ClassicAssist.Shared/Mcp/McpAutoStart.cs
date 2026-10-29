using System;
using System.IO;
using System.Net.Sockets;
using ClassicAssist.Data;
using ClassicAssist.Shared;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Mcp;

/// <summary>
///     Restarts the MCP server for a character that had it running last session. Upstream keeps
///     the MCP toggle session-only so several instances don't fight over one port; this keeps
///     that, but remembers the port per character in the profile ("McpAutoStart": {"Ports":
///     {"&lt;serial&gt;": port}}), and when that port is taken takes the next free one. Unticking
///     the option drops the character from the list.
///     <para>
///         Self-contained on purpose: it only hooks <see cref="Options.LoadEvent" />,
///         <see cref="Options.SaveEvent" /> and <see cref="Engine.PlayerInitializedEvent" />, so
///         upstream code stays untouched apart from the one call to <see cref="Initialize" />.
///     </para>
/// </summary>
public static class McpAutoStart
{
    private const string KEY = "McpAutoStart";
    private const int PORT_TRIES = 50;

    private static JObject _ports = [];
    private static int _serial;
    private static bool _initialized;

    /// <summary>
    ///     Call after the UI's own <see cref="Options.LoadEvent" /> handler is registered: the
    ///     options tab reflects the server state on load and this must run after it.
    /// </summary>
    public static void Initialize()
    {
        if ( _initialized )
        {
            return;
        }

        _initialized = true;

        Options.LoadEvent += OnOptionsLoad;
        Options.SaveEvent += OnOptionsSave;
        Engine.PlayerInitializedEvent += OnPlayerInitialized;
    }

    private static void OnOptionsLoad( JObject json, Options options )
    {
        _ports = json?[KEY]?["Ports"] as JObject ?? [];

        TryStart();
    }

    private static void OnPlayerInitialized( PlayerMobile player )
    {
        // Raised off the UI thread; the server and the bound options are only touched on it.
        Engine.Dispatcher.InvokeAsync( TryStart );
    }

    private static void TryStart()
    {
        PlayerMobile player = Engine.Player;

        if ( player == null )
        {
            return; // Again on PlayerInitializedEvent.
        }

        _serial = player.Serial;

        if ( McpServer.IsRunning || _ports[SerialKey( _serial )] is not { Type: JTokenType.Integer } token )
        {
            return;
        }

        int port = token.ToObject<int>();

        if ( port is < 1 or > 65535 )
        {
            return;
        }

        for ( int candidate = port; candidate < port + PORT_TRIES && candidate <= 65535; candidate++ )
        {
            try
            {
                // Binding a loopback listener is immediate: no wait on the UI thread.
                McpServer.Initialize( candidate );
            }
            catch ( SocketException e ) when ( e.SocketErrorCode == SocketError.AddressAlreadyInUse )
            {
                continue;
            }
            catch ( Exception )
            {
                return;
            }

            Options.CurrentOptions.McpPort = McpServer.Port;
            Options.CurrentOptions.McpEnabled = true;

            return;
        }
    }

    private static void OnOptionsSave( JObject obj )
    {
        int serial = Engine.Player?.Serial ?? _serial;

        // Several instances can share one profile file and each writes the whole of it: start from
        // the ports on disk so this save doesn't drop the other characters' entries.
        JObject ports = ReadPortsOnDisk( obj["Name"]?.ToObject<string>() ) ?? _ports;

        if ( serial != 0 )
        {
            if ( McpServer.IsRunning )
            {
                ports[SerialKey( serial )] = McpServer.Port;
            }
            else
            {
                ports.Remove( SerialKey( serial ) );
            }
        }

        _ports = ports;
        obj[KEY] = new JObject { ["Ports"] = ports };
    }

    private static JObject ReadPortsOnDisk( string name )
    {
        try
        {
            string directory = Path.IsPathRooted( AssistantOptions.ProfileDirectory )
                ? AssistantOptions.ProfileDirectory
                : Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory,
                    AssistantOptions.ProfileDirectory );

            string path = Path.Combine( directory, name ?? Options.DEFAULT_SETTINGS_FILENAME );

            if ( !File.Exists( path ) )
            {
                return null;
            }

            return JObject.Parse( File.ReadAllText( path ) )[KEY]?["Ports"] as JObject;
        }
        catch ( Exception )
        {
            return null;
        }
    }

    private static string SerialKey( int serial )
    {
        return $"0x{serial:X8}";
    }
}
