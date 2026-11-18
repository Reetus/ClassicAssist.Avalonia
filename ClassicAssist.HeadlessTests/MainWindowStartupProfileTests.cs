using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using ClassicAssist.Avalonia;
using ClassicAssist.Avalonia.Views;
using ClassicAssist.Data;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.UI.ViewModels;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.HeadlessTests;

/// <summary>
///     Opening the main window must load the last profile into every tab
///     (<see cref="AssistantOptions.OnWindowLoaded" />, reached through <c>WindowInitializedBehaviour</c>),
///     and the save on close must then write it back intact.
///     <para>
///         Xaml.Behaviors 12 attaches the behaviour while MainWindow.axaml is still being populated:
///         <c>Initialized</c> has already fired by then, and the tab view models - the settings
///         providers - do not exist yet. Hooking either moment left every tab un-deserialized, so
///         switching profile threw from <c>HotkeysTabViewModel.ClearAllHotkeys</c> and the save on
///         close rewrote the profile without its Options, Hotkeys and Friends sections.
///     </para>
/// </summary>
public class MainWindowStartupProfileTests
{
    private const string PROFILE = "Startup.json";

    [Fact]
    public Task OpeningTheMainWindowLoadsTheProfileIntoEveryTab()
    {
        return Headless.Run( () =>
        {
            string originalStartupPath = Engine.StartupPath;
            string originalLastProfile = AssistantOptions.LastProfile;
            string tempDirectory = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );

            // SpellManager (used by the hotkeys tab) reads Data\ under the startup path.
            CopyDirectory( Path.Combine( AppContext.BaseDirectory, "Data" ), Path.Combine( tempDirectory, "Data" ) );
            Directory.CreateDirectory( Path.Combine( tempDirectory, "Profiles" ) );

            Engine.StartupPath = tempDirectory;
            AssistantOptions.LastProfile = PROFILE;

            int providersAtLoad = -1;

            void OnLoad( JObject json, Options options )
            {
                providersAtLoad = BaseViewModel.Instances.Count( i => i is ISettingProvider );
            }

            StringWriter console = new();
            TextWriter originalOut = Console.Out;

            Options.LoadEvent += OnLoad;

            try
            {
                // Same sequence as App.OnFrameworkInitializationCompleted.
                UiHost.Initialize();

                MainWindow window = new() { DataContext = new MainWindowViewModel() };

                // Written after the window exists so a save it might trigger cannot overwrite it
                // first; the load runs on Show.
                string profilePath = Path.Combine( tempDirectory, "Profiles", PROFILE );
                WriteProfile( profilePath );

                Console.SetOut( console );

                window.Show();
                Headless.Settle();

                Assert.True( providersAtLoad > 0, "The profile was loaded before any settings provider existed." );

                Options.Save( Options.CurrentOptions );

                Console.SetOut( originalOut );

                // An un-deserialized provider fails to save with a NullReferenceException. Other
                // failures are not this bug: earlier tests in the assembly leave second live
                // instances of some providers (ScreenshotTabViewModel, MacrosTabViewModel), and those
                // collide on their settings key with an ArgumentException.
                Assert.DoesNotContain( "NullReferenceException", console.ToString() );

                JObject saved = JObject.Parse( File.ReadAllText( profilePath ) );

                Assert.NotNull( saved["Hotkeys"] );
                Assert.NotNull( saved["Friends"] );
                Assert.NotNull( saved["Macros"] );

                // Checked on this window's own macros tab rather than in the saved file, where a
                // leftover instance from another test may have written the section first.
                MacrosTabViewModel macros = window.GetLogicalDescendants().OfType<Control>()
                    .Select( c => c.DataContext ).OfType<MacrosTabViewModel>().First();

                Assert.Contains( macros.Items, m => m.Name == "Startup macro" );
            }
            finally
            {
                Console.SetOut( originalOut );
                Options.LoadEvent -= OnLoad;
                Engine.StartupPath = originalStartupPath;
                AssistantOptions.LastProfile = originalLastProfile;

                try
                {
                    Directory.Delete( tempDirectory, true );
                }
                catch ( IOException )
                {
                    // Best effort; the directory is under the temp path.
                }
            }
        } );
    }

    private static void WriteProfile( string path )
    {
        JObject profile = new()
        {
            ["Name"] = PROFILE,
            ["Macros"] = new JObject
            {
                ["Macros"] = new JArray
                {
                    new JObject { ["Name"] = "Startup macro", ["Macro"] = "SysMessage(\"hi\")" }
                }
            }
        };

        File.WriteAllText( path, profile.ToString() );
    }

    private static void CopyDirectory( string source, string destination )
    {
        Directory.CreateDirectory( destination );

        foreach ( string file in Directory.GetFiles( source ) )
        {
            File.Copy( file, Path.Combine( destination, Path.GetFileName( file ) ) );
        }

        foreach ( string directory in Directory.GetDirectories( source ) )
        {
            CopyDirectory( directory, Path.Combine( destination, Path.GetFileName( directory ) ) );
        }
    }
}
