using Godot;
using VoxelSiege.Art;
using VoxelSiege.Core;
using VoxelSiege.FX;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Combat;

/// <summary>
/// Base projectile node. Supports three flight modes:
///   - Standard ballistic (gravity arc, explode on contact)
///   - Homing (curves toward target, then explodes)
///   - Drill (burrows through solid blocks, destroying them)
/// Weapons configure the mode via SetHoming / SetDrillMode after Initialize.
/// </summary>
public partial class ProjectileBase : Node3D
{
    private MeshInstance3D? _visual;
    private MeshInstance3D? _trail;
    private VoxelWorld? _world;
    private PlayerSlot _owner;
    private Vector3 _velocity;
    private int _baseDamage;
    private float _blastRadiusMicrovoxels;
    private bool _hasImpacted;
    private float _spinSpeed = 8f;
    private float _spinAngle;
    private string _projectileType = "cannonball";

    // --- Homing state ---
    private bool _homingEnabled;
    private Vector3 _homingTarget;
    private float _homingStrength;

    // --- Drill state ---
    private bool _drillMode;
    private int _drillMaxPenetration;
    private int _drillBlocksBored;
    private bool _drillInsideSolid;
    private bool _drillHasBored;

    [Export]
    public float GravityMultiplier { get; set; } = 1f;

    [Export]
    public float LifetimeSeconds { get; set; } = 10f;

    /// <summary>
    /// Set the projectile visual type before Initialize.
    /// Valid types: "cannonball", "mortar_shell", "rail_slug", "missile", "drill_bit"
    /// </summary>
    public void SetProjectileType(string type)
    {
        _projectileType = type;
    }

    public void Initialize(VoxelWorld world, PlayerSlot owner, Vector3 velocity, int baseDamage, float blastRadiusMicrovoxels)
    {
        _world = world;
        _owner = owner;
        _velocity = velocity;
        _baseDamage = baseDamage;
        _blastRadiusMicrovoxels = blastRadiusMicrovoxels;
        AddToGroup("Projectiles");
        EnsureVisual();

        // Orient projectile along velocity direction
        if (_velocity.LengthSquared() > 0.01f)
        {
            LookAt(GlobalPosition + _velocity.Normalized(), Vector3.Up);
        }
    }

    /// <summary>
    /// Enables homing behavior. The projectile steers toward the target
    /// point each frame with the given turning strength.
    /// </summary>
    public void SetHoming(Vector3 targetPoint, float homingStrength = 2.5f)
    {
        _homingEnabled = true;
        _homingTarget = targetPoint;
        _homingStrength = homingStrength;
    }

    /// <summary>
    /// Enables drill mode. Instead of exploding on first contact, the
    /// projectile burrows through solid voxels, destroying them, up to
    /// maxPenetration blocks deep. Detonates after exhausting penetration
    /// or exiting the structure.
    /// </summary>
    public void SetDrillMode(int maxPenetration)
    {
        _drillMode = true;
        _drillMaxPenetration = maxPenetration;
        _drillBlocksBored = 0;
        _drillInsideSolid = false;
        _drillHasBored = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_hasImpacted || _world == null)
        {
            return;
        }

        LifetimeSeconds -= (float)delta;
        if (LifetimeSeconds <= 0f)
        {
            Impact(GlobalPosition);
            return;
        }

        // Kill projectiles that leave the arena or fall below the ground
        Vector3 pos = GlobalPosition;
        float arenaBound = 80f; // generous arena limit
        if (pos.Y < -5f || pos.Y > 120f ||
            Mathf.Abs(pos.X) > arenaBound || Mathf.Abs(pos.Z) > arenaBound)
        {
            Impact(pos);
            return;
        }

        float dt = (float)delta;
        float gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity") * GravityMultiplier;
        Vector3 previousPosition = GlobalPosition;

        // Apply homing steering before gravity
        if (_homingEnabled)
        {
            Vector3 toTarget = (_homingTarget - GlobalPosition).Normalized();
            Vector3 currentDir = _velocity.Normalized();
            float lerpFactor = Mathf.Min(_homingStrength * dt, 0.95f);
            Vector3 steerDir = currentDir.Lerp(toTarget, lerpFactor).Normalized();
            float speed = _velocity.Length();
            _velocity = steerDir * speed;
        }

