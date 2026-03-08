using Godot;
using VoxelSiege.Core;
using VoxelSiege.Voxel;

namespace VoxelSiege.Combat;

public abstract partial class WeaponBase : Node3D
{
    private MeshInstance3D? _baseMesh;
    private MeshInstance3D? _barrelMesh;

    [Export]
    public string WeaponId { get; set; } = string.Empty;

    [Export]
    public int Cost { get; set; }

    [Export]
    public int BaseDamage { get; set; }

    [Export]
    public float BlastRadiusMicrovoxels { get; set; }

    [Export]
    public float ProjectileSpeed { get; set; } = 18f;

    [Export]
    public int CooldownTurns { get; set; }

    public int LastFiredRound { get; protected set; } = -999;
    public PlayerSlot OwnerSlot { get; private set; }

    public override void _Ready()
    {
        AddToGroup("Weapons");
        EnsureVisuals();
    }

    public void AssignOwner(PlayerSlot ownerSlot)
    {
        OwnerSlot = ownerSlot;
        EnsureVisuals();
    }

    public bool CanFire(int currentRound)
    {
        return currentRound - LastFiredRound >= CooldownTurns + 1;
    }

    public virtual ProjectileBase? Fire(AimingSystem aimingSystem, VoxelWorld world, int currentRound)
    {
        if (!CanFire(currentRound))
        {
            return null;
        }

        ProjectileBase projectile = CreateProjectile();
        GetTree().CurrentScene.AddChild(projectile);
        projectile.GlobalPosition = GlobalPosition;
        projectile.Initialize(world, OwnerSlot, aimingSystem.GetLaunchVelocity(ProjectileSpeed), BaseDamage, BlastRadiusMicrovoxels);
        LastFiredRound = currentRound;
        EventBus.Instance?.EmitWeaponFired(new WeaponFiredEvent(OwnerSlot, WeaponId, GlobalPosition, aimingSystem.GetDirection()));
        return projectile;
    }

    protected virtual ProjectileBase CreateProjectile()
    {
        return new ProjectileBase();
    }

    private void EnsureVisuals()
    {
        _baseMesh ??= GetNodeOrNull<MeshInstance3D>("BaseMesh");
        if (_baseMesh == null)
        {
            _baseMesh = new MeshInstance3D();
            _baseMesh.Name = "BaseMesh";
            _baseMesh.Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.45f, 0.9f) };
            AddChild(_baseMesh);
        }

        _barrelMesh ??= GetNodeOrNull<MeshInstance3D>("BarrelMesh");
        if (_barrelMesh == null)
        {
            _barrelMesh = new MeshInstance3D();
            _barrelMesh.Name = "BarrelMesh";
            _barrelMesh.Mesh = new BoxMesh { Size = new Vector3(0.25f, 0.25f, 1.1f) };
            _barrelMesh.Position = new Vector3(0f, 0.18f, -0.7f);
            AddChild(_barrelMesh);
        }

        Color ownerColor = OwnerSlot switch
        {
            PlayerSlot.Player1 => GameConfig.PlayerColors[0],
            PlayerSlot.Player2 => GameConfig.PlayerColors[1],
            PlayerSlot.Player3 => GameConfig.PlayerColors[2],
            PlayerSlot.Player4 => GameConfig.PlayerColors[3],
            _ => Colors.White,
        };

        StandardMaterial3D baseMaterial = new StandardMaterial3D();
        baseMaterial.AlbedoColor = ownerColor.Darkened(0.3f);
        baseMaterial.Roughness = 0.8f;
        _baseMesh.MaterialOverride = baseMaterial;

        StandardMaterial3D barrelMaterial = new StandardMaterial3D();
        barrelMaterial.AlbedoColor = ownerColor.Lightened(0.15f);
        barrelMaterial.Metallic = 0.15f;
        barrelMaterial.Roughness = 0.55f;
        _barrelMesh.MaterialOverride = barrelMaterial;
    }
}
