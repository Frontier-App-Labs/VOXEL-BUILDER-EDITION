using Godot;
using VoxelSiege.Core;
using VoxelSiege.Utility;

namespace VoxelSiege.Combat;

/// <summary>
/// Manages weapon aiming state: yaw, pitch, and power. Converts those
/// parameters into launch velocities and trajectory previews.
/// Supports click-to-target mode where the player clicks a world position
/// and the system auto-calculates ballistic parameters to hit that point.
/// </summary>
public partial class AimingSystem : Node
{
    /// <summary>
    /// Minimum pitch angle in radians (-85 degrees, nearly straight down).
    /// </summary>
    private static readonly float MinPitchRadians = Mathf.DegToRad(-85f);

    /// <summary>
    /// Maximum pitch angle in radians (85 degrees, nearly straight up).
    /// </summary>
    private static readonly float MaxPitchRadians = Mathf.DegToRad(85f);

    /// <summary>
    /// Minimum power multiplier when PowerPercent is at 0.
    /// </summary>
    private const float MinPowerMultiplier = 0.3f;

    /// <summary>
    /// Maximum power multiplier when PowerPercent is at 1.
    /// </summary>
    private const float MaxPowerMultiplier = 1.0f;

    [Export]
    public float YawRadians { get; set; }

    [Export]
    public float PitchRadians
    {
        get => _pitchRadians;
        set => _pitchRadians = Mathf.Clamp(value, MinPitchRadians, MaxPitchRadians);
    }
    private float _pitchRadians = -0.2f;

    [Export]
    public float PowerPercent
    {
        get => _powerPercent;
        set => _powerPercent = Mathf.Clamp(value, 0f, 1f);
    }
    private float _powerPercent = 0.75f;

    /// <summary>
    /// Whether a target point has been set via click-to-target.
    /// </summary>
    public bool HasTarget { get; private set; }

    /// <summary>
    /// The world-space target point set by the player.
    /// </summary>
    public Vector3 TargetPoint { get; private set; }

    /// <summary>
    /// Returns the aim direction as a unit vector derived from yaw and pitch.
    /// </summary>
    public Vector3 GetDirection()
    {
        Basis yawBasis = new Basis(Vector3.Up, YawRadians);
        Basis pitchBasis = new Basis(Vector3.Right, PitchRadians);
        return (yawBasis * pitchBasis * Vector3.Forward).Normalized();
    }

    /// <summary>
    /// Converts aim angles and power into a 3D velocity vector for a projectile
    /// with the given max speed. Power maps from MinPowerMultiplier (0%) to
    /// MaxPowerMultiplier (100%) of the weapon's projectile speed.
    /// </summary>
    public Vector3 GetLaunchVelocity(float speed)
    {
        float powerFactor = Mathf.Lerp(MinPowerMultiplier, MaxPowerMultiplier, PowerPercent);
        return GetDirection() * (speed * powerFactor);
    }

    /// <summary>
    /// Returns predicted trajectory arc points for visualization.
    /// Uses ballistic physics with gravity to sample a parabolic arc.
    /// </summary>
    public Vector3[] GetTrajectoryPoints(float speed, int steps = 30)
    {
        float gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        Vector3 origin = GetParent() is Node3D parent3D ? parent3D.GlobalPosition : Vector3.Zero;
        Vector3 velocity = GetLaunchVelocity(speed);
        float stepTime = 0.1f;
        return MathHelpers.SampleBallisticArc(origin, velocity, gravity, steps, stepTime);
    }

    /// <summary>
    /// Samples a trajectory arc from a specific origin point.
    /// Used when the caller wants to specify the start position explicitly.
    /// </summary>
    public Vector3[] SampleTrajectory(Vector3 origin, float projectileSpeed, int steps = 24, float stepTime = 0.1f)
    {
        return MathHelpers.SampleBallisticArc(origin, GetLaunchVelocity(projectileSpeed), (float)ProjectSettings.GetSetting("physics/3d/default_gravity"), steps, stepTime);
    }

    /// <summary>
    /// Sets a target point and auto-calculates yaw, pitch, and power to hit it
    /// from the given weapon position using ballistic math appropriate for the weapon type.
    /// </summary>
    /// <param name="weaponPos">World position of the weapon barrel.</param>
    /// <param name="target">World position to hit.</param>
    /// <param name="projectileSpeed">Speed of the projectile.</param>
    /// <param name="weaponId">Weapon identifier for arc selection (e.g. "mortar" uses high arc).</param>
    /// <returns>True if a valid solution was found, false if target is out of range.</returns>
    public bool SetTargetPoint(Vector3 weaponPos, Vector3 target, float projectileSpeed, string weaponId)
    {
        TargetPoint = target;
        HasTarget = true;

        Vector3 delta = target - weaponPos;
        float horizontalDist = new Vector2(delta.X, delta.Z).Length();

        // Yaw: direction from weapon to target in XZ plane
        // Negate both components because GetDirection() uses Vector3.Forward = (0,0,-1)
        YawRadians = Mathf.Atan2(-delta.X, -delta.Z);

        float gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f);

        // Direct-fire weapons: aim directly at the target
        if (weaponId == "railgun" || weaponId == "drill")
        {
            PitchRadians = Mathf.Atan2(delta.Y, horizontalDist);
            PowerPercent = 1.0f;
            return true;
        }

        float speed = projectileSpeed;
        float v2 = speed * speed;
        float v4 = v2 * v2;
        float gx2 = gravity * horizontalDist * horizontalDist;
        float discriminant = v4 - gravity * (gx2 + 2f * delta.Y * v2);

        bool inRange = discriminant >= 0;

        if (weaponId == "mortar" || weaponId == "missile")
        {
            // High arc for mortar/missile to lob over walls
            if (inRange)
            {
                float sqrtDisc = Mathf.Sqrt(discriminant);
                PitchRadians = Mathf.Atan2(v2 + sqrtDisc, gravity * horizontalDist);
            }
            else
            {
                // Out of range, use high angle for maximum distance
                PitchRadians = Mathf.DegToRad(65f);
            }
        }
        else
        {
            // Low arc for cannon and other ballistic weapons
            if (inRange)
            {
                float sqrtDisc = Mathf.Sqrt(discriminant);
                PitchRadians = Mathf.Atan2(v2 - sqrtDisc, gravity * horizontalDist);
            }
            else
            {
                // Out of range, use 45 degrees for maximum range
                PitchRadians = Mathf.Pi / 4f;
            }
        }

        // Always use full power -- the ballistic math handles distance
        PowerPercent = 1.0f;

        return inRange;
    }

    /// <summary>
    /// Clears the current target point.
    /// </summary>
    public void ClearTarget()
    {
        HasTarget = false;
        TargetPoint = Vector3.Zero;
    }
}
