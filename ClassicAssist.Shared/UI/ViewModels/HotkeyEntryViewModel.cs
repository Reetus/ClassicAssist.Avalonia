using System.Collections.Generic;
using System.Collections.Specialized;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Hotkeys.Commands;
using ClassicAssist.UI.Misc;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.UI.ViewModels;

public abstract class HotkeyEntryViewModel<T> : BaseViewModel where T : HotkeyEntry
{
    private readonly HotkeyCommand _category;
    protected List<HotkeyCommand> _staticOptions = [];

    protected HotkeyEntryViewModel( string name )
    {
        _category = new HotkeyCommand { Name = name, IsCategory = true };

        HotkeyManager hotkey = HotkeyManager.GetInstance();

        hotkey.AddCategory( _category );

        Items.CollectionChanged += OnCollectionChanged;

        _category.Children = [];
    }

    public ObservableCollectionEx<T> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    ~HotkeyEntryViewModel()
    {
        Items.CollectionChanged -= OnCollectionChanged;
    }

    protected virtual void OnCollectionChanged( object sender, NotifyCollectionChangedEventArgs e )
    {
        _category.Children = [.. _staticOptions, .. Items];
    }

    protected void SetJsonValue( JToken json, string name, JToken value )
    {
        json[name] = value;
    }

    protected T2 GetJsonValue<T2>( JToken json, string name, T2 defaultValue )
    {
        if ( json == null )
        {
            return defaultValue;
        }

        return json[name] == null ? defaultValue : json[name].ToObject<T2>();
    }

    protected void SerializeStatic( JObject organizer )
    {
        JObject staticHotkeys = [];

        foreach ( HotkeyCommand option in _staticOptions )
        {
            JObject obj = new()
            {
                { "Keys", option.Hotkey.ToJObject() },
                { "PassToUO", option.PassToUO },
                { "Disableable", option.Disableable }
            };

            staticHotkeys.Add( option.Name, obj );
        }

        organizer.Add( "Static", staticHotkeys );
    }

    protected void DeserializeStatic( JObject obj )
    {
        if ( obj?["Static"] is not JObject )
        {
            return;
        }

        foreach ( HotkeyCommand option in _staticOptions )
        {
            if ( obj["Static"][option.Name] is not JObject json )
            {
                continue;
            }

            option.Hotkey = new ShortcutKeys( json["Keys"] );
            option.PassToUO = GetJsonValue( json, "PassToUO", option.PassToUO );
            option.Disableable = GetJsonValue( json, "Disableable", option.Disableable );
        }
    }
}