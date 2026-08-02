#region License
// Copyright (C) 2025 Reetus
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY
#endregion

using System.Runtime.InteropServices;

namespace ClassicAssist.Shared
{
    public static class NativeMethods
    {
        // Prefix for the per-run AppUserModelID; the process id of the game run is appended so each
        // game instance + its assistant UI group as their own taskbar button (multiboxing stays
        // separate) instead of every run collapsing into one.
        private const string APP_USER_MODEL_ID_PREFIX = "ClassicAssist.Avalonia";

        [DllImport( "shell32.dll" )]
        public static extern int SetCurrentProcessExplicitAppUserModelID( [MarshalAs( UnmanagedType.LPWStr )] string AppID );

        /// <summary>
        ///     Assigns a per-run AppUserModelID to every window in this process. Call from both the
        ///     plugin (which runs inside the game process) and the UI process, passing the game's
        ///     process id so the client and its assistant group into one taskbar button on Windows.
        ///     No-op on other platforms.
        /// </summary>
        public static void SetAppUserModelId( int gameProcessId )
        {
            if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
            {
                SetCurrentProcessExplicitAppUserModelID( $"{APP_USER_MODEL_ID_PREFIX}.{gameProcessId}" );
            }
        }
    }
}