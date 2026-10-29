using System;
using System.Collections;
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

    /// <summary>
    ///     Per-macro aliases. Mutated by the macro commands (<c>SetMacroAlias</c>/<c>UnsetAlias</c>)
    ///     on macro threads and hotkey dispatch while the UI thread walks it (the Active Objects
    ///     window) and profile save enumerates it - the same race that <c>AliasCommands._aliases</c>
    ///     had - so it is a <see cref="LockedDictionary{TKey,TValue}" /> rather than a plain
    ///     <see cref="Dictionary{TKey,TValue}" />. The type is still a
    ///     <see cref="Dictionary{TKey,TValue}" />, so every existing call site
    ///     (<c>AliasCommands</c>, <c>ActiveObjectsViewModel</c>, the tests) compiles unchanged and
    ///     binds the synchronised members; see <see cref="LockedDictionary{TKey,TValue}" /> for the
    ///     one caveat.
    /// </summary>
    public LockedDictionary<string, int> Aliases
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

/// <summary>
///     A <see cref="Dictionary{TKey,TValue}" /> whose operations are serialised on a private lock
///     and whose enumeration is snapshot-based - the dictionary counterpart of
///     <c>LockedObservableCollection&lt;T&gt;</c> (ClilocFilter.cs), introduced for the same
///     reason: macro threads write per-macro aliases through <c>SetMacroAlias</c>/<c>UnsetAlias</c>
///     while the UI thread (Active Objects) and profile save read them.
///     <para>
///         Unlike <c>LockedObservableCollection&lt;T&gt;</c>, the mutators of
///         <see cref="Dictionary{TKey,TValue}" /> are not virtual, so they are hidden with
///         <c>new</c> rather than overridden. That intercepts every call made through a
///         <see cref="LockedDictionary{TKey,TValue}" />-typed reference - which is what
///         <c>MacroEntry.Aliases</c> and all call sites in this repo use (verified by grep for
///         <c>\.Aliases</c>) - but a call through a <see cref="Dictionary{TKey,TValue}" />-typed
///         reference would reach the base implementation directly and is not synchronised.
///     </para>
///     <para>
///         <see cref="IEnumerable{T}" /> and <see cref="ICollection{T}" /> are re-implemented so
///         interface-dispatched use - LINQ, <c>ToList()</c>/<c>ToArray()</c>, and
///         <c>List&lt;T&gt;</c>'s constructor's <c>ICollection&lt;T&gt;</c> fast path - gets the
///         snapshot enumerator and a bounded <c>CopyTo</c>; the base's interface implementations
///         are sealed and cannot be overridden, so re-implementation is the only hook.
///         <see cref="Snapshot" /> is the thread-safe way to take a whole copy.
///     </para>
/// </summary>
public sealed class LockedDictionary<TKey, TValue> : Dictionary<TKey, TValue>,
    ICollection<KeyValuePair<TKey, TValue>>, IEnumerable<KeyValuePair<TKey, TValue>>, IEnumerable
    where TKey : notnull
{
    private readonly Lock _lock = new();

    public new int Count
    {
        get
        {
            lock ( _lock )
            {
                return base.Count;
            }
        }
    }

    public new TValue this[TKey key]
    {
        get
        {
            lock ( _lock )
            {
                return base[key];
            }
        }
        set
        {
            lock ( _lock )
            {
                base[key] = value;
            }
        }
    }

    public new ICollection<TKey> Keys
    {
        get
        {
            lock ( _lock )
            {
                return [.. base.Keys];
            }
        }
    }

    public new ICollection<TValue> Values
    {
        get
        {
            lock ( _lock )
            {
                return [.. base.Values];
            }
        }
    }

    public new void Add( TKey key, TValue value )
    {
        lock ( _lock )
        {
            base.Add( key, value );
        }
    }

    public new void Clear()
    {
        lock ( _lock )
        {
            base.Clear();
        }
    }

    public new bool ContainsKey( TKey key )
    {
        lock ( _lock )
        {
            return base.ContainsKey( key );
        }
    }

    public new IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        return Snapshot().GetEnumerator();
    }

    public new bool Remove( TKey key )
    {
        lock ( _lock )
        {
            return base.Remove( key );
        }
    }

    /// <summary>
    ///     A copy of the current contents, taken under the lock - the thread-safe way to walk or
    ///     persist the whole map while other threads mutate it.
    /// </summary>
    public Dictionary<TKey, TValue> Snapshot()
    {
        lock ( _lock )
        {
            Dictionary<TKey, TValue> copy = new( base.Count );

            foreach ( KeyValuePair<TKey, TValue> kvp in (Dictionary<TKey, TValue>) this )
            {
                copy.Add( kvp.Key, kvp.Value );
            }

            return copy;
        }
    }

    public new bool TryGetValue( TKey key, out TValue value )
    {
        lock ( _lock )
        {
            return base.TryGetValue( key, out value );
        }
    }

    void ICollection<KeyValuePair<TKey, TValue>>.Add( KeyValuePair<TKey, TValue> item )
    {
        Add( item.Key, item.Value );
    }

    void ICollection<KeyValuePair<TKey, TValue>>.Clear()
    {
        Clear();
    }

    bool ICollection<KeyValuePair<TKey, TValue>>.Contains( KeyValuePair<TKey, TValue> item )
    {
        lock ( _lock )
        {
            return base.TryGetValue( item.Key, out TValue value ) &&
                   EqualityComparer<TValue>.Default.Equals( value, item.Value );
        }
    }

    int ICollection<KeyValuePair<TKey, TValue>>.Count => Count;

    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo( KeyValuePair<TKey, TValue>[] array, int index )
    {
        lock ( _lock )
        {
            // Same bound as LockedObservableCollection.CopyTo: callers such as List<T>'s
            // constructor and ToArray() size their buffer from Count in a separate call, so the
            // map can grow before the copy runs - copy what fits instead of throwing, restarting
            // the very exception this type exists to stop.
            int count = Math.Min( base.Count, array.Length - index );
            int i = 0;

            foreach ( KeyValuePair<TKey, TValue> kvp in (Dictionary<TKey, TValue>) this )
            {
                if ( i >= count )
                {
                    break;
                }

                array[index + i] = kvp;
                i++;
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ( (IEnumerable<KeyValuePair<TKey, TValue>>) this ).GetEnumerator();
    }

    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
    {
        return Snapshot().GetEnumerator();
    }

    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

    bool ICollection<KeyValuePair<TKey, TValue>>.Remove( KeyValuePair<TKey, TValue> item )
    {
        lock ( _lock )
        {
            if ( base.TryGetValue( item.Key, out TValue value ) &&
                 EqualityComparer<TValue>.Default.Equals( value, item.Value ) )
            {
                return base.Remove( item.Key );
            }

            return false;
        }
    }
}