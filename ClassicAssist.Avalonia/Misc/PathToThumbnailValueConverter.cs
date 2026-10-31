#region License

// Copyright (C) 2026 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY

#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     A file path to a thumbnail of it, decoded at the width given as the converter parameter.
///     <para>
///         The gallery lists every screenshot ever taken, and decoding those at full size would hold a
///         desktop-sized bitmap per row - so they are decoded down instead, and cached by path so
///         scrolling does not re-decode. The cache is keyed on the file's write time too, so replacing a
///         file on disk still refreshes.
///     </para>
///     <para>
///         The cache is bounded at <see cref="MAX_CACHE_ENTRIES" /> thumbnails, evicting the
///         least-recently-used entry first: every entry pins a decoded native bitmap, and a gallery that
///         only ever grows would otherwise hold one per screenshot for the life of the process. Evicted
///         bitmaps are dropped for the GC to reclaim rather than disposed (see
///         <see cref="EvictLeastRecentlyUsed" /> for why disposing is unsafe here), and the file stream
///         each thumbnail is decoded from is disposed as soon as decoding finishes.
///     </para>
/// </summary>
public class PathToThumbnailValueConverter : IValueConverter
{
    private const int DEFAULT_WIDTH = 256;

    // A width-256 thumbnail is ~150 KB decoded, so this is tens of megabytes at worst - a cap sized
    // for a few screens of gallery, not for every screenshot ever taken.
    private const int MAX_CACHE_ENTRIES = 256;

    private static readonly Lock _cacheLock = new();

    // Keyed path|width|write-time ticks: a replaced file changes the key and so gets a fresh decode,
    // leaving the stale entry to age out through the eviction below.
    private static readonly Dictionary<string, CacheEntry> _cache = [];

    // Monotonic stamp behind the least-recently-used eviction.
    private static long _accessCounter;

    /// <summary>
    ///     A cached thumbnail and when it was last handed out.
    /// </summary>
    private sealed class CacheEntry
    {
        public CacheEntry( Bitmap bitmap, long lastAccess )
        {
            Bitmap = bitmap;
            LastAccess = lastAccess;
        }

        public Bitmap Bitmap { get; }

        public long LastAccess { get; set; }
    }

    public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
    {
        if ( value is not string path || !File.Exists( path ) )
        {
            return null;
        }

        int width = parameter is string text && int.TryParse( text, out int parsed ) ? parsed : DEFAULT_WIDTH;

        string key = $"{path}|{width}|{File.GetLastWriteTimeUtc( path ).Ticks}";

        lock ( _cacheLock )
        {
            if ( _cache.TryGetValue( key, out CacheEntry cached ) )
            {
                cached.LastAccess = ++_accessCounter;
                return cached.Bitmap;
            }
        }

        Bitmap thumbnail;

        try
        {
            // The stream is only the source of the pixels; the bitmap has copied them out by the
            // time the using declaration disposes it at the end of the try block.
            using FileStream stream = File.OpenRead( path );

            thumbnail = Bitmap.DecodeToWidth( stream, width );
        }
        catch ( Exception )
        {
            // A half-written or corrupt file - the row just shows no image.
            return null;
        }

        lock ( _cacheLock )
        {
            if ( _cache.TryGetValue( key, out CacheEntry existing ) )
            {
                // Another conversion raced the same thumbnail - keep the first decode.
                existing.LastAccess = ++_accessCounter;
                thumbnail.Dispose();
                return existing.Bitmap;
            }

            CacheEntry entry = new( thumbnail, ++_accessCounter );
            _cache.Add( key, entry );
            EvictLeastRecentlyUsed();
        }

        return thumbnail;
    }

    public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }

    /// <summary>
    ///     Drops least-recently-used entries until the cache is back at its cap. The evicted bitmap is
    ///     released for the GC to reclaim, NOT disposed - see the comment at the dispose site for why
    ///     disposing it could break a live binding. The caller must hold <see cref="_cacheLock" />.
    /// </summary>
    private static void EvictLeastRecentlyUsed()
    {
        while ( _cache.Count > MAX_CACHE_ENTRIES )
        {
            string oldestKey = null;
            CacheEntry oldest = null;

            foreach ( KeyValuePair<string, CacheEntry> kvp in _cache )
            {
                if ( oldest is null || kvp.Value.LastAccess < oldest.LastAccess )
                {
                    oldestKey = kvp.Key;
                    oldest = kvp.Value;
                }
            }

            _cache.Remove( oldestKey );

            // Deliberately NOT disposing the evicted bitmap. Dropping the reference is enough to
            // stop the cache growing without bound (the bug this fixes), and the native memory is
            // reclaimed by finalizers: Avalonia.Media.Imaging.Bitmap itself has no finalizer, but
            // SkiaSharp.SKNativeObject, Avalonia.Platform.Internal.UnmanagedBlob and
            // Avalonia.Media.Imaging.BitmapMemory all do (verified by reflection over the shipped
            // assemblies). Disposing here would be unsafe, because the evicted Bitmap may still be
            // held by a live Image.Source binding - ScreenshotTabControl binds
            // `{Binding Path, Converter=ThumbnailConverter}` and the binding keeps the instance.
            // Avoiding that would depend on the ListBox staying virtualised so that off-screen
            // containers are recycled before their entry can age out, which is true today but is an
            // invariant nothing in the XAML pins down. Dropping the reference removes the whole
            // class of use-after-dispose for the cost of finalizer-driven rather than deterministic
            // reclamation. Note the duplicate-decode case above DOES dispose, and that is safe:
            // that instance was never handed to any caller.
        }
    }
}
