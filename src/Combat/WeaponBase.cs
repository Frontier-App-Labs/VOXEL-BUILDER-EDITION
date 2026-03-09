using System.Diagnostics;
using Godot;
using VoxelSiege.Art;
using VoxelSiege.Camera;
using VoxelSiege.Core;
using VoxelSiege.FX;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;

namespace VoxelSiege.Combat;

public abstract partial class WeaponBase : Node3D
{
    private MeshInstance3D? _weaponMesh;
    private MeshInstance3D? _highlightOverlay;
    private GpuParticles3D? _idleSmoke;
    private bool _hasFiredOnce;
    private float _idleScanTime;
    private float _idleScanAngle;
    private bool _ownerAssigned;
    private bool _isHighlighted;
    private float _supportCheckTimer;

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
    public int MaxHitPoints { get; set; } = 120;

    public int HitPoints { get; private set; } = 120;
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
        float dt = (float)delta;
        AnimateIdleScan(dt);
        CheckSupportPeriodically(dt);
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
    /// Spawns destruction FX when a weapon is destroyed: explosion, substantial debris,
    /// dust cloud, and camera shake for a dramatic breakup effect.
    /// Debris uses the weapon's actual voxel scale (0.12-0.18m) rather than the world
    /// MicrovoxelMeters (0.5m) so pieces are appropriately small shrapnel, not full-sized blocks.
    /// </summary>
    private void SpawnDestructionFX()
    {
        Node fxParent = GetTree().Root;
        Vector3 pos = GlobalPosition;

        // Use the weapon's own voxel size so debris is proportional to the weapon model
        float weaponVoxelSize = WeaponModelGenerator.GetVoxelSize(WeaponId);

        // Explosion fireball (scaled to weapon size)
        ExplosionFX.Spawn(fxParent, pos, 1.2f);

        // Debris burst: dark metal shrapnel + charred fragments flying outward
        // to look like the weapon actually exploded — sized to weapon voxels
        Color darkMetal = new Color(0.35f, 0.35f, 0.4f);
        Color charred = new Color(0.2f, 0.18f, 0.15f);
        Color hotMetal = new Color(0.8f, 0.4f, 0.1f);
        DebrisFX.SpawnDebris(fxParent, pos, darkMetal, pos, 6, Voxel.VoxelMaterialType.Metal, weaponVoxelSize);
        DebrisFX.SpawnDebris(fxParent, pos + Vector3.Up * 0.2f, charred, pos, 5, Voxel.VoxelMaterialType.Metal, weaponVoxelSize);
        DebrisFX.SpawnDebris(fxParent, pos + Vector3.Up * 0.1f, hotMetal, pos, 3, Voxel.VoxelMaterialType.Metal, weaponVoxelSize);

        // Smoke / dust cloud
        DustFX.Spawn(fxParent, pos, 1.0f, Voxel.VoxelMaterialType.Metal);

        // Camera shake for nearby destruction
        CameraShake.Instance?.Shake(0.15f, 0.3f);
    }

