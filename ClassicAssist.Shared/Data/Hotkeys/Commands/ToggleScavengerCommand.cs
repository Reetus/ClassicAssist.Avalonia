using ClassicAssist.Data.Macros.Commands;

namespace ClassicAssist.Data.Hotkeys.Commands
{
    [HotkeyCommand( Name = "Toggle Scavenger", Category = "Agents" )]
    public sealed class ToggleScavengerCommand : HotkeyCommand
    {
        public override void Execute()
        {
            AgentCommands.SetScavenger();
        }
    }
}
