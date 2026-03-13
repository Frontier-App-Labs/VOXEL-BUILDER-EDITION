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

    // --- Launch immunity: skip voxel collision for the first few frames ---
    // This prevents projectiles from detonating on their own launcher's structure.
    private float _launchImmunityTimer = 0.05f;

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

        // Align the visual mesh (built along +Y) so it faces forward (-Z)
        if (_visual != null)
        {
            _visual.Rotation = new Vector3(-Mathf.Pi * 0.5f, 0, 0);
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
    /// Enables drill (Bunker Buster) mode. Instead of exploding on first
    /// contact, the projectile bores a 3x3 cross-section through solid
    /// voxels. Keeps flying through air gaps to re-enter the next wall.
    /// Detonates only after exhausting maxPenetration blocks or on lifetime
    /// expiry. Foundation blocks stop the drill immediately.
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

        // Spin around forward axis.
        // The visual mesh is built along the +Y axis (nose at top), but
        // LookAt points the node's -Z toward the velocity. We compose
        // two rotations via quaternions:
        //   1. Spin around the model's +Y (long axis) by _spinAngle
        //   2. Tilt -90° around X to align model +Y with node -Z (forward)
        // Quaternion multiplication applies spin first (model space), then
        // tilt, so the projectile spins around its flight axis correctly
        // without tumbling sideways.
        _spinAngle += _spinSpeed * dt;
        if (_visual != null)
        {
            Quaternion tilt = new Quaternion(Vector3.Right, -Mathf.Pi * 0.5f);
            Quaternion spin = new Quaternion(Vector3.Up, _spinAngle);
            _visual.Quaternion = tilt * spin;
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

        // Check collision with enemy weapons (direct hits deal full base damage).
        // Uses the same proximity approach as commanders. Weapons occupy roughly
        // one build unit (~1m), so a hit radius of 1.0m feels fair.
        foreach (Node node in GetTree().GetNodesInGroup("Weapons"))
        {
            if (node is not WeaponBase weapon || weapon.IsDestroyed)
            {
                continue;
            }

            // Skip friendly weapons so you can't accidentally destroy your own
            if (weapon.OwnerSlot == _owner)
            {
                continue;
            }

            float distance = weapon.GlobalPosition.DistanceTo(GlobalPosition);
            if (distance < 1.0f)
            {
                // Direct hit: apply full base damage before the explosion
                weapon.ApplyDamage(_baseDamage);
                Impact(weapon.GlobalPosition);
                return;
            }
        }

        // Check collision with FallingChunk debris (RigidBody3D on layer 4).
        // The DDA voxel traversal only checks the VoxelWorld grid, so projectiles
        // pass right through fallen/collapsed RigidBody3D chunks. Use a physics
        // raycast along the travel segment to detect these debris bodies.
        if (_launchImmunityTimer <= 0f && !_drillMode)
        {
            var spaceState = GetWorld3D().DirectSpaceState;
            if (spaceState != null)
            {
                var query = PhysicsRayQueryParameters3D.Create(previousPosition, GlobalPosition);
                query.CollisionMask = 4; // debris layer only
                query.CollideWithBodies = true;
                var result = spaceState.IntersectRay(query);
                if (result != null && result.Count > 0)
                {
                    Vector3 hitPoint = (Vector3)result["position"];
                    Impact(hitPoint);
                    return;
                }
            }
        }

        // Skip voxel collision during launch immunity so projectiles clear
        // their own launcher's structure (pillars, crenellations, etc.)
        if (_launchImmunityTimer > 0f)
        {
            _launchImmunityTimer -= dt;
        }
        else if (_drillMode)
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
        // Use DDA (Digital Differential Analyzer) voxel traversal so every
        // voxel cell the path crosses is tested.  The previous interpolation
        // approach sampled only N evenly-spaced points along the segment and
        // could skip 1-wide voxels (e.g. tree trunks) on diagonal paths.
        float scale = GameConfig.MicrovoxelMeters;
        Vector3 origin = start / scale;
        Vector3 dest = end / scale;
        Vector3 direction = dest - origin;
        float totalDist = direction.Length();
        if (totalDist < 1e-6f)
        {
            impactPoint = end;
            return false;
        }
        direction /= totalDist; // normalize

        int x = Mathf.FloorToInt(origin.X);
        int y = Mathf.FloorToInt(origin.Y);
        int z = Mathf.FloorToInt(origin.Z);

        int stepX = direction.X >= 0 ? 1 : -1;
        int stepY = direction.Y >= 0 ? 1 : -1;
        int stepZ = direction.Z >= 0 ? 1 : -1;

        float tMaxX = direction.X != 0 ? ((direction.X > 0 ? (x + 1) - origin.X : x - origin.X) / direction.X) : float.MaxValue;
        float tMaxY = direction.Y != 0 ? ((direction.Y > 0 ? (y + 1) - origin.Y : y - origin.Y) / direction.Y) : float.MaxValue;
        float tMaxZ = direction.Z != 0 ? ((direction.Z > 0 ? (z + 1) - origin.Z : z - origin.Z) / direction.Z) : float.MaxValue;

        float tDeltaX = direction.X != 0 ? Mathf.Abs(1.0f / direction.X) : float.MaxValue;
        float tDeltaY = direction.Y != 0 ? Mathf.Abs(1.0f / direction.Y) : float.MaxValue;
        float tDeltaZ = direction.Z != 0 ? Mathf.Abs(1.0f / direction.Z) : float.MaxValue;

        float t = 0f;
        int maxIterations = (int)(totalDist * 2) + 2;

        for (int i = 0; i < maxIterations; i++)
        {
            Vector3I micro = new Vector3I(x, y, z);
            if (_world!.GetVoxel(micro).IsSolid)
            {
                // Return the world-space point where the ray entered this voxel
                impactPoint = start + (end - start) * Mathf.Clamp(t / totalDist, 0f, 1f);
                return true;
            }

            // Advance to the next voxel boundary
            if (tMaxX < tMaxY)
            {
                if (tMaxX < tMaxZ) { t = tMaxX; x += stepX; tMaxX += tDeltaX; }
                else { t = tMaxZ; z += stepZ; tMaxZ += tDeltaZ; }
            }
            else
            {
                if (tMaxY < tMaxZ) { t = tMaxY; y += stepY; tMaxY += tDeltaY; }
                else { t = tMaxZ; z += stepZ; tMaxZ += tDeltaZ; }
            }

            if (t > totalDist)
                break;
        }

        impactPoint = end;
        return false;
    }

    /// <summary>
    /// Drill collision: Bunker Buster behavior. Bores a 3x3 cross-section
    /// (center voxel + 4 cardinal neighbors perpendicular to travel direction)
    /// through solid voxels. Does NOT detonate on structure exit -- keeps flying
    /// through air gaps to re-enter the next wall. Only detonates when max
    /// penetration is reached or lifetime expires. Foundation blocks still stop
    /// the drill cold.
    /// Uses DDA voxel traversal to visit every cell the path crosses.
    /// </summary>
    private void ProcessDrillCollision(Vector3 start, Vector3 end)
    {
        float scale = GameConfig.MicrovoxelMeters;
        Vector3 origin = start / scale;
        Vector3 dest = end / scale;
        Vector3 direction = dest - origin;
        float totalDist = direction.Length();
        if (totalDist < 1e-6f)
            return;
        direction /= totalDist;

        // Compute perpendicular axes for the 3x3 cross-section bore.
        // These define the "up" and "right" directions relative to the drill's
        // travel direction, used to destroy the 4 cardinal neighbors.
        Vector3 forward = _velocity.Normalized();
        Vector3 perpUp;
        if (Mathf.Abs(forward.Dot(Vector3.Up)) > 0.9f)
        {
            // Drilling nearly vertical -- use Right as reference
            perpUp = forward.Cross(Vector3.Right).Normalized();
        }
        else
        {
            perpUp = forward.Cross(Vector3.Up).Normalized();
        }
        Vector3 perpRight = forward.Cross(perpUp).Normalized();

        // Convert perp axes to voxel-space offsets (round to nearest integer direction)
        Vector3I offsetUp = new Vector3I(
            Mathf.RoundToInt(perpUp.X / scale),
            Mathf.RoundToInt(perpUp.Y / scale),
            Mathf.RoundToInt(perpUp.Z / scale));
        Vector3I offsetRight = new Vector3I(
            Mathf.RoundToInt(perpRight.X / scale),
            Mathf.RoundToInt(perpRight.Y / scale),
            Mathf.RoundToInt(perpRight.Z / scale));

        // Ensure offsets are unit-length (clamp components to -1..1)
        // If cross product produced a zero offset, fall back to sensible defaults
        offsetUp = ClampToUnit(offsetUp);
        offsetRight = ClampToUnit(offsetRight);
        if (offsetUp == Vector3I.Zero) offsetUp = new Vector3I(0, 1, 0);
        if (offsetRight == Vector3I.Zero) offsetRight = new Vector3I(1, 0, 0);
        // If both offsets ended up the same axis, pick an orthogonal one
        if (offsetUp == offsetRight || offsetUp == -offsetRight)
        {
            offsetRight = new Vector3I(offsetUp.Z, offsetUp.X, offsetUp.Y);
        }

        int x = Mathf.FloorToInt(origin.X);
        int y = Mathf.FloorToInt(origin.Y);
        int z = Mathf.FloorToInt(origin.Z);

        int stepX = direction.X >= 0 ? 1 : -1;
        int stepY = direction.Y >= 0 ? 1 : -1;
        int stepZ = direction.Z >= 0 ? 1 : -1;

        float tMaxX = direction.X != 0 ? ((direction.X > 0 ? (x + 1) - origin.X : x - origin.X) / direction.X) : float.MaxValue;
        float tMaxY = direction.Y != 0 ? ((direction.Y > 0 ? (y + 1) - origin.Y : y - origin.Y) / direction.Y) : float.MaxValue;
        float tMaxZ = direction.Z != 0 ? ((direction.Z > 0 ? (z + 1) - origin.Z : z - origin.Z) / direction.Z) : float.MaxValue;

        float tDeltaX = direction.X != 0 ? Mathf.Abs(1.0f / direction.X) : float.MaxValue;
        float tDeltaY = direction.Y != 0 ? Mathf.Abs(1.0f / direction.Y) : float.MaxValue;
        float tDeltaZ = direction.Z != 0 ? Mathf.Abs(1.0f / direction.Z) : float.MaxValue;

        float t = 0f;
        int maxIterations = (int)(totalDist * 2) + 2;

        // Cross-section offsets: center + 4 cardinal neighbors
        Vector3I[] crossSection = new Vector3I[]
        {
            Vector3I.Zero,   // center
            offsetUp,        // up
            -offsetUp,       // down
            offsetRight,     // right
            -offsetRight,    // left
        };

        for (int i = 0; i < maxIterations; i++)
        {
            Vector3I micro = new Vector3I(x, y, z);
            VoxelValue voxel = _world!.GetVoxel(micro);
            Vector3 samplePoint = start + (end - start) * Mathf.Clamp(t / totalDist, 0f, 1f);

            if (voxel.IsSolid)
            {
                // Check for Foundation in the center voxel -- stops drill cold
                if (voxel.Material == VoxelMaterialType.Foundation)
                {
                    Impact(samplePoint);
                    return;
                }

                if (!_drillInsideSolid)
                {
                    // Entering a new solid block along the center line
                    _drillBlocksBored++;
                    _drillInsideSolid = true;
                    _drillHasBored = true;
                }

                // Bore the 3x3 cross-section: destroy center + 4 cardinal neighbors
                foreach (Vector3I offset in crossSection)
                {
                    Vector3I borePos = micro + offset;
                    VoxelValue boreVoxel = _world.GetVoxel(borePos);
                    if (boreVoxel.IsSolid && boreVoxel.Material != VoxelMaterialType.Foundation)
                    {
                        // Spawn debris flying outward from the bored voxel
                        Color debrisColor = VoxelMaterials.GetPreviewColor(boreVoxel.Material);
                        DebrisFX.SpawnDebris(GetTree().Root, samplePoint, debrisColor, samplePoint - _velocity.Normalized() * 0.5f, 2, boreVoxel.Material);

                        // Destroy the voxel
                        _world.SetVoxel(borePos, VoxelValue.Air, _owner);
                    }
                }

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
                // Exited solid into air -- just mark state, keep flying
                if (_drillInsideSolid)
                {
                    _drillInsideSolid = false;
                }
            }

            // Advance to the next voxel boundary
            if (tMaxX < tMaxY)
            {
                if (tMaxX < tMaxZ) { t = tMaxX; x += stepX; tMaxX += tDeltaX; }
                else { t = tMaxZ; z += stepZ; tMaxZ += tDeltaZ; }
            }
            else
            {
                if (tMaxY < tMaxZ) { t = tMaxY; y += stepY; tMaxY += tDeltaY; }
                else { t = tMaxZ; z += stepZ; tMaxZ += tDeltaZ; }
            }

            if (t > totalDist)
                break;
        }
    }

    /// <summary>
    /// Clamp each component of a Vector3I to the range [-1, 1].
    /// Used to normalize voxel-space perpendicular offsets for drill bore cross-section.
    /// </summary>
    private static Vector3I ClampToUnit(Vector3I v)
    {
        return new Vector3I(
            Mathf.Clamp(v.X, -1, 1),
            Mathf.Clamp(v.Y, -1, 1),
            Mathf.Clamp(v.Z, -1, 1));
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

        VoxelPalette palette = new();
        palette.AddColors(v);
        palette.Build();

        VoxelModelBuilder builder = new()
        {
            VoxelSize = 0.1f,
            OriginOffset = new Vector3(-0.15f, -0.15f, -0.15f),
        };

        _visual = new MeshInstance3D();
        _visual.Name = "Visual";
        _visual.Mesh = builder.BuildMesh(v, palette);

        StandardMaterial3D mat = palette.CreateMaterial(0.5f, 0.3f);
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

        VoxelPalette palette = new();
        palette.AddColors(v);
        palette.Build();

        VoxelModelBuilder builder = new()
        {
            VoxelSize = 0.07f,
            OriginOffset = new Vector3(-0.105f, -0.175f, -0.105f),
        };

        _visual = new MeshInstance3D();
        _visual.Name = "Visual";
        _visual.Mesh = builder.BuildMesh(v, palette);
        _visual.MaterialOverride = palette.CreateMaterial(0.3f, 0.5f);
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

        VoxelPalette palette = new();
        palette.AddColors(v);
        palette.Build();

        VoxelModelBuilder builder = new()
        {
            VoxelSize = 0.05f,
            OriginOffset = new Vector3(-0.05f, -0.15f, -0.05f),
        };

        _visual = new MeshInstance3D();
        _visual.Name = "Visual";
        _visual.Mesh = builder.BuildMesh(v, palette);

        StandardMaterial3D mat = palette.CreateMaterial(0.1f, 0.2f);
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

        VoxelPalette palette = new();
        palette.AddColors(v);
        palette.Build();

        VoxelModelBuilder builder = new()
        {
            VoxelSize = 0.07f,
            OriginOffset = new Vector3(-0.105f, -0.245f, -0.105f),
        };

        _visual = new MeshInstance3D();
        _visual.Name = "Visual";
        _visual.Mesh = builder.BuildMesh(v, palette);

        StandardMaterial3D mat = palette.CreateMaterial(0.2f, 0.5f);
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

        VoxelPalette palette = new();
        palette.AddColors(v);
        palette.Build();

        VoxelModelBuilder builder = new()
        {
            VoxelSize = 0.08f,
            OriginOffset = new Vector3(-0.12f, -0.2f, -0.12f),
        };

        _visual = new MeshInstance3D();
        _visual.Name = "Visual";
        _visual.Mesh = builder.BuildMesh(v, palette);
        _visual.MaterialOverride = palette.CreateMaterial(0.6f, 0.3f);
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
