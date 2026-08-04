#region License

// Copyright (C) 2026 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ClassicAssist.Misc
{
    /// <summary>
    ///     Opening URLs, folders and editors, per platform.
    ///     <para>
    ///         The WPF tree shells out to <c>explorer.exe</c> and <c>cmd /c code</c>, and calls
    ///         <c>Process.Start( url )</c> directly - the latter throws on .NET Core, where
    ///         <see cref="ProcessStartInfo.UseShellExecute" /> defaults to false and the URL is treated as
    ///         a filename. Everything here goes through the platform's own opener instead.
    ///     </para>
    /// </summary>
    public static class ShellLauncher
    {
        /// <summary>Command names VS Code registers on PATH, most specific first.</summary>
        private static readonly string[] _vsCodeCommands = { "code", "codium", "code-insiders" };

        /// <summary>
        ///     Opens a URL in the default browser. On Linux <c>UseShellExecute</c> maps to xdg-open, which
        ///     is what we want; it is set explicitly because the .NET Core default is false.
        /// </summary>
        public static bool OpenUrl( string url )
        {
            if ( string.IsNullOrWhiteSpace( url ) )
            {
                return false;
            }

            return TryStart( new ProcessStartInfo( url ) { UseShellExecute = true } ) != null;
        }

        /// <summary>Opens a folder in the platform's file manager, creating it first if necessary.</summary>
        public static bool OpenFolder( string path )
        {
            if ( string.IsNullOrWhiteSpace( path ) )
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory( path );
            }
            catch ( Exception )
            {
                // Fall through - the opener will fail visibly enough.
            }

            return TryStart( new ProcessStartInfo( path ) { UseShellExecute = true } ) != null;
        }

        /// <summary>
        ///     Opens <paramref name="path" /> in VS Code, falling back to the default handler for the file
        ///     type when VS Code isn't installed.
        /// </summary>
        /// <param name="wait">
        ///     Pass <c>code --wait</c> and return the process, so the caller can read the file back after
        ///     the editor tab is closed. Null when VS Code wasn't found (the fallback can't be waited on -
        ///     the default handler usually returns immediately, having handed off to a running instance).
        /// </param>
        public static Process OpenInVSCode( string path, bool wait = false )
        {
            string quoted = $"\"{path}\"";

            foreach ( string command in _vsCodeCommands )
            {
                // Windows registers `code` as code.cmd, which CreateProcess won't run directly.
                ProcessStartInfo psi = RuntimeInformation.IsOSPlatform( OSPlatform.Windows )
                    ? new ProcessStartInfo( "cmd.exe", $"/c {command} {( wait ? "--wait " : "" )}{quoted}" )
                    {
                        UseShellExecute = false, CreateNoWindow = true
                    }
                    : new ProcessStartInfo( command, $"{( wait ? "--wait " : "" )}{quoted}" )
                    {
                        UseShellExecute = false
                    };

                Process process = TryStart( psi );

                if ( process != null )
                {
                    return process;
                }
            }

            // No VS Code - let the desktop decide what opens a .py file.
            TryStart( new ProcessStartInfo( path ) { UseShellExecute = true } );

            return null;
        }

        /// <summary>Waits for <paramref name="process" /> to exit without blocking the caller's thread.</summary>
        public static Task WaitForExitAsync( Process process )
        {
            if ( process == null )
            {
                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            process.EnableRaisingEvents = true;
            process.Exited += ( _, _ ) => tcs.TrySetResult( true );

            // Exited only fires for a process still running when the handler was attached.
            if ( process.HasExited )
            {
                tcs.TrySetResult( true );
            }

            return tcs.Task;
        }

        private static Process TryStart( ProcessStartInfo psi )
        {
            try
            {
                return Process.Start( psi );
            }
            catch ( Exception )
            {
                // Not installed, or no handler registered - the caller falls through to its next option.
                return null;
            }
        }
    }
}
