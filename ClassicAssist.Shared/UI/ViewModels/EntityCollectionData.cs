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
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.UI.ViewModels
{
    /// <summary>
    ///     One entity as the collection viewer shows it: its art, its display name, and its properties.
    ///     <para>
    ///         The WPF build exposes the art as a <c>System.Windows.Media.ImageSource</c>. This one stops at
    ///         <see cref="Pixmap" /> so the type stays usable from the plugin process, which has no UI
    ///         framework at all; the view converts it on the way to the screen.
    ///     </para>
    /// </summary>
    public class EntityCollectionData
    {
        private readonly Dictionary<int, Pixmap> _cache = new Dictionary<int, Pixmap>();

        public Entity Entity { get; set; }

        public string FullName => GetProperties( Entity );

        public bool IsCoin => Entity?.ID == 0x0EEA || Entity?.ID == 0x0EED || Entity?.ID == 0x0EF0;

        public string Name => GetName( Entity );

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
            if ( !( entity is Item item ) || item.Layer != Layer.Mount )
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
}
