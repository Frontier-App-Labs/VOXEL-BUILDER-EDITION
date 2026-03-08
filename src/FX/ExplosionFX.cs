using Godot;

namespace VoxelSiege.FX;

public partial class ExplosionFX : Node3D
{
    public void Play(Vector3 worldPosition)
    {
        GlobalPosition = worldPosition;
        Visible = true;
    }
}
