using System.Diagnostics;
using Godot;
using VoxelSiege.Art;
using VoxelSiege.Core;
using VoxelSiege.FX;
using VoxelSiege.Voxel;

namespace VoxelSiege.Combat;

public abstract partial class WeaponBase : Node3D
{
    private MeshInstance3D? _weaponMesh;
    private GpuParticles3D? _idleSmoke;
    private bool _hasFiredOnce;
    private float _idleScanTime;
    private float _idleScanAngle;
    private bool _ownerAssigned;

    [Export]
    public string WeaponId { get; set; } = string.Empty;

    [Export]
    public int Cost { get; set; }

    [Export]
    public int BaseDamage { get; set; }

    [Export]
    public float BlastRadiusMicrovoxels { get; set; }

    [Export]
    public float ProjectileSpeed { get; set; } = 28f;

    [Export]
    public int CooldownTurns { get; set; }

    [Export]
    public int MaxHitPoints { get; set; } = 50;

    public int HitPoints { get; private set; } = 50;
    public bool IsDestroyed { get; private set; }
    public int LastFiredRound { get; protected set; } = -999;
    public PlayerSlot OwnerSlot { get; private set; }

    /// <summary>
    /// Exposes the weapon mesh for recoil/vibration animations from subclasses.
    /// </summary>
    protected MeshInstance3D? WeaponMesh => _weaponMesh;

    public override void _Ready()
    {
        AddToGroup("Weapons");
        HitPoints = MaxHitPoints;
        EnsureVisuals();
    }

    public override void _Process(double delta)
    {
        AnimateIdleScan((float)delta);
    }

    public void AssignOwner(PlayerSlot ownerSlot)
    {
        OwnerSlot = ownerSlot;
        _ownerAssigned = true;
        EnsureVisuals();
    }

    public bool CanFire(int currentRound)
    {
        return !IsDestroyed && currentRound - LastFiredRound >= CooldownTurns + 1;
    }

    /// <summary>
    /// Applies explosion damage to this weapon. If HP reaches zero the weapon
    /// is visually destroyed, destruction FX are spawned, an event is emitted
    /// so combat lists update, and the node is freed after a short delay.
    /// </summary>
    public void ApplyDamage(int damage)
    {
        if (IsDestroyed || damage <= 0)
        {
            return;
        }

        HitPoints -= damage;
        if (HitPoints <= 0)
        {
            HitPoints = 0;
            IsDestroyed = true;
            GD.Print($"[Weapon] {WeaponId} owned by {OwnerSlot} destroyed!");

            // Emit event so GameManager / CombatUI can react
            EventBus.Instance?.EmitWeaponDestroyed(
                new WeaponDestroyedEvent(OwnerSlot, WeaponId, GlobalPosition));

            // Spawn destruction VFX: small explosion + debris
            SpawnDestructionFX();

            // Visual destruction: hide the mesh and stop processing
            if (_weaponMesh != null)
            {
                _weaponMesh.Visible = false;
            }
            if (_idleSmoke != null)
            {
                _idleSmoke.Emitting = false;
            }
            SetProcess(false);

            // Remove the weapon node from the scene tree after a brief delay
            // so events can finish propagating
            GetTree().CreateTimer(0.1).Timeout += QueueFree;
        }
    }

    /// <summary>
    /// Called when the weapon is destroyed due to loss of structural support
    /// (the voxels below it have been removed). Instantly destroys the weapon.
    /// </summary>
    public void DestroyFromLostSupport()
    {
        if (IsDestroyed)
        {
            return;
        }

        GD.Print($"[Weapon] {WeaponId} owned by {OwnerSlot} lost structural support!");
        ApplyDamage(MaxHitPoints);
    }

    public virtual ProjectileBase? Fire(AimingSystem aimingSystem, VoxelWorld world, int currentRound)
    {
        Debug.Assert(_ownerAssigned, $"WeaponBase.Fire() called on '{WeaponId}' before AssignOwner was called. OwnerSlot defaults to Player1 and may be incorrect.");

        if (!CanFire(currentRound))
        {
            return null;
        }

        ProjectileBase projectile = CreateProjectile();
        GetTree().CurrentScene.AddChild(projectile);
        projectile.GlobalPosition = GlobalPosition;
        Vector3 aimDirection = aimingSystem.GetDirection();
        projectile.Initialize(world, OwnerSlot, aimingSystem.GetLaunchVelocity(ProjectileSpeed), BaseDamage, BlastRadiusMicrovoxels);
        LastFiredRound = currentRound;

        // Weapon firing FX: muzzle flash + smoke puff at barrel
        SpawnWeaponFireFX(aimDirection);

        EventBus.Instance?.EmitWeaponFired(new WeaponFiredEvent(OwnerSlot, WeaponId, GlobalPosition, aimDirection));
        return projectile;
    }

