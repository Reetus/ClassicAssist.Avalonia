using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Shared.Resources;
using UOC = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.Data.Hotkeys.Commands;

[HotkeyCommand( Name = "Take Snapshot", Category = "Commands" )]
public sealed class SnapshotCommand : HotkeyCommand
{
    public override void Execute()
    {
        (bool result, string fileName) = MainCommands.Snapshot();

        if ( result )
        {
            UOC.SystemMessage( string.Format( Strings.Snapshot_Saved___0_, fileName ) );
        }
    }
}
