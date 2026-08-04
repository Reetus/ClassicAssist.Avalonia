using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Filters;
using ClassicAssist.Data.Macros;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UI.ViewModels;
using ClassicAssist.Shared.UO;
using ClassicAssist.UI.Misc;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.UI.ViewModels
{
    public class GeneralControlViewModel : BaseViewModel, ISettingProvider
    {
        private static ICommand _saveProfileCommand;
        private ICommand _changeProfileCommand;
        private ICommand _configureFilterCommand;
        private bool _isLinkedProfile;
        private ICommand _linkUnlinkProfileCommand;
        private ICommand _newProfileCommand;
        private Options _options;
        private ObservableCollection<string> _profiles = new ObservableCollection<string>();
        private ICommand _removeSavedPasswordCommand;
        private Dictionary<string, string> _savedPasswords = new Dictionary<string, string>();
        private string _selectedProfile = Options.CurrentOptions.Name;

        public GeneralControlViewModel()
        {
            Type[] filterTypes =
            {
                typeof( WeatherFilter ), typeof( SeasonFilter ), typeof( LightLevelFilter ),
                typeof( RepeatedMessagesFilter ), typeof( ClilocFilter ), typeof( SoundFilter ),
                typeof( ItemIDFilter )
            };

            foreach ( Type type in filterTypes )
            {
                FilterEntry fe = (FilterEntry) Activator.CreateInstance( type );
                Filters.Add( fe );
            }

            RefreshProfiles();

            AssistantOptions.ProfileChangedEvent += OnProfileChangedEvent;
            AssistantOptions.SavedPasswordsChanged += OnSavedPasswordsChangedEvent;

            OnSavedPasswordsChangedEvent( this, EventArgs.Empty );
        }

        public ICommand ChangeProfileCommand =>
            _changeProfileCommand ?? ( _changeProfileCommand = new RelayCommand( ChangeProfile, o => true ) );

        public ICommand ConfigureFilterCommand =>
            _configureFilterCommand ?? ( _configureFilterCommand =
                new RelayCommandAsync( ConfigureFilter, o => o is IConfigurableFilter ) );

        public ObservableCollectionEx<FilterEntry> Filters { get; set; } = new ObservableCollectionEx<FilterEntry>();

        public bool IsLinkedProfile
        {
            get => _isLinkedProfile;
            set => SetProperty( ref _isLinkedProfile, value );
        }

        public ICommand LinkUnlinkProfileCommand =>
            _linkUnlinkProfileCommand ?? ( _linkUnlinkProfileCommand = new RelayCommand( LinkUnlinkProfile ) );

        public ICommand NewProfileCommand =>
            _newProfileCommand ?? ( _newProfileCommand = new RelayCommandAsync( NewProfile, o => true ) );

        public Options Options
        {
            get => _options;
            set => SetProperty( ref _options, value );
        }

        public ObservableCollection<string> Profiles
        {
            get => _profiles;
            set => SetProperty( ref _profiles, value );
        }

        public ICommand RemoveSavedPasswordCommand =>
            _removeSavedPasswordCommand ?? ( _removeSavedPasswordCommand =
                new RelayCommand( RemoveSavedPassword, o => AssistantOptions.SavePasswords ) );

        public Dictionary<string, string> SavedPasswords
        {
            get => _savedPasswords;
            set => SetProperty( ref _savedPasswords, value );
        }

        public bool SavePasswords
        {
            get => AssistantOptions.SavePasswords;
            set => AssistantOptions.SavePasswords = value;
        }

        public bool SavePasswordsOnlyBlank
        {
            get => AssistantOptions.SavePasswordsOnlyBlank;
            set => AssistantOptions.SavePasswordsOnlyBlank = value;
        }

        public ICommand SaveProfileCommand =>
            _saveProfileCommand ?? ( _saveProfileCommand = new RelayCommand( SaveProfile ) );

        public string SelectedProfile
        {
            get => _selectedProfile;
            set => SetProperty( ref _selectedProfile, value );
        }

        public void Serialize( JObject json )
        {
            JObject obj = new JObject
            {
                ["AlwaysOnTop"] = Options.CurrentOptions.AlwaysOnTop,
                ["LightLevel"] = Options.CurrentOptions.LightLevel,
                ["ActionDelay"] = Options.CurrentOptions.ActionDelay,
                ["ActionDelayMS"] = Options.CurrentOptions.ActionDelayMS,
                ["DragDelay"] = Options.CurrentOptions.DragDelay,
                ["DragDelayMS"] = Options.CurrentOptions.DragDelayMS,
                ["Debug"] = Options.CurrentOptions.Debug
            };

            JArray filtersArray = new JArray();

            foreach ( FilterEntry filterEntry in Filters )
            {
                JObject filterObj = new JObject
                {
                    { "Name", filterEntry.GetType().ToString() }, { "Enabled", filterEntry.Enabled }
                };

                if ( filterEntry is IConfigurableFilter configurableFilter )
                {
                    JObject options = configurableFilter.Serialize();
                    filterObj.Add( "Options", options );
                }

                filtersArray.Add( filterObj );
            }

            obj.Add( "Filters", filtersArray );

            json?.Add( "General", obj );
        }

        public void Deserialize( JObject json, Options options )
        {
            Options = options;

            // Reset current filters to default value
            foreach ( FilterEntry filterEntry in Filters )
            {
                FilterOptionsAttribute a =
                    (FilterOptionsAttribute) Attribute.GetCustomAttribute( filterEntry.GetType(),
                        typeof( FilterOptionsAttribute ) );

                if ( a == null )
                {
                    continue;
                }

                filterEntry.Enabled = a.DefaultEnabled;

                if ( filterEntry is IConfigurableFilter configurableFilter )
                {
                    configurableFilter.ResetOptions();
                }
            }

            if ( json?["General"] == null )
            {
                return;
            }

            JToken general = json["General"];

            Options.LightLevel = general["LightLevel"]?.ToObject<int>() ?? 100;
            Options.ActionDelay = general["ActionDelay"]?.ToObject<bool>() ?? false;
            Options.ActionDelayMS = general["ActionDelayMS"]?.ToObject<int>() ?? 900;
            Options.DragDelay = general["DragDelay"]?.ToObject<bool>() ?? false;
            Options.DragDelayMS = general["DragDelayMS"]?.ToObject<int>() ?? 450;
            Options.AlwaysOnTop = general["AlwaysOnTop"]?.ToObject<bool>() ?? false;
            Options.Debug = general["Debug"]?.ToObject<bool>() ?? false;

            if ( general["Filters"] == null )
            {
                return;
            }

            foreach ( JToken token in general["Filters"] )
            {
                string filterName = token["Name"]?.ToObject<string>() ?? string.Empty;
                bool enabled = token["Enabled"]?.ToObject<bool>() ?? false;

                FilterEntry filter = Filters.FirstOrDefault( f => f.GetType().ToString().Equals( filterName ) );

                if ( filter != null )
                {
                    filter.Enabled = enabled;
                }

                if ( filter is IConfigurableFilter configurableFilter && token["Options"] != null )
                {
                    configurableFilter.Deserialize( token["Options"] );
                }

                MigrateBardsMusicFilter( filterName, enabled );
            }
        }

        /// <summary>
        ///     BardsMusicFilter was removed once SoundFilter landed - the shipped
        ///     <c>Data/Filters/Audio/Skills.json</c> already carries a "Bards Music" entry covering the same
        ///     sound IDs (and one more). It defaulted to enabled, so a profile written by an older build
        ///     that still has it on gets the equivalent SoundFilter entry switched on instead; without this
        ///     those users would silently start hearing bard music again. The key disappears from the
        ///     profile on the next save, so this only ever runs once.
        /// </summary>
        private void MigrateBardsMusicFilter( string filterName, bool enabled )
        {
            if ( !enabled || filterName != "ClassicAssist.Data.Filters.BardsMusicFilter" )
            {
                return;
            }

            SoundFilter soundFilter = Filters.OfType<SoundFilter>().FirstOrDefault();

            SoundFilterEntry entry = soundFilter?.Items.FirstOrDefault( i => i.Name == "Bards Music" );

            if ( entry == null )
            {
                return;
            }

            entry.Enabled = true;
            soundFilter.Enabled = true;
        }

        private void OnSavedPasswordsChangedEvent( object sender, EventArgs e )
        {
            Dictionary<string, string> newList =
                AssistantOptions.SavedPasswords.ToDictionary( kvp => kvp.Key, kvp => kvp.Value );

            SavedPasswords = newList;
        }

        private void RemoveSavedPassword( object obj )
        {
            if ( !( obj is KeyValuePair<string, string> kvp ) )
            {
                return;
            }

            SavedPasswords.Remove( kvp.Key );
            AssistantOptions.SavedPasswords.Remove( kvp.Key );
            AssistantOptions.OnPasswordsChanged();
        }

        private static async Task ConfigureFilter( object obj )
        {
            if ( !( obj is IConfigurableFilter configurableFilter ) )
            {
                return;
            }

            await configurableFilter.Configure();
        }

        private void OnProfileChangedEvent( string profile )
        {
            _dispatcher.Invoke( () =>
            {
                Options = Options.CurrentOptions;

                if ( Engine.Player != null )
                {
                    IsLinkedProfile = AssistantOptions.GetLinkedProfile( Engine.Player.Serial ) == profile;
                }

                SelectedProfile = profile;
            } );
        }

        private static void SaveProfile( object obj )
        {
            Options.Save( Options.CurrentOptions );
            Commands.SystemMessage( Strings.Profile_saved___ );
        }

        private void RefreshProfiles()
        {
            string[] profiles = Options.GetProfiles();

            if ( profiles == null )
            {
                return;
            }

            Profiles.Clear();

            foreach ( string profile in profiles )
            {
                Profiles.Add( Path.GetFileName( profile ) );
            }
        }

        private void ChangeProfile( object obj )
        {
            if ( !( obj is string profileName ) )
            {
                return;
            }

            MacroManager.GetInstance().StopAll();
            LoadProfile( profileName );
            Engine.UpdateWindowTitle();
        }

        private void LinkUnlinkProfile( object obj )
        {
            if ( Engine.Player == null )
            {
                return;
            }

            if ( AssistantOptions.GetLinkedProfile( Engine.Player.Serial ) == Options.CurrentOptions.Name )
            {
                AssistantOptions.RemoveLinkedProfile( Engine.Player.Serial );
                IsLinkedProfile = false;
            }
            else
            {
                AssistantOptions.SetLinkedProfile( Engine.Player.Serial, Options.CurrentOptions.Name );
                IsLinkedProfile = true;
            }
        }

        private void LoadProfile( string profile )
        {
            foreach ( FilterEntry filterEntry in Filters )
            {
                filterEntry?.Action( false );
            }

            Options.ClearOptions();
            Options.CurrentOptions = new Options();
            Options.Load( profile, Options.CurrentOptions );
            AssistantOptions.LastProfile = profile;

            if ( Engine.Player != null )
            {
                IsLinkedProfile = AssistantOptions.GetLinkedProfile( Engine.Player.Serial ) == profile;
            }
        }

        private async Task NewProfile( object arg )
        {
            NewProfileViewModel vm = new NewProfileViewModel();

            await Engine.UIInvoker.InvokeDialog( "NewProfileWindow", dataContext: vm );

            if (!string.IsNullOrEmpty( vm.FileName ) )
            {
                RefreshProfiles();

                SelectedProfile = vm.FileName;

                ChangeProfile( vm.FileName );
            }
        }
    }
}