using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.Versioning;

namespace ClassicAssist.Misc
{
    /// <summary>
    ///     <c>System.Media.SoundPlayer</c> only implements playback on Windows - everywhere else it throws
    ///     <see cref="PlatformNotSupportedException" />. Outside Windows this shells out to whatever command-line
    ///     player the platform actually ships (PulseAudio's <c>paplay</c>/ALSA's <c>aplay</c> on Linux,
    ///     <c>afplay</c> on macOS) rather than pulling in a native audio dependency for one WAV file.
    /// </summary>
    public static class AudioPlayback
    {
        private static readonly string[] _linuxPlayers = { "paplay", "aplay", "ffplay" };

        public static void Play( string path, bool sync = true )
        {
            if ( OperatingSystem.IsWindows() )
            {
                using SoundPlayer player = new SoundPlayer( path );
                PlayWindows( player, sync );

                return;
            }

            PlayExternal( path, sync );
        }

        public static void Play( Stream stream, bool sync = true )
        {
            if ( OperatingSystem.IsWindows() )
            {
                using SoundPlayer player = new SoundPlayer( stream );
                PlayWindows( player, sync );

                return;
            }

            // The external players below need a real file path, so spool the stream out to one.
            string tempFile = Path.Combine( Path.GetTempPath(), $"classicassist-{Guid.NewGuid():N}.wav" );

            using ( FileStream file = File.Create( tempFile ) )
            {
                stream.CopyTo( file );
            }

            PlayExternal( tempFile, sync, deleteWhenDone: true );
        }

        [SupportedOSPlatform( "windows" )]
        private static void PlayWindows( SoundPlayer player, bool sync )
        {
            if ( sync )
            {
                player.PlaySync();
            }
            else
            {
                player.Play();
            }
        }

        private static void PlayExternal( string path, bool sync, bool deleteWhenDone = false )
        {
            string fileName = OperatingSystem.IsMacOS() ? "afplay" : null;
            string[] candidates = fileName != null ? new[] { fileName } : _linuxPlayers;

            foreach ( string player in candidates )
            {
                Process process = TryStart( player, path );

                if ( process == null )
                {
                    continue;
                }

                if ( sync )
                {
                    process.WaitForExit();

                    if ( deleteWhenDone )
                    {
                        DeleteQuietly( path );
                    }
                }
                else if ( deleteWhenDone )
                {
                    process.EnableRaisingEvents = true;
                    process.Exited += ( _, _ ) => DeleteQuietly( path );
                }

                return;
            }
        }

        private static Process TryStart( string fileName, string path )
        {
            try
            {
                return Process.Start( new ProcessStartInfo( fileName, $"\"{path}\"" )
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                } );
            }
            catch ( Exception )
            {
                // Player not installed; the caller falls through to the next candidate.
                return null;
            }
        }

        private static void DeleteQuietly( string path )
        {
            try
            {
                File.Delete( path );
            }
            catch ( Exception )
            {
                // ignored
            }
        }
    }
}
