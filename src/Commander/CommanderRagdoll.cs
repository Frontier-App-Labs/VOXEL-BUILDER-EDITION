using Godot;

namespace VoxelSiege.Commander;

public partial class CommanderRagdoll : Node3D
{
    public bool IsActive { get; private set; }
    public Vector3 LastImpulse { get; private set; }

    public void ActivateRagdoll(Vector3 impulse)
    {
        IsActive = true;
        LastImpulse = impulse;
        Visible = true;
    }
}
