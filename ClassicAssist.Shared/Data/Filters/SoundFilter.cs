#region License

// Copyright (C) 2021 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UI;
using ClassicAssist.Shared.UI.ViewModels.Filters;
using ClassicAssist.UO.Network.PacketFilter;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Data.Filters;

[FilterOptions( Name = "Sound Filter", DefaultEnabled = false )]
public class SoundFilter : DynamicFilterEntry, IConfigurableFilter
{
    public SoundFilter()
    {
        ProcessDirectory( Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "Data", "Filters",
            "Audio" ) );
    }

    public static bool IsEnabled { get; set; }

    public ObservableCollection<SoundFilterEntry> Items { get; set; } =
        [];

    public async Task Configure()
    {
        SoundFilterConfigureViewModel vm = new( Items );

        await Engine.UIInvoker.InvokeDialog( "SoundFilterConfigureWindow", dataContext: vm );
    }

    public void Deserialize( JToken token )
    {
        if ( token?["Items"] == null )
        {
            return;
        }

        foreach ( JToken itemsToken in token["Items"] )
        {
            string name = itemsToken["Name"]?.ToObject<string>() ?? string.Empty;

            if ( string.IsNullOrEmpty( name ) )
            {
                continue;
            }

            SoundFilterEntry entry = Items.FirstOrDefault( e => e.Name == name );

            if ( entry == null )
            {
                continue;
            }

            entry.Enabled = itemsToken["Enabled"]?.ToObject<bool>() ?? false;
        }
    }

    public JObject Serialize()
    {
        JObject config = [];

        JArray items = [];

        // Only entries that differ from the shipped default, so the profile doesn't pin every sound
        // and go stale when the Audio data files gain entries.
        foreach ( SoundFilterEntry entry in Items.Where( i => i.Enabled != i.DefaultEnabled ) )
        {
            items.Add( new JObject { ["Name"] = entry.Name, ["Enabled"] = entry.Enabled } );
        }

        config.Add( "Items", items );

        return config;
    }

    public void ResetOptions()
    {
        foreach ( SoundFilterEntry item in Items )
        {
            item.Enabled = item.DefaultEnabled;
        }
    }

    /// <summary>
    ///     Loads every sound definition under <c>Data/Filters/Audio</c>, recursing into subdirectories.
    /// </summary>
    public void ProcessDirectory( string targetDirectory )
    {
        if ( !Directory.Exists( targetDirectory ) )
        {
            return;
        }

        try
        {
            foreach ( string fileName in Directory.GetFiles( targetDirectory, "*.json" ) )
            {
                ProcessFile( fileName );
            }

            foreach ( string subdirectory in Directory.GetDirectories( targetDirectory ) )
            {
                ProcessDirectory( subdirectory );
            }
        }
        catch ( Exception e )
        {
            Engine.MessageBoxProvider?.Show( $"{Strings.Error}: {e}" );
        }
    }

    public void ProcessFile( string path )
    {
        if ( !File.Exists( path ) )
        {
            return;
        }

        SoundFilterEntry[] entries = JsonConvert.DeserializeObject<SoundFilterEntry[]>( File.ReadAllText( path ) );

        if ( entries == null )
        {
            return;
        }

        foreach ( SoundFilterEntry entry in entries )
        {
            entry.Enabled = entry.DefaultEnabled;
            entry.LocalizedName = Strings.ResourceManager.GetString( entry.Name ) ?? entry.Name;
            entry.Category = Strings.ResourceManager.GetString( entry.Category ) ?? entry.Category;

            Items.Add( entry );
        }
    }

    protected override void OnChanged( bool enabled )
    {
        IsEnabled = enabled;
    }

    public override bool CheckPacket( ref byte[] packet, ref int length, PacketDirection direction )
    {
        if ( packet == null || !IsEnabled )
        {
            return false;
        }

        if ( packet[0] != 0x54 || direction != PacketDirection.Incoming )
        {
            return false;
        }

        int soundId = ( packet[2] << 8 ) | packet[3];

        for ( int i = 0; i < Items.Count; i++ )
        {
            SoundFilterEntry entry = Items[i];

            if ( entry.Enabled && entry.SoundIDs != null && Array.IndexOf( entry.SoundIDs, soundId ) >= 0 )
            {
                return true;
            }
        }

        return false;
    }
}

public class SoundFilterEntry : SetPropertyNotifyChanged
{
    public string Category
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool DefaultEnabled { get; set; }

    public bool Enabled
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool IsExpanded
    {
        get;
        set => SetProperty( ref field, value );
    } = true;

    public string LocalizedName
    {
        get;
        set => SetProperty( ref field, value );
    }

    public string Name { get; set; }
    public int[] SoundIDs { get; set; }
}
