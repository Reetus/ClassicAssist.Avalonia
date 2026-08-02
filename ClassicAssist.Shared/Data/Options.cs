using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ClassicAssist.Data.Friends;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Scavenger;
using ClassicAssist.Shared;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Data
{
    public class Options : INotifyPropertyChanged
    {
        public delegate void dLoad( JObject json, Options options );

        public const string DEFAULT_SETTINGS_FILENAME = "settings.json";
        private static string _profilePath;
        private char _commandPrefix = '+';

        public int ExpireTargetsMS
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool AbilitiesGump
        {
            get;
            set => SetProperty( ref field, value );
        } = true;

        public int AbilitiesGumpX
        {
            get;
            set => SetProperty( ref field, value );
        } = 100;

        public int AbilitiesGumpY
        {
            get;
            set => SetProperty( ref field, value );
        } = 100;

        public bool ActionDelay
        {
            get;
            set => SetProperty( ref field, value );
        }

        public int ActionDelayMS
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool AlwaysOnTop
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool AutoAcceptPartyInvite
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool AutoAcceptPartyOnlyFromFriends
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool CheckHandsPotions
        {
            get;
            set => SetProperty( ref field, value );
        }

        public char CommandPrefix
        {
            get => _commandPrefix;
            set => SetProperty(ref _commandPrefix, value);
        }

        public int? CommandPrefixIndex
        {
            get
            {
                switch ( _commandPrefix )
                {
                    case '+': return 0;
                    case '=': return 1;
                }

                return null;
            }
            set
            {
                switch ( value )
                {
                    case 0:
                        CommandPrefix = '+';
                        break;
                    case 1:
                        CommandPrefix = '=';
                        break;
                }
            }
        }

        public static Options CurrentOptions { get; set; } = new Options();

        public bool Debug
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool DefaultMacroQuietMode
        {
            get;
            set => SetProperty( ref field, value );
        }

        public string EnemyTargetMessage
        {
            get;
            set => SetProperty( ref field, value );
        }

        public ObservableCollection<FriendEntry> Friends
        {
            get;
            set => SetProperty( ref field, value );
        } = new();

        public string FriendTargetMessage
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool GetFriendEnemyUsesIgnoreList
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool IncludePartyMembersInFriends
        {
            get;
            set => SetProperty( ref field, value );
        }

        public string LastTargetMessage
        {
            get;
            set => SetProperty( ref field, value );
        }

        public int LightLevel
        {
            get;
            set
            {
                SetProperty( ref field, value );
                Engine.SendPacketToClient( new byte[] { 0x4F, ( byte )CurrentOptions.LightLevel }, 2 );
            }
        }

        public bool LimitMouseWheelTrigger
        {
            get;
            set => SetProperty( ref field, value );
        }

        public int LimitMouseWheelTriggerMS
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool MacrosGump
        {
            get;
            set => SetProperty( ref field, value );
        }

        public int MacrosGumpX
        {
            get;
            set => SetProperty( ref field, value );
        }

        public int MacrosGumpY
        {
            get;
            set => SetProperty( ref field, value );
        }

        public int MaxTargetQueueLength
        {
            get;
            set => SetProperty( ref field, value );
        } = 1;

        public string Name
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool PersistUseOnce
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool PreventAttackingFriendsInWarMode
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool PreventAttackingInnocentsInGuardzone
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool PreventTargetingFriendsWithHarmful
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool PreventTargetingInnocentsInGuardzone
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool QueueLastTarget
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool RangeCheckLastTarget
        {
            get;
            set => SetProperty( ref field, value );
        }

        public int RangeCheckLastTargetAmount
        {
            get;
            set => SetProperty( ref field, value );
        } = 11;

        public bool RehueFriends
        {
            get;
            set => SetProperty( ref field, value );
        }

        public int RehueFriendsHue
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool ShowProfileNameWindowTitle
        {
            get;
            set
            {
                SetProperty( ref field, value );
                Engine.UpdateWindowTitle();
            }
        }

        public bool ShowResurrectionWaypoints
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool SetUOTitle
        {
            get;
            set
            {
                SetProperty( ref field, value );
                Engine.SetTitle();
            }
        }

        public SmartTargetOption SmartTargetOption
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool SortMacrosAlphabetical
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool UseDeathScreenWhilstHidden
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool UseExperimentalFizzleDetection
        {
            get;
            set => SetProperty( ref field, value );
        }

        public bool UseObjectQueue
        {
            get;
            set => SetProperty( ref field, value );
        }

        public int UseObjectQueueAmount
        {
            get;
            set => SetProperty( ref field, value );
        } = 5;

        public event PropertyChangedEventHandler PropertyChanged;

        public delegate void dSave( JObject obj );

        public static event dSave SaveEvent;

        public static void Save( Options options )
        {
            JObject obj = new JObject { { "Name", options.Name } };

            SaveEvent?.Invoke( obj );

            EnsureProfilePath( Engine.StartupPath ?? Environment.CurrentDirectory );

            File.WriteAllText( Path.Combine( _profilePath, options.Name ?? DEFAULT_SETTINGS_FILENAME ), obj.ToString() );
        }

        private static void EnsureProfilePath( string startupPath )
        {
            _profilePath = Path.IsPathRooted( AssistantOptions.ProfileDirectory )
                ? AssistantOptions.ProfileDirectory
                : Path.Combine( startupPath, AssistantOptions.ProfileDirectory );

            if ( !Directory.Exists( _profilePath ) )
            {
                Directory.CreateDirectory( _profilePath );
            }
        }

        public static void ClearOptions()
        {
            HotkeyManager.GetInstance().ClearAllHotkeys();
            AliasCommands._aliases.Clear();
            ScavengerManager.GetInstance().Items.Clear();
            MacroManager.GetInstance().Items.Clear();
        }

        public static event dLoad LoadEvent;

        public static void Load( string optionsFile, Options options )
        {
            AssistantOptions.LastProfile = optionsFile;

            EnsureProfilePath( Engine.StartupPath ?? Environment.CurrentDirectory );

            JObject json = new JObject();

            string fullPath = Path.Combine( _profilePath, optionsFile );

            if ( File.Exists( fullPath ) )
            {
                json = JObject.Parse( File.ReadAllText( fullPath ) );
            }

            options.Name = options.Name ?? json["Name"]?.ToObject<string>() ?? DEFAULT_SETTINGS_FILENAME;

            LoadEvent?.Invoke( json, options );
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

        public static string[] GetProfiles()
        {
            EnsureProfilePath( Engine.StartupPath ?? Environment.CurrentDirectory );
            return Directory.EnumerateFiles( _profilePath, "*.json" ).ToArray();
        }
    }

    [Flags]
    public enum SmartTargetOption
    {
        None = 0b00,
        Friend = 0b01,
        Enemy = 0b10,
        Both = 0b11
    }
}