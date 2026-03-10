using Godot;
using System.Collections.Generic;
using VoxelSiege.Art;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;

namespace VoxelSiege.Commander;

public partial class Commander : Node3D
{
    private CommanderHealth? _health;
    private CommanderAnimation? _animation;
    private CommanderRagdoll? _ragdoll;
    private Node3D? _modelRoot;
    private PlayerSlot? _lastInstigator;
    private Vector3 _lastImpactDirection = Vector3.Up;
    private bool _lastHitWasCritical;

    // Gravity / fall-damage state
    private float _verticalVelocity;
    private bool _isFalling;
    private float _fallStartY;
    private VoxelWorld? _cachedWorld;

    // Panic timer: when an explosion lands nearby, the commander panics
    // for this many seconds before returning to idle.
    private float _panicTimer;

    // The skeleton's feet are 0.48m below the root/hips origin.
    // This offset is used for ground detection and floor snapping so the
    // collision checks happen at the actual feet position, not the hips.
    //
    // Derivation: Commander voxelSize=0.08m.  Hips→LeftHip attach Y=0,
    // thigh pivot Y=3 → knee at -3*0.08 = -0.24m below hips,
    // shin pivot Y=3 → boot sole at -3*0.08 = -0.24m below knee.
    // Total: -0.24 + -0.24 = -0.48m.
    private const float FeetOffsetY = 0.48f;

    // Maximum vertical speed (m/s) to prevent tunnelling through voxels during
    // large delta-time frames (low FPS). One microvoxel per physics tick at 60Hz
    // would be 0.5/0.0167 ≈ 30 m/s, so 20 m/s is a safe cap.
    private const float MaxFallSpeed = 20f;

    // Number of microvoxel cells to scan downward when searching for ground.
    // Covers a 2m range (4 * 0.5m) which handles stepping off ledges and
    // prevents the commander from hovering above gaps.
    private const int GroundScanDepth = 4;

    // Snap tolerance: if the feet are within this distance of the ground
    // surface, snap without triggering a full fall cycle. Prevents Y-jitter
    // from floating-point noise in the ground check.
    private const float SnapTolerance = 0.05f;

    [Export]
    public PlayerSlot OwnerSlot { get; set; } = PlayerSlot.Player1;

    [Export]
    public Vector3I BuildUnitPosition { get; private set; }

    public bool IsDead => _health?.IsDead ?? false;

    public bool IsExposed { get; private set; }

    public override void _Ready()
    {
        AddToGroup("Commanders");
        _health = GetNodeOrNull<CommanderHealth>("CommanderHealth");
        if (_health == null)
        {
            _health = new CommanderHealth();
            _health.Name = "CommanderHealth";
            AddChild(_health);
        }

        _animation = GetNodeOrNull<CommanderAnimation>("CommanderAnimation");
        if (_animation == null)
        {
            _animation = new CommanderAnimation();
            _animation.Name = "CommanderAnimation";
            AddChild(_animation);
        }

        _ragdoll = GetNodeOrNull<CommanderRagdoll>("CommanderRagdoll");
        if (_ragdoll == null)
        {
            _ragdoll = new CommanderRagdoll();
            _ragdoll.Name = "CommanderRagdoll";
            _ragdoll.Visible = false;
            AddChild(_ragdoll);
        }

        EnsureVisuals();
        _health.Damaged += OnDamaged;
        _health.Died += OnDied;

        // React to enemy commander kills with celebration
        if (EventBus.Instance != null)
        {
            EventBus.Instance.CommanderKilled += OnAnyCommanderKilled;
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.CommanderKilled -= OnAnyCommanderKilled;
        }
    }

    /// <summary>
    /// Triggers panic animation for the given duration (seconds).
    /// Called when an explosion lands nearby.
    /// </summary>
    public void TriggerPanic(float duration = 5f)
    {
        if (_health?.IsDead ?? false) return;
        _panicTimer = Mathf.Max(_panicTimer, duration);
        _animation?.SetState(CommanderAnimationState.Panic);
    }

