using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.DebugAdapter.Dap;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.Misc;
using ClassicAssist.UO.Gumps;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.UI.ViewModels
{
    public class OptionsTabViewModel : BaseViewModel, ISettingProvider
    {
        private Options _currentOptions;
        private ICommand _macrosGumpChangedCommand;
        private ICommand _selectMacroTextColorCommand;
        private ICommand _setLanguageOverrideCommand;
        private ICommand _toggleDebugAdapterCommand;

        public Options CurrentOptions
        {
            get => _currentOptions;
            set => SetProperty( ref _currentOptions, value );
        }

        public ICommand MacrosGumpChangedCommand =>
            _macrosGumpChangedCommand ?? ( _macrosGumpChangedCommand = new RelayCommand( MacrosGumpChanged ) );

        public ICommand SelectMacroTextColorCommand =>
            _selectMacroTextColorCommand ?? ( _selectMacroTextColorCommand =
                new RelayCommand( SelectMacroTextColor, o => CurrentOptions != null ) );

        public ICommand SetLanguageOverrideCommand =>
            _setLanguageOverrideCommand ?? ( _setLanguageOverrideCommand = new RelayCommand( SetLanguageOverride ) );

        public ICommand ToggleDebugAdapterCommand =>
            _toggleDebugAdapterCommand ??
            ( _toggleDebugAdapterCommand = new RelayCommand( ToggleDebugAdapter, o => true ) );

        public void Serialize( JObject json )
        {
            JObject options = new JObject();

            JObject useOnce = new JObject { ["Persist"] = CurrentOptions.PersistUseOnce };

            if ( CurrentOptions.PersistUseOnce )
            {
                JArray useOnceItems = new JArray();

                foreach ( int serial in ActionCommands.UseOnceList )
                {
                    useOnceItems.Add( serial );
                }

                useOnce.Add( "Items", useOnceItems );
            }

            options.Add( "UseOnce", useOnce );
            options.Add( "UseDeathScreenWhilstHidden", CurrentOptions.UseDeathScreenWhilstHidden );
            options.Add( "CommandPrefix", CurrentOptions.CommandPrefix );
            options.Add( "RangeCheckLastTarget", CurrentOptions.RangeCheckLastTarget );
            options.Add( "RangeCheckLastTargetAmount", CurrentOptions.RangeCheckLastTargetAmount );
            options.Add( "UseExperimentalFizzleDetection", CurrentOptions.UseExperimentalFizzleDetection );
            options.Add( "UseObjectQueue", CurrentOptions.UseObjectQueue );
            options.Add( "UseObjectQueueAmount", CurrentOptions.UseObjectQueueAmount );
            options.Add( "QueueLastTarget", CurrentOptions.QueueLastTarget );
            options.Add( "MaxTargetQueueLength", CurrentOptions.MaxTargetQueueLength );
            options.Add( "ExpireTargetsMS", CurrentOptions.ExpireTargetsMS );
            options.Add( "SmartTargetOption", CurrentOptions.SmartTargetOption.ToString() );
            options.Add( "LimitHotkeyTrigger", CurrentOptions.LimitHotkeyTrigger );
            options.Add( "LimitHotkeyTriggerMS", CurrentOptions.LimitHotkeyTriggerMS );
            options.Add( "LimitMouseWheelTrigger", CurrentOptions.LimitMouseWheelTrigger );
            options.Add( "LimitMouseWheelTriggerMS", CurrentOptions.LimitMouseWheelTriggerMS );
            options.Add( "AutoAcceptPartyInvite", CurrentOptions.AutoAcceptPartyInvite );
            options.Add( "AutoAcceptPartyOnlyFromFriends", CurrentOptions.AutoAcceptPartyOnlyFromFriends );
            options.Add( "PreventTargetingInnocentsInGuardzone", CurrentOptions.PreventTargetingInnocentsInGuardzone );
            options.Add( "PreventAttackingInnocentsInGuardzone", CurrentOptions.PreventAttackingInnocentsInGuardzone );
            options.Add( "LastTargetMessage", CurrentOptions.LastTargetMessage );
            options.Add( "FriendTargetMessage", CurrentOptions.FriendTargetMessage );
            options.Add( "EnemyTargetMessage", CurrentOptions.EnemyTargetMessage );
            options.Add( "DefaultMacroQuietMode", CurrentOptions.DefaultMacroQuietMode );
            options.Add( "GetFriendEnemyUsesIgnoreList", CurrentOptions.GetFriendEnemyUsesIgnoreList );
            options.Add( "AbilitiesGump", CurrentOptions.AbilitiesGump );
            options.Add( "AbilitiesGumpX", CurrentOptions.AbilitiesGumpX );
            options.Add( "AbilitiesGumpY", CurrentOptions.AbilitiesGumpY );
            options.Add( "ShowProfileNameWindowTitle", CurrentOptions.ShowProfileNameWindowTitle );
            options.Add( "SetUOTitle", CurrentOptions.SetUOTitle );
            options.Add( "SortMacrosAlphabetical", CurrentOptions.SortMacrosAlphabetical );
            options.Add( "ShowResurrectionWaypoints", CurrentOptions.ShowResurrectionWaypoints );
            options.Add( "RehueFriends", CurrentOptions.RehueFriends );
            options.Add( "RehueFriendsHue", CurrentOptions.RehueFriendsHue );
            options.Add( "CheckHandsPotions", CurrentOptions.CheckHandsPotions );
            options.Add( "MacrosGump", CurrentOptions.MacrosGump );
            options.Add( "MacrosGumpX", CurrentOptions.MacrosGumpX );
            options.Add( "MacrosGumpY", CurrentOptions.MacrosGumpY );
            options.Add( "MacrosGumpHeight", CurrentOptions.MacrosGumpHeight );
            options.Add( "MacrosGumpWidth", CurrentOptions.MacrosGumpWidth );
            options.Add( "MacrosGumpTextColor", CurrentOptions.MacrosGumpTextColor );
            options.Add( "MacrosGumpTransparent", CurrentOptions.MacrosGumpTransparent );
            options.Add( "DisableHotkeysLoad", CurrentOptions.DisableHotkeysLoad );
            options.Add( "HotkeysStatusGump", CurrentOptions.HotkeysStatusGump );
            options.Add( "HotkeysStatusGumpX", CurrentOptions.HotkeysStatusGumpX );
            options.Add( "HotkeysStatusGumpY", CurrentOptions.HotkeysStatusGumpY );

            json?.Add( "Options", options );
        }

        public void Deserialize( JObject json, Options options )
        {
            CurrentOptions = options;

            CurrentOptions.PropertyChanged += OnOptionsChanged;

            // The debug adapter is a session-only, app-level toggle that isn't persisted per profile.
            // A running server outlives profile switches, so reflect its actual state in the (fresh)
            // options rather than letting the checkbox revert to off while the server is still bound.
            CurrentOptions.DebugAdapterEnabled = DapServer.IsRunning;

            if ( DapServer.IsRunning )
            {
                CurrentOptions.DebugAdapterPort = DapServer.Port;
            }

            ActionCommands.UseOnceList.Clear();

            JToken config = json?["Options"];

            if ( config?["UseOnce"] != null )
            {
                CurrentOptions.PersistUseOnce = config["UseOnce"]["Persist"]?.ToObject<bool>() ?? false;

                if ( CurrentOptions.PersistUseOnce )
                {
                    foreach ( JToken token in config["UseOnce"]["Items"] )
                    {
                        ActionCommands.UseOnceList.Add( token.ToObject<int>() );
                    }
                }
            }

            CurrentOptions.UseDeathScreenWhilstHidden =
                config?["UseDeathScreenWhilstHidden"]?.ToObject<bool>() ?? false;
            CurrentOptions.CommandPrefix = config?["CommandPrefix"]?.ToObject<char>() ?? '=';
            CurrentOptions.RangeCheckLastTarget = config?["RangeCheckLastTarget"]?.ToObject<bool>() ?? false;
            CurrentOptions.RangeCheckLastTargetAmount = config?["RangeCheckLastTargetAmount"]?.ToObject<int>() ?? 11;

            CurrentOptions.UseExperimentalFizzleDetection =
                config?["UseExperimentalFizzleDetection"]?.ToObject<bool>() ?? false;

            CurrentOptions.UseObjectQueue = config?["UseObjectQueue"]?.ToObject<bool>() ?? false;
            CurrentOptions.UseObjectQueueAmount = config?["UseObjectQueueAmount"]?.ToObject<int>() ?? 5;
            CurrentOptions.QueueLastTarget = config?["QueueLastTarget"]?.ToObject<bool>() ?? false;
            CurrentOptions.MaxTargetQueueLength = config?["MaxTargetQueueLength"]?.ToObject<int>() ?? 1;
            CurrentOptions.ExpireTargetsMS = config?["ExpireTargetsMS"]?.ToObject<int>() ?? -1;
            CurrentOptions.SmartTargetOption =
                config?["SmartTargetOption"]?.ToObject<SmartTargetOption>() ?? SmartTargetOption.None;
            CurrentOptions.LimitHotkeyTrigger = config?["LimitHotkeyTrigger"]?.ToObject<bool>() ?? false;
            CurrentOptions.LimitHotkeyTriggerMS = config?["LimitHotkeyTriggerMS"]?.ToObject<int>() ?? 0;
            CurrentOptions.LimitMouseWheelTrigger = config?["LimitMouseWheelTrigger"]?.ToObject<bool>() ?? false;
            CurrentOptions.LimitMouseWheelTriggerMS = config?["LimitMouseWheelTriggerMS"]?.ToObject<int>() ?? 25;
            CurrentOptions.AutoAcceptPartyInvite = config?["AutoAcceptPartyInvite"]?.ToObject<bool>() ?? false;
            CurrentOptions.AutoAcceptPartyOnlyFromFriends =
                config?["AutoAcceptPartyOnlyFromFriends"]?.ToObject<bool>() ?? false;
            CurrentOptions.PreventTargetingInnocentsInGuardzone =
                config?["PreventTargetingInnocentsInGuardzone"]?.ToObject<bool>() ?? false;
            CurrentOptions.PreventAttackingInnocentsInGuardzone =
                config?["PreventAttackingInnocentsInGuardzone"]?.ToObject<bool>() ?? false;
            CurrentOptions.LastTargetMessage = config?["LastTargetMessage"]?.ToObject<string>() ?? "[Last Target]";
            CurrentOptions.FriendTargetMessage = config?["FriendTargetMessage"]?.ToObject<string>() ?? "[Friend]";
            CurrentOptions.EnemyTargetMessage = config?["EnemyTargetMessage"]?.ToObject<string>() ?? "[Enemy]";
            CurrentOptions.DefaultMacroQuietMode = config?["DefaultMacroQuietMode"]?.ToObject<bool>() ?? false;
            CurrentOptions.GetFriendEnemyUsesIgnoreList =
                config?["GetFriendEnemyUsesIgnoreList"]?.ToObject<bool>() ?? false;
            CurrentOptions.AbilitiesGump = config?["AbilitiesGump"]?.ToObject<bool>() ?? true;
            CurrentOptions.AbilitiesGumpX = config?["AbilitiesGumpX"]?.ToObject<int>() ?? 100;
            CurrentOptions.AbilitiesGumpY = config?["AbilitiesGumpY"]?.ToObject<int>() ?? 100;
            CurrentOptions.ShowProfileNameWindowTitle =
                config?["ShowProfileNameWindowTitle"]?.ToObject<bool>() ?? false;
            CurrentOptions.SetUOTitle = config?["SetUOTitle"]?.ToObject<bool>() ?? true;
            CurrentOptions.SortMacrosAlphabetical = config?["SortMacrosAlphabetical"]?.ToObject<bool>() ?? false;
            CurrentOptions.ShowResurrectionWaypoints = config?["ShowResurrectionWaypoints"]?.ToObject<bool>() ?? true;
            CurrentOptions.RehueFriends = config?["RehueFriends"]?.ToObject<bool>() ?? false;
            CurrentOptions.RehueFriendsHue = config?["RehueFriendsHue"]?.ToObject<int>() ?? 35;
            CurrentOptions.CheckHandsPotions = config?["CheckHandsPotions"]?.ToObject<bool>() ?? false;
            CurrentOptions.MacrosGump = config?["MacrosGump"]?.ToObject<bool>() ?? true;
            CurrentOptions.MacrosGumpX = config?["MacrosGumpX"]?.ToObject<int>() ?? 100;
            CurrentOptions.MacrosGumpY = config?["MacrosGumpY"]?.ToObject<int>() ?? 100;
            CurrentOptions.MacrosGumpHeight = config?["MacrosGumpHeight"]?.ToObject<int>() ?? 190;
            CurrentOptions.MacrosGumpWidth = config?["MacrosGumpWidth"]?.ToObject<int>() ?? 180;
            CurrentOptions.MacrosGumpTextColor = config?["MacrosGumpTextColor"]?.ToObject<string>() ?? "#FFFFFFFF";
            CurrentOptions.MacrosGumpTransparent = config?["MacrosGumpTransparent"]?.ToObject<bool>() ?? true;
            CurrentOptions.DisableHotkeysLoad = config?["DisableHotkeysLoad"]?.ToObject<bool>() ?? false;
            CurrentOptions.HotkeysStatusGump = config?["HotkeysStatusGump"]?.ToObject<bool>() ?? false;
            CurrentOptions.HotkeysStatusGumpX = config?["HotkeysStatusGumpX"]?.ToObject<int>() ?? 10;
            CurrentOptions.HotkeysStatusGumpY = config?["HotkeysStatusGumpY"]?.ToObject<int>() ?? 30;

            HotkeyManager.GetInstance().Enabled = !CurrentOptions.DisableHotkeysLoad;

            if ( CurrentOptions.AbilitiesGumpX < 0 )
            {
                CurrentOptions.AbilitiesGumpX = 100;
            }

            if ( CurrentOptions.AbilitiesGumpY < 0 )
            {
                CurrentOptions.AbilitiesGumpY = 100;
            }

            if ( CurrentOptions.MacrosGump )
            {
                MacrosGump.Initialize();
            }
        }

        // Replay CurrentOptions changes onto Options.CurrentOptions
        // TODO: Fix Options
        private void OnOptionsChanged( object sender, PropertyChangedEventArgs args )
        {
            if ( args.PropertyName == "Name" )
            {
                return;
            }

            object val = CurrentOptions.GetType().GetProperty( args.PropertyName )?.GetValue( CurrentOptions );

            if ( val == null )
            {
                return;
            }

            object oldVal = Options.CurrentOptions.GetType().GetProperty( args.PropertyName )
                ?.GetValue( Options.CurrentOptions );

            if ( !val.Equals( oldVal ) )
            {
                Options.CurrentOptions.GetType().GetProperty( args.PropertyName )
                    ?.SetValue( Options.CurrentOptions, val );
            }
        }

        private static void MacrosGumpChanged( object obj )
        {
            MacrosGump.ResendGump( true );
        }

        private async void SelectMacroTextColor( object obj )
        {
            if ( CurrentOptions == null )
            {
                return;
            }

            MacrosGumpTextColorSelectorViewModel vm = new MacrosGumpTextColorSelectorViewModel
            {
                SelectedColor = CurrentOptions.MacrosGumpTextColor
            };

            await Engine.UIInvoker.InvokeDialog( "MacrosGumpTextColorWindow", dataContext: vm );

            if ( vm.Result )
            {
                CurrentOptions.MacrosGumpTextColor = vm.SelectedColor;
                MacrosGump.ResendGump( true );
            }
        }

        private static async void SetLanguageOverride( object obj )
        {
            if ( !( obj is Language language ) )
            {
                return;
            }

            AssistantOptions.LanguageOverride = language;

            await Engine.MessageBoxProvider.Show( Strings.Restart_game_for_changes_to_take_effect___,
                Strings.Restart_game_for_changes_to_take_effect___ );
        }

        private async void ToggleDebugAdapter( object obj )
        {
            try
            {
                if ( CurrentOptions.DebugAdapterEnabled )
                {
                    int port = CurrentOptions.DebugAdapterPort;

                    if ( port < 1 || port > 65535 )
                    {
                        CurrentOptions.DebugAdapterEnabled = false;
                        await Engine.MessageBoxProvider.Show( Strings.Debug_adapter_invalid_port, Strings.Error );
                        return;
                    }

                    DapServer.Initialize( port );
                }
                else
                {
                    DapServer.Shutdown();
                }
            }
            catch ( Exception e )
            {
                // Ensure any partially-initialised server is torn down before reverting the toggle.
                DapServer.Shutdown();
                CurrentOptions.DebugAdapterEnabled = false;
                await Engine.MessageBoxProvider.Show( e.Message, Strings.Error );
            }
        }
    }
}