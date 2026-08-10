using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Data.Friends;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Misc;
using ClassicAssist.UI.ViewModels;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Shared.UI.ViewModels.Agents;

public class FriendsTabViewModel : BaseViewModel, ISettingProvider
{
    public ICommand AddFriendCommand => field ??= new RelayCommandAsync( AddFriend, o => true );

    public ICommand ChangeRehueOption => field ??= new RelayCommand( ChangeRehue, o => true );

    public Options Options
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand RemoveFriendCommand => field ??=
            new RelayCommandAsync( RemoveFriend, o => SelectedItem != null );
    //new RelayCommandAsync( RemoveFriend, o => SelectedItem != null ) );

    public FriendEntry SelectedItem
    {
        get;
        set => SetProperty( ref field, value );
    }

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