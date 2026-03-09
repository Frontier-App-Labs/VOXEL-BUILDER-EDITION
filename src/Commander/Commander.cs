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
    }

    public override void _Process(double delta)
    {
        if (_health?.IsDead ?? false)
        {
            return;
        }

        // Animation is now handled by CommanderAnimation._Process
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
        // Check the voxel directly beneath the commander's feet.
        // Convert the commander's world position to microvoxel coords and sample
        // one microvoxel below the feet.
        bool hasGround = false;
        if (_cachedWorld != null)
        {
            Vector3 feetPos = GlobalPosition;
            Vector3I microPos = MathHelpers.WorldToMicrovoxel(feetPos);
            // Check the voxel at our feet and one below
            if (_cachedWorld.GetVoxel(microPos).IsSolid
                || _cachedWorld.GetVoxel(microPos + Vector3I.Down).IsSolid)
            {
                hasGround = true;
            }
        }

        if (!hasGround)
        {
            // --- Falling ---
            if (!_isFalling)
            {
                // Just started falling – record the starting height
                _isFalling = true;
                _fallStartY = GlobalPosition.Y;
                _animation?.SetState(CommanderAnimationState.Falling);
            }

            _verticalVelocity -= GameConfig.CommanderGravity * dt;
            Vector3 pos = GlobalPosition;
            pos.Y += _verticalVelocity * dt;
            GlobalPosition = pos;
        }
        else if (_isFalling)
        {
            // --- Just landed ---
            _isFalling = false;
            float fallDistance = _fallStartY - GlobalPosition.Y;
            _verticalVelocity = 0f;

            if (fallDistance > GameConfig.CommanderFallDamageMinHeight)
            {
                float excessHeight = fallDistance - GameConfig.CommanderFallDamageMinHeight;
                int damage = Mathf.CeilToInt(excessHeight * GameConfig.CommanderFallDamagePerMeter);
                ApplyDamage(damage, null, GlobalPosition + Vector3.Down);
            }

            // Restore the appropriate animation state
            if (!(_health?.IsDead ?? false))
            {
                if (IsExposed)
                {
                    _animation?.SetState(CommanderAnimationState.Panic);
                }
                else
                {
                    _animation?.SetState(CommanderAnimationState.Idle);
                }
            }
        }
    }

    public void PlaceCommander(VoxelWorld world, Vector3I buildUnitPosition)
    {
        _cachedWorld = world;
        BuildUnitPosition = buildUnitPosition;
        Vector3I microBase = MathHelpers.BuildToMicrovoxel(buildUnitPosition);
        Vector3 worldBase = MathHelpers.MicrovoxelToWorld(microBase);
        Position = worldBase + new Vector3(GameConfig.BuildUnitMeters * 0.5f, GameConfig.BuildUnitMeters, GameConfig.BuildUnitMeters * 0.5f);
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
                    _animation?.SetState(CommanderAnimationState.Panic);
                    return true;
                }
            }
        }

        IsExposed = false;
        if (!(_health?.IsDead ?? false))
        {
            _animation?.SetState(CommanderAnimationState.Idle);
        }

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
            _animation?.SetState(IsExposed ? CommanderAnimationState.Panic : CommanderAnimationState.Flinch);
        }
    }

    private async void OnDied()
    {
        _animation?.SetState(CommanderAnimationState.Dead);

        // Create the ragdoll from the body parts
        ActivateRagdollDeath();

        // Slow-motion for dramatic effect
        Engine.TimeScale = GameConfig.SlowMoTimeScale;
        EventBus.Instance?.EmitCommanderKilled(new CommanderKilledEvent(OwnerSlot, _lastInstigator, GlobalPosition));
        await ToSignal(GetTree().CreateTimer(GameConfig.SlowMoDuration * GameConfig.SlowMoTimeScale), SceneTreeTimer.SignalName.Timeout);
        Engine.TimeScale = 1f;
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