        // Velocity-Verlet integration: exact for constant acceleration (gravity).
        // This matches the analytical ballistic solution used by AimingSystem.SetTargetPoint.
        // Previous semi-implicit Euler (v -= g*dt; p += v*dt) accumulated a systematic
        // Y-axis undershoot of ~0.5*g*dt per second of flight, causing projectiles to
        // land short of the clicked target.
        GlobalPosition += _velocity * dt - new Vector3(0, 0.5f * gravity * dt * dt, 0);
        _velocity.Y -= gravity * dt;

        // Orient along velocity
        if (_velocity.LengthSquared() > 0.01f)
        {
            Vector3 forward = _velocity.Normalized();
            Vector3 up = Vector3.Up;
            if (Mathf.Abs(forward.Dot(up)) > 0.99f)
            {
                up = Vector3.Right;
            }

            LookAt(GlobalPosition + forward, up);
        }

        // Spin around forward axis
        _spinAngle += _spinSpeed * dt;
        if (_visual != null)
        {
            _visual.Rotation = new Vector3(0, 0, _spinAngle);
        }

        // Check collision with commanders regardless of drill mode
        // (skip owner's commander to prevent self-hits)
        foreach (Node node in GetTree().GetNodesInGroup("Commanders"))
        {
            if (node is not Commander.Commander commander)
            {
                continue;
            }

            if (commander.OwnerSlot == _owner)
            {
                continue;
            }

            float distance = commander.GlobalPosition.DistanceTo(GlobalPosition);
            if (distance < 1.5f)
            {
                Impact(commander.GlobalPosition);
                return;
            }
        }