    /// <summary>
    /// Periodically checks that the weapon still has structural support (solid
    /// voxels beneath it). This catches cases where the floor is removed by
    /// fire, falling chunks, or other non-explosion causes. Runs every 0.5s
    /// to avoid per-frame overhead. The same footprint check used by
    /// Explosion.CheckWeaponSupport is duplicated here for consistency.
    /// </summary>
    private void CheckSupportPeriodically(float delta)
    {
        const float checkInterval = 0.5f;
        _supportCheckTimer += delta;
        if (_supportCheckTimer < checkInterval)
        {
            return;
        }
        _supportCheckTimer = 0f;

        // Only check support during combat — during build phase the player is still
        // constructing and the foundation may not be complete yet.
        GameManager? gm = GetTree().Root.GetNodeOrNull<GameManager>("Main");
        if (gm == null || gm.CurrentPhase != GamePhase.Combat)
        {
            return;
        }

        VoxelWorld? world = GetTree().Root.GetNodeOrNull<VoxelWorld>("Main/GameWorld");
        if (world == null)
        {
            return;
        }

        Vector3 weaponPos = GlobalPosition;
        Vector3 cornerPos = weaponPos - new Vector3(
            GameConfig.BuildUnitMeters * 0.5f,
            0f,
            GameConfig.BuildUnitMeters * 0.5f);
        Vector3I microBase = MathHelpers.WorldToMicrovoxel(cornerPos);

        // Only check directly below the weapon (y = -1), matching Explosion.cs logic.
        // Don't check the weapon's own footprint (y >= 0) — weapons are meshes, not voxels.
        bool hasSupport = false;
        for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit && !hasSupport; z++)
        {
            for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit && !hasSupport; x++)
            {
                Vector3I below = microBase + new Vector3I(x, -1, z);
                if (world.GetVoxel(below).IsSolid)
                {
                    hasSupport = true;
                }
            }
        }

        if (!hasSupport)
        {
            DestroyFromLostSupport();
        }
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

    /// <summary>
    /// Toggles a green selection highlight on/off for this weapon's 3D model.
    /// Uses the outline_highlight shader for a pulsing green glow overlay
    /// so the player can see which weapon they are about to fire.
    /// </summary>
    public void SetHighlighted(bool highlighted)
    {
        if (_isHighlighted == highlighted)
        {
            return;
        }

        _isHighlighted = highlighted;

        if (highlighted)
        {
            EnsureHighlightOverlay();
            if (_highlightOverlay != null)
            {
                _highlightOverlay.Visible = true;
            }
        }
        else
        {
            if (_highlightOverlay != null)
            {
                _highlightOverlay.Visible = false;
            }
        }
    }

    /// <summary>
    /// Creates the highlight overlay mesh on first use. The overlay is a duplicate
    /// of the weapon mesh rendered with the outline_highlight shader in green,
    /// slightly expanded via the shader's vertex offset so it wraps the weapon.
    /// </summary>
    private void EnsureHighlightOverlay()
    {
        if (_highlightOverlay != null || _weaponMesh == null)
        {
            return;
        }

        _highlightOverlay = new MeshInstance3D();
        _highlightOverlay.Name = "SelectionHighlight";
        _highlightOverlay.Mesh = _weaponMesh.Mesh;

        // Use the outline_highlight shader for a pulsing green glow
        if (ResourceLoader.Exists("res://assets/shaders/outline_highlight.gdshader"))
        {
            Shader shader = GD.Load<Shader>("res://assets/shaders/outline_highlight.gdshader");
            ShaderMaterial shaderMat = new ShaderMaterial();
            shaderMat.Shader = shader;
            shaderMat.SetShaderParameter("highlight_color", new Color(0.15f, 0.95f, 0.3f, 0.75f));
            shaderMat.SetShaderParameter("pulse_speed", 2.0f);
            shaderMat.SetShaderParameter("fresnel_power", 1.8f);
            shaderMat.SetShaderParameter("glow_width", 0.7f);
            shaderMat.SetShaderParameter("glow_intensity", 2.2f);
            shaderMat.SetShaderParameter("outline_width", 0.025f);
            _highlightOverlay.MaterialOverride = shaderMat;
        }
        else
        {
            // Fallback: simple green translucent emission material
            StandardMaterial3D mat = new StandardMaterial3D();
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.AlbedoColor = new Color(0.15f, 0.9f, 0.3f, 0.35f);
            mat.EmissionEnabled = true;
            mat.Emission = new Color(0.15f, 0.9f, 0.3f);
            mat.EmissionEnergyMultiplier = 2.0f;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            _highlightOverlay.MaterialOverride = mat;
        }

        // Add as child of the weapon mesh so it follows idle scan rotation
        _weaponMesh.AddChild(_highlightOverlay);
    }

    private void EnsureVisuals()
    {
        // Clean up highlight overlay (it's a child of _weaponMesh, so it would be
        // freed with the mesh, but we null the reference to force recreation)
        _highlightOverlay = null;

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

        // Recreate highlight overlay if the weapon was highlighted before rebuild
        if (_isHighlighted)
        {
            EnsureHighlightOverlay();
            if (_highlightOverlay != null)
            {
                _highlightOverlay.Visible = true;
            }
        }
    }
}
