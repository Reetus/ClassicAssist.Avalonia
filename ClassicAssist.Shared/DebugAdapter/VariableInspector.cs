using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ClassicAssist.DebugAdapter.Dap;
using IronPython.Hosting;
using IronPython.Runtime;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using Microsoft.Scripting.Hosting;

namespace ClassicAssist.DebugAdapter;

public sealed class VariableInspector
{
    private static readonly HashSet<string> FilteredGlobals = new( StringComparer.Ordinal )
    {
        "__builtins__", "__name__", "__doc__", "__file__", "__package__"
    };

    // Maps frameId → (localsRef, globalsRef)
    private readonly ConcurrentDictionary<int, Tuple<int, int>> _frameRefs =
        new();

    // Maps variablesReference → the object to enumerate
    private readonly ConcurrentDictionary<int, object> _refMap = new();

    // Maps threadId → list of frames for stack trace
    private readonly ConcurrentDictionary<int, List<TraceBackFrame>> _threadFrames =
        new();

    // Reference counter for expandable variables. Starts above frame-based refs.
    private int _nextRef = 10000;

    public void StoreFrames( int threadId, TraceBackFrame topFrame )
    {
        ClearThread( threadId );

        List<TraceBackFrame> frames = [];
        TraceBackFrame frame = topFrame;

        while ( frame != null )
        {
            frames.Add( frame );

            try
            {
                frame = (TraceBackFrame) frame.f_back;
            }
            catch
            {
                break;
            }
        }

        _threadFrames[threadId] = frames;

        // Pre-allocate refs for each frame
        int baseFrameId = threadId * 1000;

        for ( int i = 0; i < frames.Count; i++ )
        {
            int frameId = baseFrameId + i;
            int localsRef = AllocRef( frames[i].f_locals );
            int globalsRef = AllocRef( frames[i].f_globals );
            _frameRefs[frameId] = Tuple.Create( localsRef, globalsRef );
        }
    }

    public int GetLocalsRef( int frameId )
    {
        return _frameRefs.TryGetValue( frameId, out Tuple<int, int> refs ) ? refs.Item1 : 0;
    }

    public int GetGlobalsRef( int frameId )
    {
        return _frameRefs.TryGetValue( frameId, out Tuple<int, int> refs ) ? refs.Item2 : 0;
    }

    public Tuple<string, string, int> Evaluate( int frameId, string expression )
    {
        // Find the frame for this frameId
        int threadId = frameId / 1000;
        int frameIndex = frameId % 1000;


        if ( !_threadFrames.TryGetValue( threadId, out List<TraceBackFrame> frames ) || frameIndex >= frames.Count )
        {
            throw new InvalidOperationException( "No frame available for evaluation" );
        }

        TraceBackFrame frame = frames[frameIndex];
        ScriptEngine engine = Python.CreateEngine();

        // Build a scope with the frame's locals and globals
        ScriptScope scope = engine.CreateScope();

        if ( frame.f_globals is PythonDictionary globals )
        {
            foreach ( KeyValuePair<object, object> kvp in globals )
            {
                if ( kvp.Key is string key )
                {
                    scope.SetVariable( key, kvp.Value );
                }
            }
        }

        if ( frame.f_locals is PythonDictionary locals )
        {
            foreach ( KeyValuePair<object, object> kvp in locals )
            {
                if ( kvp.Key is string key )
                {
                    scope.SetVariable( key, kvp.Value );
                }
            }
        }

        object result = engine.Execute( expression, scope );
        string repr = SafeRepr( result );
        string type = result?.GetType().Name;
        int childRef = 0;

        if ( IsExpandable( result ) )
        {
            childRef = AllocRef( result );
        }

        return Tuple.Create( repr, type, childRef );
    }

    public DapVariable[] GetVariables( int variablesReference )
    {

        if ( !_refMap.TryGetValue( variablesReference, out object obj ) || obj == null )
        {
            return [];
        }

        try
        {
            if ( obj is PythonDictionary pythonDict )
            {
                return EnumerateDict( pythonDict );
            }

            if ( obj is IDictionary dict )
            {
                return EnumerateDict( dict );
            }

            if ( obj is IList list )
            {
                return EnumerateList( list );
            }

            return
            [
                new DapVariable { Name = "(value)", Value = SafeRepr( obj ), Type = obj.GetType().Name }
            ];
        }
        catch ( Exception e )
        {
            return [new DapVariable { Name = "(error)", Value = e.Message }];
        }
    }

    private DapVariable[] EnumerateDict( PythonDictionary dict )
    {
        List<DapVariable> result = new( dict.Count );

        foreach ( KeyValuePair<object, object> kvp in dict )
        {
            string key = kvp.Key?.ToString() ?? "(null)";

            if ( FilteredGlobals.Contains( key ) )
            {
                continue;
            }

            // Skip module and function objects from display
            if ( kvp.Value is PythonModule or PythonFunction )
            {
                continue;
            }

            result.Add( MakeVariable( key, kvp.Value ) );
        }

        result.Sort( ( a, b ) => string.Compare( a.Name, b.Name, StringComparison.Ordinal ) );
        return [.. result];
    }

