using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO.Data;
using IronPython.Runtime.Operations;
using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Data.Macros;

public class MacroEntry : HotkeyEntry, IComparable<MacroEntry>
{
    //private readonly Dispatcher _dispatcher;
    private string _name;

    public MacroEntry( JToken token = null )
    {
        //TODO
        //_dispatcher = Dispatcher.CurrentDispatcher;
        MacroInvoker.ExceptionEvent += OnExceptionEvent;
        MacroInvoker.StoppedEvent += OnStoppedEvent;
        MacroInvoker.PausedEvent += OnPausedEvent;

        if ( token == null )
        {
            return;
        }

        Id = GetJsonValue<string>( token, "Id", null );
        Name = GetJsonValue( token, "Name", string.Empty );
        Loop = GetJsonValue( token, "Loop", false );
        DoNotAutoInterrupt = GetJsonValue( token, "DoNotAutoInterrupt", false );
        FilePath = GetJsonValue<string>( token, "FilePath", null );

        string embeddedMacro = GetJsonValue( token, "Macro", string.Empty );

        if ( IsFileBacked && !string.IsNullOrEmpty( embeddedMacro ) )
        {
            // The last save couldn't write the backing file and embedded the newer content in
            // the profile instead - prefer it and re-attempt the file write on the next save.
            Macro = embeddedMacro;
            BackingFileWritePending = true;
        }
        else if ( IsFileBacked && File.Exists( FilePath ) )
        {
            try
            {
                Macro = File.ReadAllText( FilePath );
            }
            catch
            {
                // Unreadable backing file - load the entry without content rather than failing
                // the whole profile; the folder scan will reload it once readable.
                Macro = string.Empty;
                BackingFileReadFailed = true;
            }
        }
        else
        {
            Macro = embeddedMacro;
        }

        PassToUO = GetJsonValue( token, "PassToUO", true );
        IsBackground = GetJsonValue( token, "IsBackground", false );
        IsAutostart = GetJsonValue( token, "IsAutostart", false );
        Disableable = GetJsonValue( token, "Disableable", true );
        Global = GetJsonValue( token, "Global", false );
        Breakpoints = GetJsonValue( token, "Breakpoints", new ObservableCollection<int>() );

        /* Keys aren't done here, because of logic global vs normal */

        if ( token["Metadata"] != null )
        {
            foreach ( JToken metadataToken in token["Metadata"] )
            {
                string metadataKey = metadataToken["Key"]?.ToObject<string>() ?? string.Empty;
                string metadataValue = metadataToken["Value"]?.ToObject<string>() ?? string.Empty;

                if ( !string.IsNullOrEmpty( metadataKey ) )
                {
                    Metadata[metadataKey] = metadataValue;
                }
            }
        }

        if ( token["Aliases"] == null )
        {
            return;
        }

        foreach ( JToken aliasToken in token["Aliases"] )
        {
            if ( aliasToken.Type == JTokenType.Property )
            {
                JProperty jProperty = (JProperty) aliasToken;

                Aliases.Add( jProperty.Name, jProperty.Value.ToObject<int>() );
            }
            else
            {
                string key = aliasToken["Key"]?.ToObject<string>() ?? string.Empty;
                int value = aliasToken["Value"]?.ToObject<int>() ?? 0;

                if ( string.IsNullOrEmpty( key ) )
                {
                    continue;
                }

                Aliases.Add( key, value );
            }
        }
    }

    public Dictionary<string, int> Aliases
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    [JsonIgnore]
    public AutoResetEvent AutoResetEvent
    {
        get;
        set => SetProperty( ref field, value );
    }

    /// <summary>
    ///     Line numbers the debugger should pause execution on. Not yet surfaced by any editor UI -
    ///     nothing currently populates this, so it has no effect until a breakpoint-toggle UI exists.
    /// </summary>
    public ObservableCollection<int> Breakpoints
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public bool DoNotAutoInterrupt
    {
        get;
        set => SetProperty( ref field, value );
    }