    public override void _Process(double delta)
    {
        if (_health?.IsDead ?? false)
        {
            return;
        }

        // Count down panic timer and revert to idle when it expires
        if (_panicTimer > 0f)
        {
            _panicTimer -= (float)delta;
            if (_panicTimer <= 0f)
            {
                _panicTimer = 0f;
                _animation?.SetState(CommanderAnimationState.Idle);
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_health?.IsDead ?? false)
        {
            return;
        }

        float dt = (float)delta;

        // --- Void kill: instant death if fallen below the world ---
        if (GlobalPosition.Y < GameConfig.CommanderVoidKillY)
        {
            _health?.ApplyDamage(_health.CurrentHealth);
            return;
        }

        // --- Ground detection ---
        // Scan downward from the feet position to find the highest solid
        // voxel surface beneath the commander. Uses FloorToInt-based
        // conversion to avoid boundary oscillation that RoundToInt causes.
        bool hasGround = false;
        float groundSurfaceY = 0f;
        if (_cachedWorld != null)
        {
            Vector3 feetPos = GlobalPosition - new Vector3(0, FeetOffsetY, 0);
            Vector3I microPos = MathHelpers.WorldToMicrovoxelFloor(feetPos);

            // First check if the feet are *inside* a solid voxel (sunk into
            // the ground). If so, search upward to find the first air cell
            // above -- the ground surface is the top of the voxel just below
            // that air cell. This prevents the commander from getting stuck
            // inside terrain after a big gravity step.
            if (_cachedWorld.GetVoxel(microPos).IsSolid)
            {
                hasGround = true;
                // Search upward for the first air cell to find the true surface
                Vector3I scanUp = microPos;
                for (int i = 0; i < GroundScanDepth; i++)
                {
                    scanUp += Vector3I.Up;
                    if (_cachedWorld.GetVoxel(scanUp).IsAir)
                    {
                        break;
                    }
                }
                // Ground surface is the top of the voxel below the first air
                groundSurfaceY = scanUp.Y * GameConfig.MicrovoxelMeters;
            }
            else
            {
                // Feet are in air -- scan downward to find solid ground below.
                // Check multiple voxels to handle stepping off ledges and
                // large dt fall steps that skip past a single-voxel check.
                for (int i = 1; i <= GroundScanDepth; i++)
                {
                    Vector3I checkPos = microPos + new Vector3I(0, -i, 0);
                    if (_cachedWorld.GetVoxel(checkPos).IsSolid)
                    {
                        hasGround = true;
                        // Top surface of this solid voxel
                        groundSurfaceY = (checkPos.Y + 1) * GameConfig.MicrovoxelMeters;
                        break;
                    }
                }
            }
        }

        if (!hasGround)
        {
            // --- Falling (no ground within scan range) ---
            if (!_isFalling)
            {
                _isFalling = true;
                _fallStartY = GlobalPosition.Y;
                _animation?.SetState(CommanderAnimationState.Falling);
            }

            _verticalVelocity -= GameConfig.CommanderGravity * dt;
            // Clamp fall speed to prevent tunnelling through voxels on
            // low-FPS frames where dt is large.
            if (_verticalVelocity < -MaxFallSpeed)
            {
                _verticalVelocity = -MaxFallSpeed;
            }

            Vector3 pos = GlobalPosition;
            pos.Y += _verticalVelocity * dt;
            GlobalPosition = pos;
        }
        else
        {
            // --- Ground found ---
            float targetHipsY = groundSurfaceY + FeetOffsetY;
            float currentY = GlobalPosition.Y;
            float diff = targetHipsY - currentY;

            if (_isFalling)
            {
                // Just landed -- snap to ground and apply fall damage
                _isFalling = false;
                float fallDistance = _fallStartY - targetHipsY;
                _verticalVelocity = 0f;

                Vector3 snapped = GlobalPosition;
                snapped.Y = targetHipsY;
                GlobalPosition = snapped;

                if (fallDistance > GameConfig.CommanderFallDamageMinHeight)
                {
                    float excessHeight = fallDistance - GameConfig.CommanderFallDamageMinHeight;
                    int damage = Mathf.CeilToInt(excessHeight * GameConfig.CommanderFallDamagePerMeter);
                    ApplyDamage(damage, null, GlobalPosition + Vector3.Down);
                }

                // Restore the appropriate animation state
                if (!(_health?.IsDead ?? false))
                {
                    _animation?.SetState(_panicTimer > 0f ? CommanderAnimationState.Panic : CommanderAnimationState.Idle);
                }
            }
            else if (Mathf.Abs(diff) > SnapTolerance)
            {
                // Grounded but drifted away from ground surface (e.g., voxel
                // destroyed beneath, or slight floating-point drift). Lerp
                // smoothly toward the correct height to avoid jarring pops.
                Vector3 pos = GlobalPosition;
                pos.Y = Mathf.Lerp(currentY, targetHipsY, Mathf.Min(1f, dt * 20f));
                GlobalPosition = pos;
                _verticalVelocity = 0f;
            }
            else
            {
                // Already snapped and within tolerance -- hold position.
                // Only write if actually different to avoid per-frame noise.
                if (Mathf.Abs(diff) > 0.001f)
                {
                    Vector3 pos = GlobalPosition;
                    pos.Y = targetHipsY;
                    GlobalPosition = pos;
                }
                _verticalVelocity = 0f;
            }
        }
    }

    public void PlaceCommander(VoxelWorld world, Vector3I buildUnitPosition)
    {
        _cachedWorld = world;
        BuildUnitPosition = buildUnitPosition;
        Vector3I microBase = MathHelpers.BuildToMicrovoxel(buildUnitPosition);
        Vector3 worldBase = MathHelpers.MicrovoxelToWorld(microBase);
        // Raise the root (hips) by FeetOffsetY so the feet sit on the floor
        // surface.  The skeleton has feet at -FeetOffsetY relative to root.
        Position = worldBase + new Vector3(GameConfig.BuildUnitMeters * 0.5f, FeetOffsetY, GameConfig.BuildUnitMeters * 0.5f);
        _verticalVelocity = 0f;
        _isFalling = false;
        EnsureVisuals();
        EvaluateExposure(world);
    }

    public bool ApplyDamage(int damage, PlayerSlot? instigator = null, Vector3? impactOrigin = null, bool isCriticalHit = false)
    {
        _lastInstigator = instigator;
        if (impactOrigin.HasValue)
        {
            _lastImpactDirection = (GlobalPosition - impactOrigin.Value).Normalized();
        }

        _lastHitWasCritical = isCriticalHit;
        return _health?.ApplyDamage(damage) ?? false;
    }

    /// <summary>
    /// Check if the Commander is exposed (any adjacent voxels missing).
    /// When exposed, switch to panic animation. When covered, return to idle.
    /// </summary>
    public bool EvaluateExposure(VoxelWorld world)
    {
        Vector3I microBase = MathHelpers.BuildToMicrovoxel(BuildUnitPosition);
        Vector3I max = microBase + new Vector3I(GameConfig.MicrovoxelsPerBuildUnit, GameConfig.MicrovoxelsPerBuildUnit * 2, GameConfig.MicrovoxelsPerBuildUnit) - Vector3I.One;
        foreach (Vector3I direction in Directions())
        {
            foreach (Vector3I shellVoxel in EnumerateSurface(microBase, max, direction))
            {
                if (world.GetVoxel(shellVoxel + direction).IsAir)
                {
                    IsExposed = true;
                    return true;
                }
            }
        }

        IsExposed = false;
        return false;
    }

    /// <summary>
    /// Apply an explosion impulse to the ragdoll if the Commander is already dead.
    /// Lets subsequent explosions keep punting the body around.
    /// </summary>
    public void ApplyExplosionToRagdoll(Vector3 explosionOrigin, float force)
    {
        if (_health?.IsDead == true && _ragdoll?.IsActive == true)
        {
            _ragdoll.ApplyExplosionImpulse(explosionOrigin, force);
        }
    }

    /// <summary>
    /// Check if the ragdoll has settled (all parts at rest).
    /// </summary>
    public bool IsRagdollSettled()
    {
        return _ragdoll?.IsSettled() ?? false;
    }

    private void OnDamaged(int damage, int remainingHealth)
    {
        // Always emit the damage event so PlayerData.CommanderHealth stays in sync
        // with the actual health. Previously this was gated on remainingHealth > 0,
        // which meant the killing blow never updated the PlayerData via the damage
        // path (it relied solely on the separate CommanderKilled event).
        EventBus.Instance?.EmitCommanderDamaged(new CommanderDamagedEvent(OwnerSlot, damage, remainingHealth, GlobalPosition));

        if (remainingHealth > 0)
        {
            // Update health ratio for low-health tremor animation
            if (_animation != null && _health != null)
            {
                _animation.HealthRatio = (float)remainingHealth / _health.MaxHealth;
            }
            _animation?.SetState(CommanderAnimationState.Flinch);
        }
    }

    private void OnAnyCommanderKilled(CommanderKilledEvent payload)
    {
        if (_health?.IsDead ?? true) return;
        // Celebrate when an enemy commander dies (not ourselves)
        if (payload.Victim != OwnerSlot)
        {
            _animation?.TriggerCelebrate(2.5f);
        }
    }

    private async void OnDied()
    {
        _animation?.SetState(CommanderAnimationState.Dead);

        // Create the ragdoll from the body parts
        ActivateRagdollDeath();

        // Blood splat: spray tiny red voxel debris outward from the commander
        SpawnBloodSplat();

        // Slow-motion for dramatic effect
        Engine.TimeScale = GameConfig.SlowMoTimeScale;
        EventBus.Instance?.EmitCommanderKilled(new CommanderKilledEvent(OwnerSlot, _lastInstigator, GlobalPosition));
        await ToSignal(GetTree().CreateTimer(GameConfig.SlowMoDuration * GameConfig.SlowMoTimeScale), SceneTreeTimer.SignalName.Timeout);
        Engine.TimeScale = 1f;
    }

    /// <summary>
    /// Spawns a burst of tiny blood-red voxel debris from the commander's position.
    /// Uses multiple shades of red/crimson for variety.
    /// </summary>
    private void SpawnBloodSplat()
    {
        Vector3 pos = GlobalPosition + Vector3.Up * 0.5f; // center-mass height
        Vector3 center = pos;

        // Blood colors: dark red, bright red, crimson
        Color[] bloodColors = new Color[]
        {
            new Color(0.6f, 0.05f, 0.05f),  // dark red
            new Color(0.8f, 0.1f, 0.08f),   // bright red
            new Color(0.5f, 0.0f, 0.0f),    // deep crimson
            new Color(0.7f, 0.15f, 0.1f),   // blood red
        };

        // Spawn from several points around the body for a fuller splat
        float bloodVoxelScale = 0.08f; // tiny voxels
        for (int i = 0; i < 4; i++)
        {
            Vector3 offset = new Vector3(
                (float)GD.RandRange(-0.3, 0.3),
                (float)GD.RandRange(-0.2, 0.4),
                (float)GD.RandRange(-0.3, 0.3));
            Color color = bloodColors[i % bloodColors.Length];
            FX.DebrisFX.SpawnDebris(this, pos + offset, color, center, 8,
                VoxelMaterialType.Stone, bloodVoxelScale);
        }
    }

    /// <summary>
    /// Convert the Commander's animated body parts into a physics ragdoll.
    /// Hides the animated model and spawns RigidBody3D parts that tumble spectacularly.
    /// </summary>
    private void ActivateRagdollDeath()
    {
        if (_ragdoll == null || _modelRoot == null)
        {
            return;
        }

        // Collect mesh references from the skeleton hierarchy.
        // The skeleton has joints (Hips/Spine/Neck/LeftShoulder/etc.) each
        // containing a MeshInstance3D child. For the ragdoll we need the
        // meshes from: Neck->Head, Spine->Torso, LeftShoulder->LeftUpperArm,
        // RightShoulder->RightUpperArm, LeftHip->LeftThigh, RightHip->RightThigh.
        Node3D? hips = _modelRoot.GetNodeOrNull<Node3D>("Hips");
        Node3D? spine = hips?.GetNodeOrNull<Node3D>("Spine");
        Node3D? neck = spine?.GetNodeOrNull<Node3D>("Neck");
        Node3D? leftShoulder = spine?.GetNodeOrNull<Node3D>("LeftShoulder");
        Node3D? rightShoulder = spine?.GetNodeOrNull<Node3D>("RightShoulder");
        Node3D? leftHip = hips?.GetNodeOrNull<Node3D>("LeftHip");
        Node3D? rightHip = hips?.GetNodeOrNull<Node3D>("RightHip");

        MeshInstance3D? headMesh = neck?.GetNodeOrNull<MeshInstance3D>("Head");
        MeshInstance3D? torsoMesh = spine?.GetNodeOrNull<MeshInstance3D>("Torso");
        MeshInstance3D? leftArmMesh = leftShoulder?.GetNodeOrNull<MeshInstance3D>("LeftUpperArm");
        MeshInstance3D? rightArmMesh = rightShoulder?.GetNodeOrNull<MeshInstance3D>("RightUpperArm");
        MeshInstance3D? leftLegMesh = leftHip?.GetNodeOrNull<MeshInstance3D>("LeftThigh");
        MeshInstance3D? rightLegMesh = rightHip?.GetNodeOrNull<MeshInstance3D>("RightThigh");

        // Hide the animated model
        _modelRoot.Visible = false;

        // Death impulse: direction away from the killing blow
        float deathForce = 6f;
        Vector3 impulseDir = _lastImpactDirection;

        // If the impact direction is mostly zero (e.g., no origin given),
        // use a random horizontal direction with a slight upward nudge
        if (impulseDir.LengthSquared() < 0.01f)
        {
            float angle = GD.Randf() * Mathf.Tau;
            impulseDir = new Vector3(Mathf.Cos(angle), 0.3f, Mathf.Sin(angle)).Normalized();
        }

        // Build ragdoll part descriptors from the skeleton meshes
        var ragdollParts = new RagdollSkeletonPart[]
        {
            new() { Name = "Head", SourceMesh = headMesh, Joint = neck, Mass = 1.0f },
            new() { Name = "Torso", SourceMesh = torsoMesh, Joint = spine, Mass = 3.0f },
            new() { Name = "LeftArm", SourceMesh = leftArmMesh, Joint = leftShoulder, Mass = 0.8f },
            new() { Name = "RightArm", SourceMesh = rightArmMesh, Joint = rightShoulder, Mass = 0.8f },
            new() { Name = "LeftLeg", SourceMesh = leftLegMesh, Joint = leftHip, Mass = 1.5f },
            new() { Name = "RightLeg", SourceMesh = rightLegMesh, Joint = rightHip, Mass = 1.5f },
        };

        // Activate the ragdoll from the skeleton hierarchy
        _ragdoll.ActivateFromSkeleton(ragdollParts, GlobalTransform, impulseDir, deathForce);
    }

    private static IEnumerable<Vector3I> Directions()
    {
        yield return Vector3I.Left;
        yield return Vector3I.Right;
        yield return Vector3I.Down;
        yield return Vector3I.Up;
        yield return Vector3I.Back;
        yield return Vector3I.Forward;
    }

    private static IEnumerable<Vector3I> EnumerateSurface(Vector3I min, Vector3I max, Vector3I direction)
    {
        for (int z = min.Z; z <= max.Z; z++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    Vector3I position = new Vector3I(x, y, z);
                    if ((direction == Vector3I.Left && x == min.X)
                        || (direction == Vector3I.Right && x == max.X)
                        || (direction == Vector3I.Down && y == min.Y)
                        || (direction == Vector3I.Up && y == max.Y)
                        || (direction == Vector3I.Back && z == min.Z)
                        || (direction == Vector3I.Forward && z == max.Z))
                    {
                        yield return position;
                    }
                }
            }
        }
    }

    private Color GetTeamColor()
    {
        return OwnerSlot switch
        {
            PlayerSlot.Player1 => GameConfig.PlayerColors[0],
            PlayerSlot.Player2 => GameConfig.PlayerColors[1],
            PlayerSlot.Player3 => GameConfig.PlayerColors[2],
            PlayerSlot.Player4 => GameConfig.PlayerColors[3],
            _ => Colors.White,
        };
    }

    private void EnsureVisuals()
    {
        if (_modelRoot != null)
        {
            _modelRoot.QueueFree();
            _modelRoot = null;
        }

        Color teamColor = GetTeamColor();

        // Build the skeleton-based commander model
        CharacterDefinition def = TroopModelGenerator.GenerateCommander(teamColor);
        _modelRoot = VoxelCharacterBuilder.Build(def);
        _modelRoot.Name = "CommanderModel";
        AddChild(_modelRoot);

        // Apply toon shader with team color — use the same method troops use
        VoxelCharacterBuilder.ApplyToonMaterial(_modelRoot, teamColor);

        // Wire up the animation system with the skeleton hierarchy
        _animation?.Initialize(_modelRoot);
    }

}