    private DapVariable[] EnumerateDict( IDictionary dict )
    {
        List<DapVariable> result = new( dict.Count );

        foreach ( DictionaryEntry entry in dict )
        {
            string key = entry.Key?.ToString() ?? "(null)";

            if ( FilteredGlobals.Contains( key ) )
            {
                continue;
            }

            result.Add( MakeVariable( key, entry.Value ) );
        }

        result.Sort( ( a, b ) => string.Compare( a.Name, b.Name, StringComparison.Ordinal ) );
        return [.. result];
    }

    private DapVariable[] EnumerateList( IList list )
    {
        DapVariable[] result = new DapVariable[list.Count];

        for ( int i = 0; i < list.Count; i++ )
        {
            result[i] = MakeVariable( $"[{i}]", list[i] );
        }

        return result;
    }

    private DapVariable MakeVariable( string name, object value )
    {
        int childRef = 0;

        if ( IsExpandable( value ) )
        {
            childRef = AllocRef( value );
        }

        return new DapVariable
        {
            Name = name,
            Value = SafeRepr( value ),
            Type = value?.GetType().Name ?? "None",
            VariablesReference = childRef
        };
    }

    private static bool IsExpandable( object value )
    {
        return value is PythonDictionary or IDictionary or IList or PythonTuple;
    }

    private int AllocRef( object value )
    {
        if ( value == null )
        {
            return 0;
        }

        int refId = Interlocked.Increment( ref _nextRef );
        _refMap[refId] = value;
        return refId;
    }

    private static string SafeRepr( object value )
    {
        if ( value == null )
        {
            return "None";
        }

        try
        {
            return PythonOps.Repr( DefaultContext.Default, value ) ?? value.ToString() ?? "";
        }
        catch
        {
            try
            {
                return value.ToString() ?? "(error)";
            }
            catch
            {
                return "(error)";
            }
        }
    }

    private void ClearThread( int threadId )
    {
        if ( _threadFrames.TryRemove( threadId, out _ ) )
        {
            // Clean up frame refs for this thread
            int baseFrameId = threadId * 1000;

            for ( int i = 0; i < 100; i++ )
            {
                int frameId = baseFrameId + i;

                if ( _frameRefs.TryRemove( frameId, out Tuple<int, int> refs ) )
                {
                    _refMap.TryRemove( refs.Item1, out _ );
                    _refMap.TryRemove( refs.Item2, out _ );
                }
            }
        }
    }

    public Tuple<string, string> SetVariable( int variablesReference, string name, string valueExpression )
    {

        if ( !_refMap.TryGetValue( variablesReference, out object container ) )
        {
            throw new InvalidOperationException( "Variable container not found" );
        }

        // Evaluate the new value expression
        ScriptEngine engine = Python.CreateEngine();
        ScriptScope scope = engine.CreateScope();

        // Provide existing variables as context for the expression
        if ( container is PythonDictionary contextDict )
        {
            foreach ( KeyValuePair<object, object> kvp in contextDict )
            {
                if ( kvp.Key is string key )
                {
                    scope.SetVariable( key, kvp.Value );
                }
            }
        }

        object newValue = engine.Execute( valueExpression, scope );

        // Write back into the container
        if ( container is PythonDictionary pd )
        {
            pd[name] = newValue;
        }
        else if ( container is IDictionary id )
        {
            id[name] = newValue;
        }
        else if ( container is IList list && name.StartsWith( "[" ) && name.EndsWith( "]" ) )
        {

            if ( int.TryParse( name[1..^1], out int idx ) )
            {
                list[idx] = newValue;
            }
            else
            {
                throw new InvalidOperationException( "Cannot set variable in this container" );
            }
        }
        else
        {
            throw new InvalidOperationException( "Cannot set variable in this container" );
        }

        return Tuple.Create( SafeRepr( newValue ), newValue?.GetType().Name );
    }

    public CompletionItem[] GetCompletions( int frameId, string text, int column )
    {
        // Extract the word being typed (everything after the last non-identifier char)
        string prefix = text[..Math.Min( Math.Max( column - 1, 0 ), text.Length )];
        int dotIndex = prefix.LastIndexOf( '.' );
        int wordStart = prefix.LastIndexOfAny( [' ', '(', '[', ',', '=', '+', '-', '*', '/', ':', '{'] );
        string word = prefix[( Math.Max( dotIndex, wordStart ) + 1 )..];

        int threadId = frameId / 1000;
        int frameIndex = frameId % 1000;


        if ( !_threadFrames.TryGetValue( threadId, out List<TraceBackFrame> frames ) || frameIndex >= frames.Count )
        {
            return [];
        }

        TraceBackFrame frame = frames[frameIndex];
        HashSet<string> names = new( StringComparer.Ordinal );

        Action<object> collectNames = dict =>
        {
            if ( dict is not PythonDictionary pd )
            {
                return;
            }

            foreach ( KeyValuePair<object, object> kvp in pd )
            {
                if ( kvp.Key is string key && !FilteredGlobals.Contains( key ) &&
                     key.StartsWith( word, StringComparison.OrdinalIgnoreCase ) &&
                     kvp.Value is not PythonModule && kvp.Value is not PythonFunction )
                {
                    names.Add( key );
                }
            }
        };

        collectNames( frame.f_locals );
        collectNames( frame.f_globals );

        return [.. names.OrderBy( n => n ).Select( n => new CompletionItem { Label = n, Type = "variable" } )];
    }

    public void Clear()
    {
        _refMap.Clear();
        _frameRefs.Clear();
        _threadFrames.Clear();
    }
}
