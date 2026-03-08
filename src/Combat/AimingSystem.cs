using Godot;
using VoxelSiege.Core;
using VoxelSiege.Utility;

namespace VoxelSiege.Combat;

public partial class AimingSystem : Node
{
    [Export]
    public float YawRadians { get; set; }

    [Export]
    public float PitchRadians { get; set; } = -0.2f;

    [Export]
    public float PowerPercent { get; set; } = 0.75f;

    public Vector3 GetDirection()
    {
        Basis yawBasis = new Basis(Vector3.Up, YawRadians);
        Basis pitchBasis = new Basis(Vector3.Right, PitchRadians);
        return (yawBasis * pitchBasis * Vector3.Forward).Normalized();
    }

    public Vector3 GetLaunchVelocity(float projectileSpeed)
    {
        return GetDirection() * (projectileSpeed * Mathf.Clamp(PowerPercent, 0.1f, 1f));
    }

    public Vector3[] SampleTrajectory(Vector3 origin, float projectileSpeed, int steps = 24, float stepTime = 0.1f)
    {
        return MathHelpers.SampleBallisticArc(origin, GetLaunchVelocity(projectileSpeed), (float)ProjectSettings.GetSetting("physics/3d/default_gravity"), steps, stepTime);
    }
}
