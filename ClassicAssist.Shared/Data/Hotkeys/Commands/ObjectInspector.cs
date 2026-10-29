using System;
using ClassicAssist.UI.ViewModels;

namespace ClassicAssist.Data.Hotkeys.Commands;

[HotkeyCommand( Name = "Object Inspector" )]
public class ObjectInspector : HotkeyCommand
{
    /// <summary>
    ///     Invoked from <c>Task.Run</c> (HotkeyCommand's action / <c>HotkeyManager</c>) with no
    ///     SynchronizationContext, so an exception after the first await would be unhandled on a
    ///     thread-pool thread and terminate the process. Guarded with the same policy as
    ///     <see cref="RelayCommandAsync.Execute" />: ordinary failures are logged and reported,
    ///     fatal faults (see <see cref="CommandExceptionPolicy" />) still propagate.
    /// </summary>
    public override async void Execute()
    {
        try
        {
            await Shared.UO.Commands.InspectObjectAsync();
        }
        catch ( Exception e ) when ( !CommandExceptionPolicy.IsFatal( e ) )
        {
            CommandExceptionPolicy.Report( e );
        }
    }
}
