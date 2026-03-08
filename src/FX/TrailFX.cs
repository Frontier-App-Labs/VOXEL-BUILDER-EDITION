using Godot;

namespace VoxelSiege.FX;

public partial class TrailFX : Node3D
{
    public void Follow(Node3D target)
    {
        GlobalPosition = target.GlobalPosition;
    }
}