    /// <summary>
    ///     Free-form key/value data carried with the macro. Upstream's Public Macros browser stamps
    ///     <c>PublicId</c>/<c>PublicSHA1</c> here to track which published macro an entry came from and
    ///     whether it has been edited since. That browser isn't ported, so nothing writes this yet -
    ///     it round-trips so a profile shared with the WPF build doesn't lose the link.
    /// </summary>
    public Dictionary<string, string> Metadata
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public string FilePath
    {
        get;
        set
        {
            SetProperty( ref field, value );
            OnPropertyChanged( nameof( IsFileBacked ) );
        }
    }

    public bool IsFileBacked => !string.IsNullOrEmpty( FilePath );

    /// <summary>
    ///     The backing file couldn't be read when the profile loaded; saves must not write
    ///     (empty) content over it until the folder scan has reloaded it.
    /// </summary>
    public bool BackingFileReadFailed { get; set; }

    /// <summary>
    ///     The in-memory content is newer than the backing file (the last file write failed and
    ///     the content was embedded in the profile instead); the folder scan must not reload
    ///     over it and the next save should retry the file write.
    /// </summary>
    public bool BackingFileWritePending { get; set; }

    [JsonIgnore]
    public Dictionary<string, object> FrameVariables
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool Global
    {
        get;
        set => SetProperty( ref field, value );
    }

    [JsonIgnore]
    public string Hash => Macro.SHA1();

    public string Id
    {
        get => field ??= Guid.NewGuid().ToString();
        set => SetProperty( ref field, value );
    }

    public bool IsAutostart
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool IsBackground
    {
        get;
        set => SetProperty( ref field, value );
    }

    [JsonIgnore]
    public bool IsPaused
    {
        get;
        set => SetProperty( ref field, value );
    }

    [JsonIgnore]
    public bool IsRunning
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool Loop
    {
        get;
        set => SetProperty( ref field, value );
    }

    public DateTime StartedOn { get; set; }

    public string Macro
    {
        get;
        set => SetProperty( ref field, value );
    } = string.Empty;

    [JsonIgnore]
    public MacroInvoker MacroInvoker
    {
        get;
        set => SetProperty( ref field, value );
    } = new MacroInvoker();

    public override string Name
    {
        get => _name;
        set => SetName( _name, value );
    }

    [JsonIgnore]
    public int PausedLineNumber
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int CompareTo( MacroEntry other )
    {
        return string.Compare( Name, other.Name, StringComparison.OrdinalIgnoreCase );
    }

    private void OnPausedEvent( int lineNumber, AutoResetEvent autoResetEvent, Dictionary<string, object> frameVariables )
    {
        Shared.UO.Commands.SystemMessage( string.Format( Strings.Debugger_paused_on_line__0_, lineNumber ), SystemMessageHues.Yellow );

        Engine.Dispatcher.Invoke( () =>
        {
            IsPaused = true;
            PausedLineNumber = lineNumber;
            AutoResetEvent = autoResetEvent;
            FrameVariables = frameVariables;
        } );
    }

    private void OnStoppedEvent()
    {
        bool wasPaused = IsPaused;

        if ( wasPaused )
        {
            AutoResetEvent?.Set();
        }

        Engine.Dispatcher.Invoke( () =>
        {
            IsRunning = false;

            if ( wasPaused )
            {
                IsPaused = false;
            }
        } );

        if ( IsBackground && !MacroManager.QuietMode )
        {
            Shared.UO.Commands.SystemMessage( string.Format( Strings.Background_macro___0___stopped___, Name ) );
        }
    }

    public override string ToString()
    {
        return Name;
    }

    private void SetName( string name, string value )
    {
        MacroManager manager = MacroManager.GetInstance();

        bool exists = manager.Items.Any( m => m.Name == value && !ReferenceEquals( m, this ) );

        if ( exists && name == null )
        {
            SetName( null, $"{value}-" );
            return;
        }

        if ( exists )
        {
            //TODO
            //MessageBox.Show( Strings.Macro_name_must_be_unique_, Strings.Error );
            return;
        }

        SetProperty( ref _name, value );
    }

