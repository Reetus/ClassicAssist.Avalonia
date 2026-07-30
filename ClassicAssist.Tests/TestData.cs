using System;
using System.IO;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Locates a Ultima Online installation for the tests that need real .mul/.uop data.
    ///     <para>
    ///         Those tests are skipped rather than failed when no install is present, so the suite still runs
    ///         on CI. Set <c>UO_DATA_PATH</c> to point at one.
    ///     </para>
    /// </summary>
    internal static class TestData
    {
        private static readonly Lazy<string> _lazyPath = new Lazy<string>( Locate );

        /// <summary>Path to a UO installation, or null if none was found.</summary>
        internal static string UOPath => _lazyPath.Value;

        internal static bool HasUOData => UOPath != null;

        private static string Locate()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable( "UO_DATA_PATH" );

            string[] candidates =
            {
                fromEnvironment, "/home/reetus/UO106", @"D:\Games\Ultima Online\",
                @"C:\Program Files (x86)\Electronic Arts\Ultima Online Classic"
            };

            foreach ( string candidate in candidates )
            {
                if ( !string.IsNullOrEmpty( candidate ) && Directory.Exists( candidate ) &&
                     File.Exists( Path.Combine( candidate, "tiledata.mul" ) ) )
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
