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
using System.Threading.Tasks;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.Data.Screenshot;

/// <summary>
///     Lets the packet handlers and the macro/hotkey commands reach the screenshot tab without
///     depending on it, the same shape the other agents use.
/// </summary>
public class ScreenshotManager
{
    private static ScreenshotManager _instance;
    private static readonly object _instanceLock = new();

    private ScreenshotManager()
    {
    }

    public Action<Mobile> OnMobileDeath { get; set; }
    public Action<string> OnPlayerDeath { get; set; }

    /// <summary>
    ///     Takes a screenshot and resolves to the file written, or null if it could not be taken. Two
    ///     differences from upstream: it is asynchronous, since the pixels come from the client over RPC
    ///     rather than from a GDI call in this process, and there is no fullscreen argument - what is
    ///     captured is the frame the client drew, so there is no desktop to include.
    /// </summary>
    public Func<string, string, Task<string>> TakeScreenshot { get; set; }

    public static ScreenshotManager GetInstance()
    {
        // ReSharper disable once InvertIf
        if ( _instance == null )
        {
            lock ( _instanceLock )
            {
                _instance ??= new ScreenshotManager();
            }
        }

        return _instance;
    }
}
