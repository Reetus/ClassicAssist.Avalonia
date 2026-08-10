using System;
using System.Collections.Generic;

namespace ClassicAssist.Updater;

public enum UpdaterStage
{
    Initial,
    Install
}

/// <summary>
///     The WPF updater used the CommandLineParser package for these seven options. Parsed by hand here
///     instead - the option names and shapes are unchanged, so the command line
///     <see cref="ClassicAssist.Shared.UI.ViewModels.AboutControlViewModel" /> builds still works, but a
///     standalone updater that must run when the rest of the install is broken is better off with no
///     package dependencies it does not need.
/// </summary>
public class CommandLineOptions
{
    public string CurrentVersion { get; set; }
    public bool Force { get; set; }
    public string Path { get; set; } = string.Empty;
    public int PID { get; set; }
    public UpdaterStage Stage { get; set; } = UpdaterStage.Initial;
    public string UpdatePath { get; set; } = string.Empty;
    public string URL { get; set; }

    public static CommandLineOptions Parse( IReadOnlyList<string> args )
    {
        CommandLineOptions options = new();

        if ( args == null )
        {
            return options;
        }

        for ( int i = 0; i < args.Count; i++ )
        {
            string name = args[i];

            if ( !name.StartsWith( "--", StringComparison.Ordinal ) )
            {
                continue;
            }

            name = name[2..].ToLowerInvariant();

            // --force is the only flag; everything else consumes the next argument.
            if ( name == "force" )
            {
                options.Force = true;
                continue;
            }

            if ( i + 1 >= args.Count )
            {
                continue;
            }

            string value = args[++i];

            switch ( name )
            {
                case "version":
                    options.CurrentVersion = value;
                    break;
                case "path":
                    options.Path = value;
                    break;
                case "pid":
                    options.PID = int.TryParse( value, out int pid ) ? pid : 0;
                    break;
                case "stage":
                    options.Stage = Enum.TryParse( value, true, out UpdaterStage stage )
                        ? stage
                        : UpdaterStage.Initial;

                    break;
                case "updatepath":
                    options.UpdatePath = value;
                    break;
                case "url":
                    options.URL = value;
                    break;
            }
        }

        return options;
    }
}
