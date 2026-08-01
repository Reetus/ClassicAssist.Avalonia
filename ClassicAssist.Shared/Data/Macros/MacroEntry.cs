using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

namespace ClassicAssist.Data.Macros
{
    public class MacroEntry : HotkeyEntry, IComparable<MacroEntry>
    {
        //private readonly Dispatcher _dispatcher;
        private Dictionary<string, int> _aliases = new Dictionary<string, int>();
        private AutoResetEvent _autoResetEvent;
        private ObservableCollection<int> _breakpoints = new ObservableCollection<int>();
        private bool _doNotAutoInterrupt;
        private Dictionary<string, object> _frameVariables;
        private bool _global;
        private bool _isAutostart;
        private bool _isBackground;
        private bool _isPaused;
        private bool _isRunning;
        private bool _loop;
        private string _macro = string.Empty;
        private MacroInvoker _macroInvoker = new MacroInvoker();
        private string _name;
        private int _pausedLineNumber;

        public MacroEntry()
        {
            //TODO
            //_dispatcher = Dispatcher.CurrentDispatcher;
            _macroInvoker.ExceptionEvent += OnExceptionEvent;
            _macroInvoker.StoppedEvent += OnStoppedEvent;
            _macroInvoker.PausedEvent += OnPausedEvent;
        }

        public Dictionary<string, int> Aliases
        {
            get => _aliases;
            set => SetProperty( ref _aliases, value );
        }

        [JsonIgnore]
        public AutoResetEvent AutoResetEvent
        {
            get => _autoResetEvent;
            set => SetProperty( ref _autoResetEvent, value );
        }

        /// <summary>
        ///     Line numbers the debugger should pause execution on. Not yet surfaced by any editor UI -
        ///     nothing currently populates this, so it has no effect until a breakpoint-toggle UI exists.
        /// </summary>
        public ObservableCollection<int> Breakpoints
        {
            get => _breakpoints;
            set => SetProperty( ref _breakpoints, value );
        }

        public bool DoNotAutoInterrupt
        {
            get => _doNotAutoInterrupt;
            set => SetProperty( ref _doNotAutoInterrupt, value );
        }

        [JsonIgnore]
        public Dictionary<string, object> FrameVariables
        {
            get => _frameVariables;
            set => SetProperty( ref _frameVariables, value );
        }

        public bool Global
        {
            get => _global;
            set => SetProperty( ref _global, value );
        }

        [JsonIgnore]
        public string Hash => _macro.SHA1();

        public bool IsAutostart
        {
            get => _isAutostart;
            set => SetProperty( ref _isAutostart, value );
        }

        public bool IsBackground
        {
            get => _isBackground;
            set => SetProperty( ref _isBackground, value );
        }

        [JsonIgnore]
        public bool IsPaused
        {
            get => _isPaused;
            set => SetProperty( ref _isPaused, value );
        }

        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty( ref _isRunning, value );
        }

        public bool Loop
        {
            get => _loop;
            set => SetProperty( ref _loop, value );
        }

        public string Macro
        {
            get => _macro;
            set => SetProperty( ref _macro, value );
        }

        [JsonIgnore]
        public MacroInvoker MacroInvoker
        {
            get => _macroInvoker;
            set => SetProperty( ref _macroInvoker, value );
        }

        public override string Name
        {
            get => _name;
            set => SetName( _name, value );
        }

        [JsonIgnore]
        public int PausedLineNumber
        {
            get => _pausedLineNumber;
            set => SetProperty( ref _pausedLineNumber, value );
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

            _macroInvoker.Execute( this, parameters );
        }

        public void Stop()
        {
            if ( IsRunning )
            {
                _macroInvoker.Stop();
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
    }
}