#region License

// Copyright (C) 2022 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY

#endregion

using System.Collections.Generic;
using System.Linq;
using ClassicAssist.Shared.UI;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.UI.ViewModels;

/// <summary>
///     One entity as the collection viewer shows it: its art, its display name, and its properties.
///     <para>
///         The WPF build exposes the art as a <c>System.Windows.Media.ImageSource</c>. This one stops at
///         <see cref="Pixmap" /> so the type stays usable from the plugin process, which has no UI
///         framework at all; the view converts it on the way to the screen.
///     </para>
/// </summary>
public class EntityCollectionData : SetPropertyNotifyChanged
{
    private readonly Dictionary<int, Pixmap> _cache = [];
    private bool _isLocked;

    public Entity Entity { get; set; }

    public string FullName => GetProperties( Entity );

    public bool IsCoin => Entity?.ID is 0x0EEA or 0x0EED or 0x0EF0;

    // Persisted to Options.LockedItems (see EntityCollectionViewerViewModel.ContextToggleLock), so a
    // lock survives both a Rebuild() and closing/reopening the window. Needs to raise PropertyChanged
    // - the padlock overlay in the ListBox item template binds to it directly, and ContextToggleLock
    // flips it on rows already on screen rather than rebuilding them.
    public bool IsLocked
    {
        get => _isLocked;
        set => SetProperty( ref _isLocked, value );
    }

    public string Name => GetName( Entity );

    /// <summary>
    ///     Re-raises change notification for the computed, entity-derived properties. Called when the
    ///     underlying entity's name/properties/hue are updated after the row was created (e.g. an OPL
    ///     packet arriving after the item was added to the viewer).
    /// </summary>
    public void NotifyPropertiesUpdated()
    {
        OnPropertyChanged( nameof( Name ) );
        OnPropertyChanged( nameof( FullName ) );
        OnPropertyChanged( nameof( Pixmap ) );
    }

    public Pixmap Pixmap
    {
        get
        {
            int id = Entity.ID;

            // Gold, silver and copper draw as a single coin, a small pile or a large pile depending on
            // how many are in the stack.
            if ( IsCoin && Entity is Item coin )
            {
                if ( coin.Count > 5 )
                {
                    id += 2;
                }
                else if ( coin.Count > 1 )
                {
                    id += 1;
                }
            }

            int key = ( id << 16 ) | Entity.Hue;

            if ( _cache.TryGetValue( key, out Pixmap cached ) )
            {
                return cached;
            }

            Pixmap result = Art.GetStatic( id, Entity.Hue );

            // A mount in the equipment layer has its own item ID, which draws as nothing useful. The
            // lookup maps it to the statue graphic the client would show.
            if ( Entity is Item item && item.Layer == Layer.Mount &&
                 ( EntityCollectionViewerViewModel.MountIDEntries.Value?.TryGetValue( Entity.ID,
                     out int mountId ) ?? false ) )
            {
                result = Art.GetStatic( mountId, Entity.Hue );
            }

            _cache.Add( key, result );

            return result;
        }
    }

    private static string GetProperties( Entity entity )
    {
        return entity.Properties == null
            ? GetName( entity )
            : entity.Properties
                .Aggregate( "", ( current, entityProperty ) => current + entityProperty.Text + "\r\n" )
                .TrimEnd( '\r', '\n' );
    }

    private static string GetName( Entity entity )
    {
        if ( entity is not Item item || item.Layer != Layer.Mount )
        {
            return entity.Name;
        }

        if ( !( EntityCollectionViewerViewModel.MountIDEntries.Value?.TryGetValue( entity.ID, out int id ) ??
                false ) || id == 0 )
        {
            return entity.Name;
        }

        StaticTile tileData = TileData.GetStaticTile( id );

        return !string.IsNullOrEmpty( tileData.Name ) ? tileData.Name : entity.Name;
    }
}