        if (_drillMode)
        {
            ProcessDrillCollision(previousPosition, GlobalPosition);
        }
        else
        {
            if (CheckCollisionAlongPath(previousPosition, GlobalPosition, out Vector3 impactPoint))
            {
                Impact(impactPoint);
                return;
            }
        }
    }

    private bool CheckCollisionAlongPath(Vector3 start, Vector3 end, out Vector3 impactPoint)
    {
        Vector3 delta = end - start;
        int steps = Mathf.Max(1, Mathf.CeilToInt(delta.Length() / GameConfig.MicrovoxelMeters));
        for (int index = 1; index <= steps; index++)
        {
            float t = index / (float)steps;
            Vector3 samplePoint = start + (delta * t);
            if (_world!.GetVoxel(MathHelpers.WorldToMicrovoxel(samplePoint)).IsSolid)
            {
                impactPoint = samplePoint;
                return true;
            }
        }

        impactPoint = end;
        return false;
    }

    /// <summary>
    /// Drill collision: instead of stopping on first solid hit, destroy voxels
    /// in the path and keep going until penetration is exhausted.
    /// </summary>
    private void ProcessDrillCollision(Vector3 start, Vector3 end)
    {
        Vector3 delta = end - start;
        int steps = Mathf.Max(1, Mathf.CeilToInt(delta.Length() / GameConfig.MicrovoxelMeters));

        for (int index = 1; index <= steps; index++)
        {
            float t = index / (float)steps;
            Vector3 samplePoint = start + (delta * t);
            Vector3I micro = MathHelpers.WorldToMicrovoxel(samplePoint);
            VoxelValue voxel = _world!.GetVoxel(micro);

            if (voxel.IsSolid)
            {
                if (voxel.Material == VoxelMaterialType.Foundation)
                {
                    // Foundation stops the drill cold -- detonate here
                    Impact(samplePoint);
                    return;
                }

                if (!_drillInsideSolid)
                {
                    // Entering a new solid block
                    _drillBlocksBored++;
                    _drillInsideSolid = true;
                    _drillHasBored = true;
                }

                // Destroy the voxel
                _world.SetVoxel(micro, VoxelValue.Air, _owner);

                // Play drill SFX for satisfying tunneling feedback
                AudioDirector.Instance?.PlaySFX("drill_bore", samplePoint);

                if (_drillBlocksBored >= _drillMaxPenetration)
                {
                    // Exhausted penetration -- detonate inside
                    Impact(samplePoint);
                    return;
                }
            }
            else
            {
                if (_drillInsideSolid)
                {
                    _drillInsideSolid = false;

                    // Drill has punched through the structure -- detonate on the exit side
                    if (_drillHasBored)
                    {
                        Impact(samplePoint);
                        return;
                    }
                }
            }
        }
    }

    private void Impact(Vector3 point)
    {
        if (_hasImpacted || _world == null)
        {
            return;
        }

        _hasImpacted = true;

        // Detach any TrailFX children so they linger after projectile is freed
        foreach (Node child in GetChildren())
        {
            if (child is TrailFX trailFx)
            {
                trailFx.Detach();
            }
        }

        Explosion.Trigger(GetParent() ?? _world, _world, point, _baseDamage, _blastRadiusMicrovoxels, _owner);
        QueueFree();
    }

    private void EnsureVisual()
    {
        _visual ??= GetNodeOrNull<MeshInstance3D>("Visual");
        if (_visual != null)
        {
            return;
        }

        switch (_projectileType)
        {
            case "mortar_shell":
                CreateMortarShell();
                break;
            case "rail_slug":
                CreateRailSlug();
                break;
            case "missile":
                CreateMissile();
                break;
            case "drill_bit":
                CreateDrillBit();
                _spinSpeed = 20f;
                break;
            default:
                CreateCannonball();
                break;
        }
    }

    private void CreateCannonball()
    {
        // Dark metal sphere with slight glow
        Color?[,,] v = new Color?[3, 3, 3];
        Color metal = new(0.2f, 0.2f, 0.22f);
        Color glow = new(0.6f, 0.35f, 0.1f);

        // Fill a rough sphere (center + faces)
        v[1, 1, 1] = metal;
        v[0, 1, 1] = metal; v[2, 1, 1] = metal;
        v[1, 0, 1] = metal; v[1, 2, 1] = metal;
        v[1, 1, 0] = metal; v[1, 1, 2] = metal;

        VoxelModelBuilder builder = new()
        {
            VoxelSize = 0.1f,
            OriginOffset = new Vector3(-0.15f, -0.15f, -0.15f),
        };

        _visual = new MeshInstance3D();
        _visual.Name = "Visual";
        _visual.Mesh = builder.BuildMesh(v);

        StandardMaterial3D mat = VoxelModelBuilder.CreateVoxelMaterial(0.5f, 0.3f);
        mat.EmissionEnabled = true;
        mat.Emission = glow;
        mat.EmissionEnergyMultiplier = 0.5f;
        _visual.MaterialOverride = mat;
        AddChild(_visual);

        // Trail glow
        CreateTrailMesh(glow, 0.06f);
    }

    private void CreateMortarShell()
    {
        // Elongated shape with fins
        Color?[,,] v = new Color?[3, 5, 3];
        Color shell = new(0.3f, 0.32f, 0.25f);
        Color fin = new(0.5f, 0.5f, 0.52f);
        Color nose = new(0.7f, 0.2f, 0.15f);

        // Body
        v[1, 0, 1] = fin; // bottom fin
        v[0, 0, 1] = fin;
        v[2, 0, 1] = fin;
        v[1, 0, 0] = fin;
        v[1, 0, 2] = fin;
        v[1, 1, 1] = shell;
        v[1, 2, 1] = shell;
        v[1, 3, 1] = shell;
        v[0, 2, 1] = shell;
        v[2, 2, 1] = shell;
        v[1, 2, 0] = shell;
        v[1, 2, 2] = shell;
        v[1, 4, 1] = nose; // nose

        VoxelModelBuilder builder = new()
        {
            VoxelSize = 0.07f,
            OriginOffset = new Vector3(-0.105f, -0.175f, -0.105f),
        };

        _visual = new MeshInstance3D();
        _visual.Name = "Visual";
        _visual.Mesh = builder.BuildMesh(v);
        _visual.MaterialOverride = VoxelModelBuilder.CreateVoxelMaterial(0.3f, 0.5f);
        AddChild(_visual);
    }

    private void CreateRailSlug()
    {
        // Thin elongated with bright cyan glow
        Color?[,,] v = new Color?[2, 6, 2];
        Color cyanBright = new(0.3f, 0.9f, 1.0f);
        Color white = new(0.9f, 0.95f, 1.0f);

        v[0, 0, 0] = cyanBright;
        v[1, 0, 1] = cyanBright;
        for (int y = 1; y < 5; y++)
        {
            v[0, y, 0] = cyanBright;
            v[1, y, 0] = cyanBright;
            v[0, y, 1] = cyanBright;
            v[1, y, 1] = cyanBright;
        }
        v[0, 5, 0] = white;
        v[1, 5, 1] = white;

        VoxelModelBuilder builder = new()
        {
            VoxelSize = 0.05f,
            OriginOffset = new Vector3(-0.05f, -0.15f, -0.05f),
        };

        _visual = new MeshInstance3D();
        _visual.Name = "Visual";
        _visual.Mesh = builder.BuildMesh(v);

        StandardMaterial3D mat = VoxelModelBuilder.CreateVoxelMaterial(0.1f, 0.2f);
        mat.EmissionEnabled = true;
        mat.Emission = cyanBright;
        mat.EmissionEnergyMultiplier = 2.0f;
        _visual.MaterialOverride = mat;
        AddChild(_visual);

        CreateTrailMesh(cyanBright, 0.04f);
        _spinSpeed = 15f;
    }

    private void CreateMissile()
    {
        // Classic rocket shape with red nose
        Color?[,,] v = new Color?[3, 7, 3];
        Color body = new(0.35f, 0.38f, 0.3f);
        Color redNose = new(0.85f, 0.15f, 0.1f);
        Color fin = new(0.5f, 0.5f, 0.52f);
        Color flame = new(1.0f, 0.6f, 0.1f);

        // Exhaust
        v[1, 0, 1] = flame;

        // Fins at base
        v[0, 1, 1] = fin;
        v[2, 1, 1] = fin;
        v[1, 1, 0] = fin;
        v[1, 1, 2] = fin;

        // Body
        for (int y = 1; y < 6; y++)
        {
            v[1, y, 1] = body;
        }
        v[0, 3, 1] = body;
        v[2, 3, 1] = body;
        v[1, 3, 0] = body;
        v[1, 3, 2] = body;

        // Red nose cone
        v[1, 6, 1] = redNose;

        VoxelModelBuilder builder = new()
        {
            VoxelSize = 0.07f,
            OriginOffset = new Vector3(-0.105f, -0.245f, -0.105f),
        };

        _visual = new MeshInstance3D();
        _visual.Name = "Visual";
        _visual.Mesh = builder.BuildMesh(v);

        StandardMaterial3D mat = VoxelModelBuilder.CreateVoxelMaterial(0.2f, 0.5f);
        mat.EmissionEnabled = true;
        mat.Emission = flame;
        mat.EmissionEnergyMultiplier = 0.3f;
        _visual.MaterialOverride = mat;
        AddChild(_visual);

        CreateTrailMesh(flame, 0.05f);
        _spinSpeed = 3f; // missiles spin slowly
    }

    private void CreateDrillBit()
    {
        // Spinning drill mesh - spiral approximation
        Color?[,,] v = new Color?[3, 5, 3];
        Color metal = new(0.5f, 0.52f, 0.55f);
        Color orange = new(0.9f, 0.55f, 0.1f);

        // Tip
        v[1, 4, 1] = metal;

        // Spiral body
        v[0, 3, 1] = metal;
        v[1, 3, 0] = orange;
        v[2, 3, 1] = metal;
        v[1, 3, 2] = orange;

        v[1, 2, 0] = metal;
        v[2, 2, 1] = orange;
        v[1, 2, 2] = metal;
        v[0, 2, 1] = orange;

        v[0, 1, 0] = metal;
        v[2, 1, 0] = metal;
        v[0, 1, 2] = metal;
        v[2, 1, 2] = metal;
        v[1, 1, 1] = orange;

        v[1, 0, 1] = metal;
        v[0, 0, 1] = metal;
        v[2, 0, 1] = metal;

        VoxelModelBuilder builder = new()
        {
            VoxelSize = 0.08f,
            OriginOffset = new Vector3(-0.12f, -0.2f, -0.12f),
        };

        _visual = new MeshInstance3D();
        _visual.Name = "Visual";
        _visual.Mesh = builder.BuildMesh(v);
        _visual.MaterialOverride = VoxelModelBuilder.CreateVoxelMaterial(0.6f, 0.3f);
        AddChild(_visual);

        _spinSpeed = 25f; // drills spin fast
    }

    private void CreateTrailMesh(Color glowColor, float size)
    {
        _trail = new MeshInstance3D();
        _trail.Name = "Trail";
        _trail.Mesh = new SphereMesh { Radius = size, Height = size * 2f };
        _trail.Position = new Vector3(0, 0, 0.1f); // slightly behind

        StandardMaterial3D trailMat = new();
        trailMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        trailMat.AlbedoColor = new Color(glowColor.R, glowColor.G, glowColor.B, 0.5f);
        trailMat.EmissionEnabled = true;
        trailMat.Emission = glowColor;
        trailMat.EmissionEnergyMultiplier = 1.5f;
        _trail.MaterialOverride = trailMat;
        AddChild(_trail);
    }
}
