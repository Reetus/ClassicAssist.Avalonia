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

using System;
using System.IO;
using ClassicAssist.Avalonia.Views;
using ClassicAssist.Data;
using ClassicAssist.Misc;
using ClassicAssist.UI.ViewModels;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Avalonia;

/// <summary>
///     Process-wide state for the UI half of ClassicAssist. Replaces the old <c>Assistant.Engine</c>,
///     which doubled as the in-process ClassicUO entry point; that entry point now lives in the
///     separate plugin assembly and this process is a plain Avalonia app.
/// </summary>
public static class UiHost
{
    public static MainWindow MainWindow { get; internal set; }

    public static void Initialize()
    {
        Options.LoadEvent += OnOptionsLoad;
        Options.SaveEvent += OnOptionsSave;
    }

    private static void OnOptionsSave( JObject obj )
    {
        BaseViewModel[] instances = BaseViewModel.Instances;

        foreach ( BaseViewModel instance in instances )
        {
            // Each provider is guarded individually: one bad provider must not abort the whole
            // profile save. Options.Save runs with no caller-side guard - including from
            // MainWindow's OnClosing (via WindowInitializedBehaviour), where an exception is
            // fatal - and is reached from RPC threads as well as the UI thread.
            if ( instance is ISettingProvider settingProvider )
            {
                try
                {
                    settingProvider.Serialize( obj );
                }
                catch ( Exception ex )
                {
                    Console.WriteLine( $"Failed to serialize settings provider {instance.GetType().Name}: {ex}" );
                }
            }

            if ( instance is IGlobalSettingProvider globalSettingProvider )
            {
                try
                {
                    JObject global = [];

                    globalSettingProvider.Serialize( global, true );

                    File.WriteAllText(
                        Path.Combine( AssistantOptions.GetGlobalPath(), globalSettingProvider.GetGlobalFilename() ),
                        global.ToString() );
                }
                catch ( Exception ex )
                {
                    Console.WriteLine(
                        $"Failed to serialize global settings provider {instance.GetType().Name}: {ex}" );
                }
            }
        }
    }

    private static void OnOptionsLoad( JObject json, Options options )
    {
        BaseViewModel[] instances = BaseViewModel.Instances;

        foreach ( BaseViewModel instance in instances )
        {
            // Same per-provider guard on the way back in: a provider that can't deserialize its
            // own slice must not stop the remaining providers from loading the profile.
            if ( instance is ISettingProvider settingProvider )
            {
                try
                {
                    settingProvider.Deserialize( json, options );
                }
                catch ( Exception ex )
                {
                    Console.WriteLine( $"Failed to deserialize settings provider {instance.GetType().Name}: {ex}" );
                }
            }

            if ( instance is IGlobalSettingProvider globalSettingProvider )
            {
                try
                {
                    string filePath =
                        Path.Combine( AssistantOptions.GetGlobalPath(), globalSettingProvider.GetGlobalFilename() );

                    if ( !File.Exists( filePath ) )
                    {
                        continue;
                    }

                    JObject global = JObject.Parse( File.ReadAllText( filePath ) );

                    globalSettingProvider.Deserialize( global, options, true );
                }
                catch ( Exception ex )
                {
                    Console.WriteLine(
                        $"Failed to deserialize global settings provider {instance.GetType().Name}: {ex}" );
                }
            }
        }
    }
}
