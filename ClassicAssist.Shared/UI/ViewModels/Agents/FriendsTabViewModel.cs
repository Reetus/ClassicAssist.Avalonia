using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Friends;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Shared.UI.ViewModels.Agents;

public class FriendsTabViewModel : BaseViewModel, ISettingProvider
{
    public ICommand AddFriendCommand => field ??= new RelayCommandAsync( AddFriend, o => true );

    public ICommand ChangeRehueOption => field ??= new RelayCommand( ChangeRehue, o => true );

    public ICommand DeselectAllLayersCommand => field ??= new RelayCommand( o => SetAllLayers( false ), o => true );

    public Options Options
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ObservableCollection<RehueLayerEntry> RehueLayers
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public bool RehueLayersOpen
    {
        get;
        set
        {
            // Rebuild on open so the list always reflects the current profile
            if ( value )
            {
                RefreshRehueLayers();
            }

            SetProperty( ref field, value );
        }
    }

    public ICommand RemoveFriendCommand => field ??=
            new RelayCommandAsync( RemoveFriend, o => SelectedItem != null );
    //new RelayCommandAsync( RemoveFriend, o => SelectedItem != null ) );

    public FriendEntry SelectedItem
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand SelectAllLayersCommand => field ??= new RelayCommand( o => SetAllLayers( true ), o => true );

    public ICommand SelectHueCommand => field ??=
            new RelayCommandAsync( SelectHue, o => Options?.RehueFriends ?? false );

    public void Serialize( JObject json )
    {
        JObject config = new()
        {
            ["IncludePartyMembersInFriends"] = Options.IncludePartyMembersInFriends,
            ["PreventAttackingFriendsInWarMode"] = Options.PreventAttackingFriendsInWarMode,
            ["PreventTargetingFriendsWithHarmful"] = Options.PreventTargetingFriendsWithHarmful
        };

        JArray friends = [];

        foreach ( FriendEntry friend in Options.Friends )
        {
            friends.Add( new JObject { { "Name", friend.Name }, { "Serial", friend.Serial } } );
        }

        config.Add( "Items", friends );

        json?.Add( "Friends", config );
    }

    public void Deserialize( JObject json, Options options )
    {
        Options = options;
        Options.Friends.Clear();

        if ( json?["Friends"] == null )
        {
            return;
        }

        JToken config = json["Friends"];

        Options.IncludePartyMembersInFriends = config["IncludePartyMembersInFriends"]?.ToObject<bool>() ?? true;
        Options.PreventAttackingFriendsInWarMode =
            config["PreventAttackingFriendsInWarMode"]?.ToObject<bool>() ?? true;
        Options.PreventTargetingFriendsWithHarmful =
            config["PreventTargetingFriendsWithHarmful"]?.ToObject<bool>() ?? false;

        if ( config["Items"] == null )
        {
            return;
        }

        foreach ( JToken token in config["Items"] )
        {
            Options.Friends.Add( new FriendEntry
            {
                Name = token["Name"].ToObject<string>(),
                Serial = token["Serial"].ToObject<int>()
            } );
        }
    }

    private static void ChangeRehue( object obj )
    {
        MainCommands.Resync();
    }

    private void RefreshRehueLayers()
    {
        List<RehueLayerEntry> entries =
        [
            new( Strings.Body, null, Options.CurrentOptions.RehueFriendsBody, OnLayerChanged )
        ];

        IEnumerable<Layer> layers = Enum.GetValues<Layer>().Where( l => l is > Layer.Invalid and <= Layer.Mount and not Layer.Unused_xF );

        entries.AddRange( layers.Select( l => new RehueLayerEntry( l.ToString(), l,
            !Options.CurrentOptions.RehueFriendsExcludedLayers.Contains( l ), OnLayerChanged ) ) );

        RehueLayers = new ObservableCollection<RehueLayerEntry>( entries );

        void OnLayerChanged( RehueLayerEntry entry )
        {
            ApplyRehueLayers();
        }
    }

    private void SetAllLayers( bool enabled )
    {
        foreach ( RehueLayerEntry entry in RehueLayers )
        {
            entry.SetEnabledSilent( enabled );
        }

        ApplyRehueLayers();
    }

    private void ApplyRehueLayers()
    {
        // Replace rather than mutate, the set is read from the packet thread
        Options.CurrentOptions.RehueFriendsExcludedLayers =
            [.. RehueLayers.Where( e => e.Layer.HasValue && !e.Enabled ).Select( e => e.Layer.Value )];
        Options.CurrentOptions.RehueFriendsBody = RehueLayers.Any( e => !e.Layer.HasValue && e.Enabled );

        if ( Options.CurrentOptions.RehueFriends )
        {
            MainCommands.Resync();
        }
    }

    private static async Task SelectHue( object obj )
    {
        int hue = await Engine.UIInvoker.GetHueAsync();

        Options.CurrentOptions.RehueFriendsHue = hue;
        MainCommands.Resync();
    }

    private static async Task AddFriend( object arg )
    {
        await Task.Run( () => MobileCommands.AddFriend() );
    }

    private static Task RemoveFriend( object arg )
    {
        if ( arg is not FriendEntry fe )
        {
            return Task.CompletedTask;
        }

        MobileCommands.RemoveFriend( fe.Serial );

        return Task.CompletedTask;
    }
}