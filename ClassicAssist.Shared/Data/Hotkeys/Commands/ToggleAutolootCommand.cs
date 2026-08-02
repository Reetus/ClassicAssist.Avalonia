using ClassicAssist.Data.Macros.Commands;

namespace ClassicAssist.Data.Hotkeys.Commands
{
    [HotkeyCommand( Name = "Toggle Autoloot", Category = "Agents" )]
    public sealed class ToggleAutolootCommand : HotkeyCommand
    {
        public override void Execute()
        {
            AgentCommands.SetAutoloot();
        }
    }
}
