using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClassicAssist.Launcher.Models;
using ClassicAssist.Launcher.Windows.Interop;

namespace ClassicAssist.Launcher.Windows
{
    /// <summary>
    ///     Windows taskbar jump list support, reimplemented directly against the Win32 Shell COM API
    ///     (see Interop/ShellInterop.cs) since there is no Avalonia equivalent of WPF's
    ///     System.Windows.Shell.JumpList. No-op on every other OS.
    ///     <para>
    ///         Each played shard becomes a "Shards" category entry that relaunches this Launcher with
    ///         "--shard &lt;name&gt;" (see App.OnFrameworkInitializationCompleted), which skips the main
    ///         window entirely and launches the client directly.
    ///     </para>
    /// </summary>
    public static class JumpListService
    {
        private const string CATEGORY_NAME = "Shards";

        public static void Update( ShardManager shardManager )
        {
            if ( !OperatingSystem.IsWindows() )
            {
                return;
            }

            try
            {
                UpdateCore( shardManager );
            }
            catch ( Exception )
            {
                // Best effort - the jump list is a convenience, never block launching the client on it.
            }
        }

        [SupportedOSPlatform( "windows" )]
        private static void UpdateCore( ShardManager shardManager )
        {
            string exePath = Environment.ProcessPath;

            if ( string.IsNullOrEmpty( exePath ) )
            {
                return;
            }

            string workingDirectory = Path.GetDirectoryName( exePath );

            List<ShardEntry> playedShards = shardManager.VisibleShards
                .Where( e => e.LastPlayed != default )
                .OrderBy( e => e.LastPlayed )
                .ToList();

            ICustomDestinationList destinationList = CreateInstance<ICustomDestinationList>( ShellGuids.CLSID_DestinationList );
            IObjectCollection collection = CreateInstance<IObjectCollection>( ShellGuids.CLSID_EnumerableObjectCollection );

            try
            {
                Guid iidObjectArray = ShellGuids.IID_IObjectArray;
                destinationList.BeginList( out _, ref iidObjectArray, out _ );

                foreach ( ShardEntry shard in playedShards )
                {
                    IShellLinkW link = CreateInstance<IShellLinkW>( ShellGuids.CLSID_ShellLink );

                    try
                    {
                        link.SetPath( exePath );
                        link.SetArguments( $"--shard \"{shard.Name}\"" );
                        link.SetWorkingDirectory( workingDirectory );
                        link.SetIconLocation( exePath, 0 );

                        IPropertyStore propertyStore = (IPropertyStore) link;
                        PropertyKey titleKey = PropertyKey.Title;

                        using ( PropVariant title = PropVariant.FromString( shard.Name ) )
                        {
                            propertyStore.SetValue( ref titleKey, title );
                            propertyStore.Commit();
                        }

                        collection.AddObject( link );
                    }
                    finally
                    {
                        Marshal.ReleaseComObject( link );
                    }
                }

                destinationList.AppendCategory( CATEGORY_NAME, (IObjectArray) collection );
                destinationList.CommitList();
            }
            catch
            {
                destinationList.AbortList();
                throw;
            }
            finally
            {
                Marshal.ReleaseComObject( collection );
                Marshal.ReleaseComObject( destinationList );
            }
        }

        [SupportedOSPlatform( "windows" )]
        private static T CreateInstance<T>( Guid clsid )
        {
            Type type = Type.GetTypeFromCLSID( clsid ) ?? throw new InvalidOperationException( $"COM class {clsid} is not registered." );

            return (T) Activator.CreateInstance( type );
        }
    }
}
