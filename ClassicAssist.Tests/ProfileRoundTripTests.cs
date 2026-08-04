using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClassicAssist.Data;
using ClassicAssist.Data.Dress;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Data.Scavenger;
using ClassicAssist.Data.Skills;
using ClassicAssist.Misc;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UI.ViewModels.Agents;
using ClassicAssist.UI.Misc;
using ClassicAssist.UI.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Verifies that the profile save path (Options.Save -> every ISettingProvider.Serialize)
    ///     reproduces the profile that was loaded in, i.e. that loading a real profile and saving it
    ///     back out is lossless. The reference profile lives in ClassicAssist.Tests/data/profile.json
    ///     and is intentionally not committed to the repo, so the tests bail out (inconclusive) when
    ///     it isn't present.
    /// </summary>
    [TestClass]
    public class ProfileRoundTripTests
    {
        /// <summary>
        ///     Dot-notation paths excluded from the input-vs-output comparison. These are blocks/fields
        ///     the Avalonia port doesn't persist yet (or intentionally never will) and are deferred
        ///     rather than treated as round-trip failures. Remove an entry as each one gets ported.
        ///     <list type="bullet">
        ///         <item><c>*</c> matches any object property or array element (e.g. <c>Autoloot.Items.*.Group</c>).</item>
        ///         <item><c>[Name=value]</c> matches an array element whose <c>Name</c> (or other) property equals value
        ///         (e.g. <c>General.Filters.[Name=ClassicAssist.Data.Filters.SoundFilter]</c>).</item>
        ///     </list>
        /// </summary>
        private static readonly string[] DeferredPaths =
        {
            // Written by the WPF Options.Save wrapper, not by any setting provider.
            "Hash",

            // Skills moved to the global Skills.json store and need UO data files to build.
            "Skills",

            // Agents/blocks not yet ported to Avalonia.
            "OrganizerOptions",
            "NameOverride",
            "Screenshot",
            "UseOnceAgent",
            "Hotkeys.Options",

            // General: WPF-only settings.
            "General.DragDelay",
            "General.DragDelayMS",
            "General.SysTray",
            "General.SlowHandlerThreshold",
            "General.Autologin",
            "General.AutologinUsername",
            "General.AutologinPassword",
            "General.AutologinServerIndex",
            "General.AutologinCharacterIndex",
            "General.AutologinConnectDelay",
            "General.AutologinReconnectDelay",

            // Filters: SoundFilter/ItemIDFilter not ported; BardsMusicFilter is Avalonia-only.
            "General.Filters.[Name=ClassicAssist.Data.Filters.SoundFilter]",
            "General.Filters.[Name=ClassicAssist.Data.Filters.ItemIDFilter]",
            "General.Filters.[Name=ClassicAssist.Data.Filters.BardsMusicFilter]",

            // Filters: SeasonFilter isn't IConfigurableFilter in Avalonia yet, and ClilocFilter only
            // persists Key/Value (WPF also writes Hue and ShowOverhead).
            "General.Filters.[Name=ClassicAssist.Data.Filters.SeasonFilter].Options",
            "General.Filters.[Name=ClassicAssist.Data.Filters.ClilocFilter].Options.Filters.*.Hue",
            "General.Filters.[Name=ClassicAssist.Data.Filters.ClilocFilter].Options.Filters.*.ShowOverhead",

            // Options: WPF-only settings.
            "Options.LimitHotkeyTrigger",
            "Options.LimitHotkeyTriggerMS",
            "Options.ChatWindowHeight",
            "Options.ChatWindowWidth",
            "Options.ChatWindowRightColumn",
            "Options.ExpireTargetsMS",
            "Options.LogoutDisconnectedPrompt",

            // Hotkeys: ShowChatWindowCommand not ported; Avalonia writes Disableable for spells.
            "Hotkeys.Commands.[Type=ClassicAssist.Data.Hotkeys.Commands.ShowChatWindowCommand]",
            "Hotkeys.Spells.*.Disableable",

            // Macros: WPF UI state.
            "Macros.LeftColumnWidth",
            "Macros.Groups",
            "Macros.PlayerAliases",
            "Macros.Macros.*.Group",
            "Macros.Macros.*.Metadata",

            // VendorSell: only written by WPF when non-zero.
            "VendorSell.ContainerSerial",

            // Autoloot: WPF UI state not ported.
            "Autoloot.LeftColumnWidth"
        };

        private string _originalStartupPath;
        private string _originalProfileDirectory;
        private string _originalGlobalDirectory;
        private Options _originalCurrentOptions;
        private string _tempDir;

        [TestInitialize]
        public void SetUp()
        {
            _originalStartupPath = Engine.StartupPath;
            _originalProfileDirectory = AssistantOptions.ProfileDirectory;
            _originalGlobalDirectory = AssistantOptions.GlobalDirectory;
            _originalCurrentOptions = Options.CurrentOptions;

            Engine.StartupPath = AppContext.BaseDirectory;

            _tempDir = Path.Combine( Path.GetTempPath(), "ClassicAssistProfileRoundTrip", Guid.NewGuid().ToString( "N" ) );
            AssistantOptions.ProfileDirectory = Path.Combine( _tempDir, "Profiles" );
            AssistantOptions.GlobalDirectory = Path.Combine( _tempDir, "Global" );

            Options.CurrentOptions = new Options();
        }

        [TestCleanup]
        public void TearDown()
        {
            Options.CurrentOptions = _originalCurrentOptions;

            AssistantOptions.ProfileDirectory = _originalProfileDirectory;
            AssistantOptions.GlobalDirectory = _originalGlobalDirectory;
            Engine.StartupPath = _originalStartupPath;

            ResetSingletonState();

            try
            {
                Directory.Delete( _tempDir, true );
            }
            catch ( IOException )
            {
                // best effort
            }
            catch ( UnauthorizedAccessException )
            {
                // best effort
            }
        }

        /// <summary>
        ///     The setting providers mutate shared singletons as a side effect of loading a profile.
        ///     Restore them so later tests aren't affected by the profile data (e.g. DressAgentTests
        ///     break if DressManager.UseUO3DPackets is left set from the profile).
        /// </summary>
        private static void ResetSingletonState()
        {
            DressManager dressManager = DressManager.GetInstance();
            dressManager.UseUO3DPackets = false;
            dressManager.IsDressing = false;
            dressManager.Items = new ObservableCollectionEx<DressAgentEntry>();

            HotkeyManager.GetInstance().ClearItems();

            MacroManager.GetInstance().Items.Clear();
            ScavengerManager.GetInstance().Items.Clear();
            SkillManager.GetInstance().Items.Clear();

            AliasCommands._aliases.Clear();
            ActionCommands.UseOnceList.Clear();
        }

        [TestMethod]
        public void SaveProfileRoundTripsFaithfully()
        {
            string profileFile = FindProfileDataFile();

            if ( profileFile == null )
            {
                Assert.Inconclusive( "ClassicAssist.Tests/data/profile.json is not present - skipping round-trip test" );
                return;
            }

            JObject input = JObject.Parse( File.ReadAllText( profileFile ) );

            string profileName = input["Name"]?.ToObject<string>() ?? "profile.json";

            Directory.CreateDirectory( AssistantOptions.ProfileDirectory );
            Directory.CreateDirectory( AssistantOptions.GlobalDirectory );

            File.Copy( profileFile, Path.Combine( AssistantOptions.ProfileDirectory, profileName ), true );

            List<BaseViewModel> providers = InstantiateProviders();

            JObject saved = new JObject();

            void OnOptionsLoad( JObject json, Options options )
            {
                foreach ( BaseViewModel instance in providers )
                {
                    if ( instance is ISettingProvider settingProvider )
                    {
                        settingProvider.Deserialize( json, options );
                    }

                    if ( instance is IGlobalSettingProvider globalSettingProvider )
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
                }
            }

            void OnOptionsSave( JObject obj )
            {
                foreach ( BaseViewModel instance in providers )
                {
                    if ( instance is ISettingProvider settingProvider )
                    {
                        settingProvider.Serialize( obj );
                    }

                    if ( instance is IGlobalSettingProvider globalSettingProvider )
                    {
                        JObject global = new JObject();

                        globalSettingProvider.Serialize( global, true );

                        File.WriteAllText(
                            Path.Combine( AssistantOptions.GetGlobalPath(), globalSettingProvider.GetGlobalFilename() ),
                            global.ToString() );
                    }
                }

                saved = (JObject) obj.DeepClone();
            }

            Options.SaveEvent += OnOptionsSave;
            Options.LoadEvent += OnOptionsLoad;

            try
            {
                Options options = Options.CurrentOptions;
                Options.Load( profileName, options );
                Options.Save( options );
            }
            finally
            {
                Options.SaveEvent -= OnOptionsSave;
                Options.LoadEvent -= OnOptionsLoad;
            }

            JObject expected = (JObject) input.DeepClone();
            JObject actual = (JObject) saved.DeepClone();

            foreach ( string path in DeferredPaths )
            {
                RemovePath( expected, path );
                RemovePath( actual, path );
            }

            List<string> differences = new List<string>();
            CompareTokens( expected, actual, string.Empty, differences );

            Assert.IsTrue( differences.Count == 0,
                "Loading and re-saving the profile did not reproduce the input.\n" +
                $"Deferred paths excluded: {string.Join( ", ", DeferredPaths )}\n\n" +
                string.Join( "\n", differences ) );
        }

        private static string FindProfileDataFile()
        {
            string directory = AppContext.BaseDirectory;

            while ( directory != null )
            {
                string candidate = Path.Combine( directory, "data", "profile.json" );

                if ( File.Exists( candidate ) )
                {
                    return candidate;
                }

                directory = Directory.GetParent( directory )?.FullName;
            }

            return null;
        }

        /// <summary>
        ///     The view models the main window instantiates (and which therefore participate in a
        ///     profile save). Kept explicit so the test tracks what the UI actually wires up.
        /// </summary>
        private static List<BaseViewModel> InstantiateProviders()
        {
            Type[] types =
            {
                typeof( GeneralControlViewModel ),
                typeof( OptionsTabViewModel ),
                typeof( HotkeysTabViewModel ),
                typeof( MacrosTabViewModel ),
                typeof( SkillsTabViewModel ),
                typeof( OrganizerTabViewModel ),
                typeof( DressTabViewModel ),
                typeof( CountersTabViewModel ),
                typeof( FriendsTabViewModel ),
                typeof( VendorBuyTabViewModel ),
                typeof( VendorSellTabViewModel ),
                typeof( ScavengerTabViewModel ),
                typeof( AutolootViewModel )
            };

            return types.Select( t => (BaseViewModel) Activator.CreateInstance( t ) ).ToList();
        }

        private static void RemovePath( JToken token, string path )
        {
            string[] segments = SplitPath( path );

            RemovePathRecursive( token, segments, 0 );
        }

        private static string[] SplitPath( string path )
        {
            List<string> segments = new List<string>();
            int start = 0;
            bool inBracket = false;

            for ( int i = 0; i < path.Length; i++ )
            {
                switch ( path[i] )
                {
                    case '[':
                        inBracket = true;
                        break;
                    case ']':
                        inBracket = false;
                        break;
                    case '.' when !inBracket:
                        segments.Add( path.Substring( start, i - start ) );
                        start = i + 1;
                        break;
                }
            }

            segments.Add( path.Substring( start ) );

            return segments.ToArray();
        }

        private static bool TryKeyArray( JArray array, out string keyField, out Dictionary<string, JToken> map )
        {
            keyField = null;
            map = null;

            foreach ( string candidate in new[] { "Id", "Name", "Type" } )
            {
                bool allKeyed = array.All( e => e is JObject o && o[candidate] != null && o[candidate].Type != JTokenType.Null );

                if ( !allKeyed )
                {
                    continue;
                }

                Dictionary<string, JToken> dict = new Dictionary<string, JToken>();
                bool unique = true;

                foreach ( JToken element in array )
                {
                    string key = element[candidate].ToString();

                    if ( !dict.TryAdd( key, element ) )
                    {
                        unique = false;
                        break;
                    }
                }

                if ( unique )
                {
                    keyField = candidate;
                    map = dict;

                    return true;
                }
            }

            return false;
        }

        private static bool RemovePathRecursive( JToken token, string[] segments, int index )
        {
            if ( token == null || index >= segments.Length )
            {
                return false;
            }

            string segment = segments[index];
            bool isLast = index == segments.Length - 1;

            switch ( token )
            {
                case JObject jobject:
                    if ( segment == "*" )
                    {
                        bool removed = false;

                        foreach ( JProperty property in jobject.Properties().ToList() )
                        {
                            removed |= RemovePathRecursive( property.Value, segments, index + 1 );
                        }

                        return removed;
                    }

                    if ( TryParseValueMatch( segment, out _, out _ ) )
                    {
                        return false;
                    }

                    if ( isLast )
                    {
                        return jobject.Remove( segment );
                    }

                    return RemovePathRecursive( jobject[segment], segments, index + 1 );

                case JArray jarray:
                    if ( segment == "*" )
                    {
                        bool removed = false;

                        foreach ( JToken element in jarray.ToList() )
                        {
                            removed |= RemovePathRecursive( element, segments, index + 1 );
                        }

                        return removed;
                    }

                    if ( TryParseValueMatch( segment, out string propertyName, out string propertyValue ) )
                    {
                        bool removed = false;

                        foreach ( JToken element in jarray.ToList() )
                        {
                            if ( !( element is JObject elementObject ) ||
                                 elementObject[propertyName]?.ToString() != propertyValue )
                            {
                                continue;
                            }

                            if ( isLast )
                            {
                                jarray.Remove( element );
                                removed = true;
                            }
                            else
                            {
                                removed |= RemovePathRecursive( element, segments, index + 1 );
                            }
                        }

                        return removed;
                    }

                    if ( int.TryParse( segment, out int arrayIndex ) && arrayIndex >= 0 && arrayIndex < jarray.Count )
                    {
                        if ( isLast )
                        {
                            jarray.RemoveAt( arrayIndex );

                            return true;
                        }

                        return RemovePathRecursive( jarray[arrayIndex], segments, index + 1 );
                    }

                    return false;
            }

            return false;
        }

        private static bool TryParseValueMatch( string segment, out string propertyName, out string propertyValue )
        {
            propertyName = null;
            propertyValue = null;

            if ( segment.Length < 3 || !segment.StartsWith( "[" ) || !segment.EndsWith( "]" ) )
            {
                return false;
            }

            string inner = segment.Substring( 1, segment.Length - 2 );
            int equalsIndex = inner.IndexOf( '=' );

            if ( equalsIndex <= 0 )
            {
                return false;
            }

            propertyName = inner.Substring( 0, equalsIndex );
            propertyValue = inner.Substring( equalsIndex + 1 );

            return true;
        }

        private static IEnumerable<string> DescribeNameDifferences( string path, JArray expected, JArray actual )
        {
            string Key( JToken token )
            {
                if ( token is JObject jobject )
                {
                    foreach ( string key in new[] { "Name", "Type" } )
                    {
                        JToken value = jobject[key];

                        if ( value != null && value.Type != JTokenType.Null )
                        {
                            return value.ToString();
                        }
                    }
                }

                return null;
            }

            bool IsKeyed( JToken token ) => token is JObject && Key( token ) != null;

            if ( !expected.All( IsKeyed ) || !actual.All( IsKeyed ) )
            {
                yield break;
            }

            HashSet<string> expectedKeys = new HashSet<string>( expected.Select( Key ) );
            HashSet<string> actualKeys = new HashSet<string>( actual.Select( Key ) );

            foreach ( string missing in expectedKeys.Except( actualKeys ) )
            {
                yield return $"{path}: missing in save output: {missing}";
            }

            foreach ( string extra in actualKeys.Except( expectedKeys ) )
            {
                yield return $"{path}: not in input but present in save output: {extra}";
            }
        }

        private static void CompareTokens( JToken expected, JToken actual, string path, List<string> differences )
        {
            if ( expected == null && actual == null )
            {
                return;
            }

            if ( expected == null || actual == null )
            {
                differences.Add( $"{path}: one side null (input={expected} save={actual})" );

                return;
            }

            if ( expected.Type == JTokenType.Object || actual.Type == JTokenType.Object )
            {
                if ( expected.Type != JTokenType.Object || actual.Type != JTokenType.Object )
                {
                    differences.Add( $"{path}: type mismatch (input={expected.Type} save={actual.Type})" );

                    return;
                }

                foreach ( JProperty property in ( (JObject) expected ).Properties() )
                {
                    string childPath = string.IsNullOrEmpty( path ) ? property.Name : $"{path}.{property.Name}";
                    JToken actualValue = ( (JObject) actual ).Property( property.Name )?.Value;

                    if ( actualValue == null )
                    {
                        differences.Add( $"{childPath}: missing in save output (input={property.Value})" );
                    }
                    else
                    {
                        CompareTokens( property.Value, actualValue, childPath, differences );
                    }
                }

                foreach ( JProperty property in ( (JObject) actual ).Properties() )
                {
                    if ( ( (JObject) expected ).Property( property.Name ) == null )
                    {
                        string childPath = string.IsNullOrEmpty( path ) ? property.Name : $"{path}.{property.Name}";
                        differences.Add( $"{childPath}: not in input but present in save output ({property.Value})" );
                    }
                }

                return;
            }

            if ( expected.Type == JTokenType.Array || actual.Type == JTokenType.Array )
            {
                if ( expected.Type != JTokenType.Array || actual.Type != JTokenType.Array )
                {
                    differences.Add( $"{path}: type mismatch (input={expected.Type} save={actual.Type})" );

                    return;
                }

                JArray expectedArray = (JArray) expected;
                JArray actualArray = (JArray) actual;

                // Compare arrays of objects by a stable key rather than by position: the save output
                // can legitimately order entries differently (e.g. hotkey commands are enumerated in
                // assembly type-discovery order), so positional comparison would report false diffs.
                if ( TryKeyArray( expectedArray, out string keyField, out Dictionary<string, JToken> expectedByKey ) &&
                     TryKeyArray( actualArray, out _, out Dictionary<string, JToken> actualByKey ) )
                {
                    foreach ( KeyValuePair<string, JToken> kvp in expectedByKey )
                    {
                        string childPath = $"{path}[{keyField}={kvp.Key}]";

                        if ( !actualByKey.TryGetValue( kvp.Key, out JToken actualValue ) )
                        {
                            differences.Add( $"{childPath}: missing in save output" );
                        }
                        else
                        {
                            CompareTokens( kvp.Value, actualValue, childPath, differences );
                        }
                    }

                    foreach ( KeyValuePair<string, JToken> kvp in actualByKey )
                    {
                        if ( !expectedByKey.ContainsKey( kvp.Key ) )
                        {
                            differences.Add( $"{path}[{keyField}={kvp.Key}]: not in input but present in save output" );
                        }
                    }

                    return;
                }

                if ( expectedArray.Count != actualArray.Count )
                {
                    differences.Add(
                        $"{path}: array length differs (input={expectedArray.Count} save={actualArray.Count})" );
                    differences.AddRange( DescribeNameDifferences( path, expectedArray, actualArray ) );

                    return;
                }

                for ( int i = 0; i < expectedArray.Count; i++ )
                {
                    CompareTokens( expectedArray[i], actualArray[i], $"{path}[{i}]", differences );
                }

                return;
            }

            // Scalar values: compare by their serialized form, which is what actually gets written to
            // disk. Newtonsoft creates a JValue of type String (with a null value) for a null property
            // added to a JObject, which serialises to JSON null; comparing serialized form avoids
            // false positives against a parsed JSON null that differ only in in-memory token type.
            if ( !expected.ToString( Formatting.None ).Equals( actual.ToString( Formatting.None ) ) )
            {
                differences.Add( $"{path}: value differs (input={expected} save={actual})" );
            }
        }
    }
}
