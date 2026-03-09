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
    private MeshInstance3D? _headMesh;
    private MeshInstance3D? _torsoMesh;
    private MeshInstance3D? _leftArmMesh;
    private MeshInstance3D? _rightArmMesh;
    private MeshInstance3D? _leftLegMesh;
    private MeshInstance3D? _rightLegMesh;
    private CommanderBodyParts _bodyParts;
    private PlayerSlot? _lastInstigator;
    private Vector3 _lastImpactDirection = Vector3.Up;

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

    public void PlaceCommander(VoxelWorld world, Vector3I buildUnitPosition)
    {
        BuildUnitPosition = buildUnitPosition;
        Vector3I microBase = MathHelpers.BuildToMicrovoxel(buildUnitPosition);
        Vector3 worldBase = MathHelpers.MicrovoxelToWorld(microBase);
        Position = worldBase + new Vector3(GameConfig.BuildUnitMeters * 0.5f, GameConfig.BuildUnitMeters, GameConfig.BuildUnitMeters * 0.5f);
        EnsureVisuals();
        EvaluateExposure(world);
    }

    public bool ApplyDamage(int damage, PlayerSlot? instigator = null, Vector3? impactOrigin = null)
    {
        _lastInstigator = instigator;
        if (impactOrigin.HasValue)
        {
            _lastImpactDirection = (GlobalPosition - impactOrigin.Value).Normalized();
        }

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
        if (remainingHealth > 0)
        {
            _animation?.SetState(IsExposed ? CommanderAnimationState.Panic : CommanderAnimationState.Flinch);
            EventBus.Instance?.EmitCommanderDamaged(new CommanderDamagedEvent(OwnerSlot, damage, remainingHealth, GlobalPosition));
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

        // Collect mesh references before hiding the model
        MeshInstance3D?[] meshes = new MeshInstance3D?[]
        {
            _headMesh,
            _torsoMesh,
            _leftArmMesh,
            _rightArmMesh,
            _leftLegMesh,
            _rightLegMesh,
        };

        // Hide the animated model
        _modelRoot.Visible = false;

        // Death impulse: direction of the killing blow + dramatic upward launch
        float deathForce = 8f;
        Vector3 impulseDir = _lastImpactDirection;

        // If the impact direction is mostly zero (e.g., no origin given), launch upward
        if (impulseDir.LengthSquared() < 0.01f)
        {
            impulseDir = Vector3.Up;
        }

        // Activate the ragdoll with full body part data
        _ragdoll.Activate(_bodyParts, GlobalTransform, impulseDir, deathForce, meshes);
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
        _bodyParts = CommanderModelGenerator.Generate(teamColor);

        _modelRoot = new Node3D();
        _modelRoot.Name = "CommanderModel";
        AddChild(_modelRoot);

        // Load toon shader material
        ShaderMaterial? toonMat = VoxelModelBuilder.CreateToonMaterial();
        if (toonMat != null)
        {
            toonMat.SetShaderParameter("team_color", new Godot.Color(teamColor.R, teamColor.G, teamColor.B, 1f));
        }

        // Fallback material if shader not found
        StandardMaterial3D fallbackMat = VoxelModelBuilder.CreateVoxelMaterial(0.0f, 0.8f);

        _headMesh = CreateBodyPartMesh("Head", _bodyParts.HeadMesh, _bodyParts.HeadRegion, toonMat, fallbackMat);
        _torsoMesh = CreateBodyPartMesh("Torso", _bodyParts.TorsoMesh, _bodyParts.TorsoRegion, toonMat, fallbackMat);
        _leftArmMesh = CreateBodyPartMesh("LeftArm", _bodyParts.LeftArmMesh, _bodyParts.LeftArmRegion, toonMat, fallbackMat);
        _rightArmMesh = CreateBodyPartMesh("RightArm", _bodyParts.RightArmMesh, _bodyParts.RightArmRegion, toonMat, fallbackMat);
        _leftLegMesh = CreateBodyPartMesh("LeftLeg", _bodyParts.LeftLegMesh, _bodyParts.LeftLegRegion, toonMat, fallbackMat);
        _rightLegMesh = CreateBodyPartMesh("RightLeg", _bodyParts.RightLegMesh, _bodyParts.RightLegRegion, toonMat, fallbackMat);

        // Wire up the animation system with body part references
        _animation?.SetBodyParts(
            _headMesh, _torsoMesh,
            _leftArmMesh, _rightArmMesh,
            _leftLegMesh, _rightLegMesh,
            _bodyParts.HeadRegion.CenterOffset,
            _bodyParts.TorsoRegion.CenterOffset,
            _bodyParts.LeftArmRegion.CenterOffset,
            _bodyParts.RightArmRegion.CenterOffset,
            _bodyParts.LeftLegRegion.CenterOffset,
            _bodyParts.RightLegRegion.CenterOffset
        );
    }

    private MeshInstance3D CreateBodyPartMesh(string name, ArrayMesh mesh, CommanderBodyPartRegion region, ShaderMaterial? toonMat, StandardMaterial3D fallbackMat)
    {
        MeshInstance3D instance = new();
        instance.Name = name;
        instance.Mesh = mesh;
        instance.MaterialOverride = (Material?)toonMat?.Duplicate() ?? fallbackMat;
        _modelRoot!.AddChild(instance);
        return instance;
    }
}