    protected virtual ProjectileBase CreateProjectile()
    {
        ProjectileBase projectile = new();
        projectile.SetProjectileType(GetProjectileType());
        return projectile;
    }

    /// <summary>
    /// Maps weapon ID to projectile visual type.
    /// </summary>
    protected virtual string GetProjectileType()
    {
        return WeaponId switch
        {
            "cannon" => "cannonball",
            "mortar" => "mortar_shell",
            "railgun" => "rail_slug",
            "missile" => "missile",
            "drill" => "drill_bit",
            _ => "cannonball",
        };
    }

    /// <summary>
    /// Spawns weapon-type-specific fire FX at the weapon position, enables
    /// idle smoke wisps after the first shot, and triggers recoil animation.
    /// Subclasses override this to call their specific WeaponFX method.
    /// Called by Fire() and can be called by subclass overrides.
    /// </summary>
    protected virtual void SpawnWeaponFireFX(Vector3 aimDirection)
    {
        // Default: generic muzzle flash + smoke puff (fallback for unknown weapons)
        Node sceneRoot = GetTree().Root;
        WeaponFX.SpawnMuzzleFlash(sceneRoot, GlobalPosition, aimDirection);
        WeaponFX.SpawnSmokePuff(sceneRoot, GlobalPosition);

        EnableIdleSmoke();
    }

    /// <summary>
    /// Enables idle smoke wisps after the weapon has fired at least once.
    /// Called by SpawnWeaponFireFX and subclass overrides.
    /// </summary>
    protected void EnableIdleSmoke()
    {
        if (!_hasFiredOnce)
        {
            _hasFiredOnce = true;
            if (_idleSmoke != null)
            {
                _idleSmoke.Emitting = true;
            }
        }
    }

    /// <summary>
    /// Spawns a small explosion and debris at the weapon's position when it is destroyed.
    /// </summary>
    private void SpawnDestructionFX()
    {
        Node fxParent = GetTree().Root;
        Vector3 pos = GlobalPosition;

        // Small explosion fireball
        ExplosionFX.Spawn(fxParent, pos, 0.8f);

        // Debris in the team color
        Color teamColor = GetOwnerColor();
        DebrisFX.SpawnDebris(fxParent, pos, teamColor, pos, 4, Voxel.VoxelMaterialType.Metal);

        // Dust puff
        DustFX.Spawn(fxParent, pos, 0.6f, Voxel.VoxelMaterialType.Metal);
    }

    private void AnimateIdleScan(float delta)
    {
        if (_weaponMesh == null)
        {
            return;
        }

        _idleScanTime += delta;
        // Gentle scanning rotation on Y axis
        float targetAngle = Mathf.Sin(_idleScanTime * 0.8f) * 0.15f;
        _idleScanAngle = Mathf.Lerp(_idleScanAngle, targetAngle, delta * 2f);
        _weaponMesh.Rotation = new Vector3(0, _idleScanAngle, 0);
    }

    private Color GetOwnerColor()
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
        if (_weaponMesh != null)
        {
            _weaponMesh.QueueFree();
            _weaponMesh = null;
        }

        if (_idleSmoke != null)
        {
            _idleSmoke.QueueFree();
            _idleSmoke = null;
        }

        Color teamColor = GetOwnerColor();
        WeaponModelResult result = WeaponModelGenerator.Generate(WeaponId, teamColor);

        _weaponMesh = new MeshInstance3D();
        _weaponMesh.Name = "WeaponModel";
        _weaponMesh.Mesh = result.Mesh;
        _weaponMesh.MaterialOverride = VoxelModelBuilder.CreateVoxelMaterial(0.15f, 0.6f);
        AddChild(_weaponMesh);

        // Create idle smoke emitter (starts disabled, enabled after first fire)
        _idleSmoke = WeaponFX.CreateIdleSmoke();
        AddChild(_idleSmoke);
        if (_hasFiredOnce)
        {
            _idleSmoke.Emitting = true;
        }
    }
}
