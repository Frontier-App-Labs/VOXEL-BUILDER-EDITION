using Godot;

namespace VoxelSiege.FX;

public partial class ImpactDecals : Node3D
{
    public void Stamp(Vector3 position, Vector3 normal)
    {
        GlobalPosition = position;
        LookAt(position + normal, Vector3.Up);
    }
}