    public void Execute( object[] parameters = null )
    {
        Engine.Dispatcher.Invoke( () => IsRunning = true );

        if ( IsBackground && !MacroManager.QuietMode )
        {
            Shared.UO.Commands.SystemMessage( string.Format( Strings.Background_macro___0___started___, Name ) );
        }

        StartedOn = DateTime.Now;
        MacroInvoker.Execute( this, parameters );
    }

    public void Stop()
    {
        if ( IsRunning )
        {
            MacroInvoker.Stop();
            Engine.Dispatcher.Invoke( () => IsRunning = false );
        }
    }

    private static void OnExceptionEvent( Exception exception )
    {
        Shared.UO.Commands.SystemMessage( string.Format( Strings.Macro_error___0_, exception.Message ) );

        if ( exception is SyntaxErrorException syntaxError )
        {
            Shared.UO.Commands.SystemMessage( $"{Strings.Line_Number}: {syntaxError.RawSpan.Start.Line}" );
        }
        else
        {
            DynamicStackFrame sf = PythonOps.GetDynamicStackFrames( exception ).FirstOrDefault();

            if ( sf != null )
            {
                Shared.UO.Commands.SystemMessage( $"{Strings.Line_Number}: {sf.GetFileLineNumber()}" );
            }
        }
    }

    private static T2 GetJsonValue<T2>( JToken json, string name, T2 defaultValue )
    {
        if ( json == null )
        {
            return defaultValue;
        }

        return json[name] == null ? defaultValue : json[name].ToObject<T2>();
    }

    public JObject ToJObject()
    {
        JObject entry = new()
        {
            { "Id", Id },
            { "Name", Name },
            { "Loop", Loop },
            { "DoNotAutoInterrupt", DoNotAutoInterrupt },
            // File-backed macros keep their content in the .py file, not the profile, unless
            // the backing file couldn't be written - then embed the content so it isn't lost.
            { "Macro", IsFileBacked && !BackingFileWritePending ? string.Empty : Macro },
            { "PassToUO", PassToUO },
            { "Keys", Hotkey.ToJObject() },
            { "IsBackground", IsBackground },
            { "IsAutostart", IsAutostart },
            { "Disableable", Disableable },
            { "Global", Global },
            { "LastSavedHash", Hash }
        };

        if ( IsFileBacked )
        {
            entry.Add( "FilePath", FilePath );
        }

        if ( Metadata?.Count > 0 )
        {
            JArray metadataArray =
            [
                .. Metadata.Select( kvp =>
                    new JObject { { "Key", kvp.Key }, { "Value", kvp.Value } } ),
            ];

            entry.Add( "Metadata", metadataArray );
        }

        if ( !Global )
        {
            JArray aliasesArray =
            [
                .. Aliases.Select( kvp =>
                    new JObject { { "Key", kvp.Key }, { "Value", kvp.Value } } ),
            ];

            entry.Add( "Aliases", aliasesArray );
        }
        else
        {
            /*
             * Write global macro aliases as properties for backwards compatibility (for now)
             */
            JObject aliases = [];

            foreach ( KeyValuePair<string, int> keyValuePair in Aliases )
            {
                aliases.Add( keyValuePair.Key, keyValuePair.Value );
            }

            entry.Add( "Aliases", aliases );
        }

        if ( Breakpoints != null )
        {
            JArray breakpointsArray = [.. Breakpoints];

            entry.Add( "Breakpoints", breakpointsArray );
        }

        return entry;
    }

    public void Resume()
    {
        AutoResetEvent?.Set();

        Engine.Dispatcher.Invoke( () => IsPaused = false );
    }

    public void Step()
    {
        AutoResetEvent?.Set();
    }
}