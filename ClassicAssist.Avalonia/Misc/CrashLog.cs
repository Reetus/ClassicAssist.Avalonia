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
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SEngine = ClassicAssist.Shared.Engine;

namespace ClassicAssist.Avalonia.Misc;

/// <summary>
///     Writes crash logs to a Logs directory next to the UI executable when the process dies on an
///     unhandled exception.
/// </summary>
internal static class CrashLog
{
    private const string LOG_DIRECTORY = "Logs";
    private static readonly Lock Sync = new();

    public static void Log( Exception exception )
    {
        if ( exception == null )
        {
            return;
        }

        try
        {
            string message = BuildMessage( exception );

            lock ( Sync )
            {
                Directory.CreateDirectory( LogDirectory );

                string path = Path.Combine( LogDirectory,
                    $"{DateTime.Now:yyyy-MM-dd_hh-mm-ss}_crash.log" );

                File.AppendAllText( path, message );
            }
        }
        catch
        {
            // The process is already dying; a crash log that cannot be written is not worth dying on.
        }
    }

    private static string LogDirectory => Path.Combine( AppContext.BaseDirectory, LOG_DIRECTORY );

    private static string BuildMessage( Exception exception )
    {
        StringBuilder builder = new();

        builder.AppendLine( "######################## [START LOG] ########################" );
        builder.AppendLine( $"ClassicAssist - {GetAssemblyVersion()} - {DateTime.Now:g}" );
        builder.AppendLine( $"OS: {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}" );
        builder.AppendLine( $"Thread: {Thread.CurrentThread.Name ?? "(unnamed)"}" );
        builder.AppendLine();
        builder.AppendLine( $"Shard: {GetShardName()}" );
        builder.AppendLine( $"ClientVersion: {GetClientVersion()}" );
        builder.AppendLine();
        builder.AppendLine( "Exception:" );
        builder.AppendLine( exception.ToString() );
        builder.Append( "######################## [END LOG] ########################" );

        return builder.ToString();
    }

    private static string GetAssemblyVersion()
    {
        try
        {
            return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string GetShardName()
    {
        try
        {
            return SEngine.CurrentShard?.Name ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string GetClientVersion()
    {
        try
        {
            return SEngine.ClientVersion?.ToString() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
}
