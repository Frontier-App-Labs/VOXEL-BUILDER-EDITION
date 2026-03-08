using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;

namespace VoxelSiege.Commander;

public partial class Commander : Node3D
{
    private CommanderHealth? _health;
    private CommanderAnimation? _animation;
    private CommanderRagdoll? _ragdoll;
    private MeshInstance3D? _bodyMesh;
    private MeshInstance3D? _hatMesh;
    private PlayerSlot? _lastInstigator;
    private Vector3 _lastImpactDirection = Vector3.Up;

    [Export]
    public PlayerSlot OwnerSlot { get; set; } = PlayerSlot.Player1;

    [Export]
    public Vector3I BuildUnitPosition { get; private set; }

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
        if (_bodyMesh != null)
        {
            _bodyMesh.Visible = false;
        }

        if (_hatMesh != null)
        {
            _hatMesh.Visible = false;
        }

        _ragdoll?.ActivateRagdoll(_lastImpactDirection * 8f);
        Engine.TimeScale = GameConfig.SlowMoTimeScale;
        EventBus.Instance?.EmitCommanderKilled(new CommanderKilledEvent(OwnerSlot, _lastInstigator, GlobalPosition));
        await ToSignal(GetTree().CreateTimer(GameConfig.SlowMoDuration * GameConfig.SlowMoTimeScale), SceneTreeTimer.SignalName.Timeout);
        Engine.TimeScale = 1f;
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

    private void EnsureVisuals()
    {
        _bodyMesh ??= GetNodeOrNull<MeshInstance3D>("BodyMesh");
        if (_bodyMesh == null)
        {
            _bodyMesh = new MeshInstance3D();
            _bodyMesh.Name = "BodyMesh";
            _bodyMesh.Mesh = new BoxMesh { Size = new Vector3(0.5f, 1.1f, 0.45f) };
            _bodyMesh.Position = new Vector3(0f, 0.55f, 0f);
            AddChild(_bodyMesh);
        }

        _hatMesh ??= GetNodeOrNull<MeshInstance3D>("HatMesh");
        if (_hatMesh == null)
        {
            _hatMesh = new MeshInstance3D();
            _hatMesh.Name = "HatMesh";
            _hatMesh.Mesh = new BoxMesh { Size = new Vector3(0.55f, 0.2f, 0.55f) };
            _hatMesh.Position = new Vector3(0f, 1.2f, 0f);
            AddChild(_hatMesh);
        }

        Color bodyColor = OwnerSlot switch
        {
            PlayerSlot.Player1 => GameConfig.PlayerColors[0],
            PlayerSlot.Player2 => GameConfig.PlayerColors[1],
            PlayerSlot.Player3 => GameConfig.PlayerColors[2],
            PlayerSlot.Player4 => GameConfig.PlayerColors[3],
            _ => Colors.White,
        };

        StandardMaterial3D bodyMaterial = new StandardMaterial3D();
        bodyMaterial.AlbedoColor = bodyColor;
        bodyMaterial.Roughness = 0.8f;
        _bodyMesh.MaterialOverride = bodyMaterial;

        StandardMaterial3D hatMaterial = new StandardMaterial3D();
        hatMaterial.AlbedoColor = new Color("2f3642");
        hatMaterial.Roughness = 0.7f;
        _hatMesh.MaterialOverride = hatMaterial;
    }
}
