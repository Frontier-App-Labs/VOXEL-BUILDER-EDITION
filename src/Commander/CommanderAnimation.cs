using Godot;

namespace VoxelSiege.Commander;

public enum CommanderAnimationState
{
    Idle,
    Flinch,
    Panic,
    Dead,
}

public partial class CommanderAnimation : Node
{
    public CommanderAnimationState CurrentState { get; private set; } = CommanderAnimationState.Idle;

    public void SetState(CommanderAnimationState state)
    {
        CurrentState = state;
    }
}
