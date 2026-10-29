using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using ClassicAssist.Data;
using ClassicAssist.Shared;
using ClassicAssist.UO.Objects;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Mcp;

/// <summary>
///     Restarts the MCP server across sessions. Upstream keeps the MCP toggle session-only so
///     several instances don't fight over one port; this keeps that, but remembers the port per
///     character in the profile ("McpAutoStart": {"Ports": {"&lt;serial&gt;": port}}).
///     <list type="bullet">
///         <item>
///             Before any character is logged in (login screen, server list) the server starts on
///             the first free port from the lowest one the profile has saved, so it is reachable
///             from the start.
///         </item>
///         <item>
///             When a character logs in, a server started that way moves to the character's own
///             port if that port is free (connected MCP clients reconnect), or is stopped if the
///             character has no entry. A server started by hand is left alone.
///         </item>
///         <item>
///             With no server running, the character's port is used, or the next free one when it
///             is taken.
///         </item>
///     </list>
///     Unticking the option drops the character from the list.
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

    // The running server was started here (not by the checkbox): only that one is moved or stopped.
    private static bool _startedHere;

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

        Apply();
    }

    private static void OnPlayerInitialized( PlayerMobile player )
    {
        // Raised off the UI thread; the server and the bound options are only touched on it.
        Engine.Dispatcher.InvokeAsync( Apply );
    }

    private static void Apply()
    {
        if ( !McpServer.IsRunning )
        {
            _startedHere = false;
        }

        PlayerMobile player = Engine.Player;

        if ( player == null )
        {
            // No character yet: any port this profile has used, so the server is up from the start.
            if ( !McpServer.IsRunning && LowestSavedPort() is { } lowest )
            {
                Start( lowest );
            }

            return;
        }

        _serial = player.Serial;

        int? wanted = SavedPort( _serial );

        if ( !McpServer.IsRunning )
        {
            if ( wanted != null )
            {
                Start( wanted.Value );
            }

            return;
        }

        if ( !_startedHere )
        {
            return;
        }

        if ( wanted == null )
        {
            // Started before the character was known, and this character doesn't use it.
            McpServer.Shutdown();
            _startedHere = false;
            Options.CurrentOptions.McpEnabled = false;
        }
        else if ( McpServer.Port != wanted.Value && IsFree( wanted.Value ) )
        {
            McpServer.Shutdown();
            Start( wanted.Value );
        }
    }

    private static void Start( int port )
    {
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
                break;
            }

            _startedHere = true;
            Options.CurrentOptions.McpPort = McpServer.Port;
            Options.CurrentOptions.McpEnabled = true;

            return;
        }

        Options.CurrentOptions.McpEnabled = false;
    }

    private static bool IsFree( int port )
    {
        try
        {
            TcpListener listener = new( IPAddress.Loopback, port );

            listener.Start();
            listener.Stop();

            return true;
        }
        catch ( SocketException )
        {
            return false;
        }
    }

    private static int? SavedPort( int serial )
    {
        return _ports[SerialKey( serial )] is { Type: JTokenType.Integer } token && ValidPort( token.ToObject<int>() )
            ? token.ToObject<int>()
            : null;
    }

    private static int? LowestSavedPort()
    {
        int? lowest = null;

        foreach ( JProperty property in _ports.Properties() )
        {
            if ( property.Value is { Type: JTokenType.Integer } token && ValidPort( token.ToObject<int>() ) )
            {
                int port = token.ToObject<int>();

                lowest = lowest == null ? port : Math.Min( lowest.Value, port );
            }
        }

        return lowest;
    }

    private static bool ValidPort( int port )
    {
        return port is >= 1 and <= 65535;
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
