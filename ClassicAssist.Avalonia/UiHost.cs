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

using ClassicAssist.Avalonia.Views;
using ClassicAssist.Data;
using ClassicAssist.Misc;
using ClassicAssist.UI.ViewModels;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Avalonia
{
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
                if ( instance is ISettingProvider settingProvider )
                {
                    settingProvider.Serialize( obj );
                }
            }
        }

        private static void OnOptionsLoad( JObject json, Options options )
        {
            BaseViewModel[] instances = BaseViewModel.Instances;

            foreach ( BaseViewModel instance in instances )
            {
                if ( instance is ISettingProvider settingProvider )
                {
                    settingProvider.Deserialize( json, options );
                }
            }
        }
    }
}
