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
    private bool _isFalling;
    private Color?[,,]? _voxelGrid;
    private float _voxelSize;

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
    public int MaxHitPoints { get; set; } = 150;

    public int HitPoints { get; private set; } = 150;
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
    /// Called when the weapon loses structural support. Instead of being destroyed,
    /// the weapon drops to the next solid surface below it (gravity anchor).
    /// Only destroyed if there is no solid ground anywhere beneath.
    /// </summary>
    public void DestroyFromLostSupport()
    {
        if (IsDestroyed || _isFalling)
        {
            return;
        }

        _isFalling = true;

        // Try to find the next solid surface below
        VoxelWorld? world = GetTree().Root.GetNodeOrNull<VoxelWorld>("Main/GameWorld");
        if (world == null)
        {
            GD.Print($"[Weapon] {WeaponId} owned by {OwnerSlot} lost support (no world ref)!");
            ApplyDamage(MaxHitPoints);
            return;
        }

        Vector3 weaponPos = GlobalPosition;
        Vector3 cornerPos = weaponPos - new Vector3(
            GameConfig.BuildUnitMeters * 0.5f,
            0f,
            GameConfig.BuildUnitMeters * 0.5f);
        Vector3I microBase = MathHelpers.WorldToMicrovoxel(cornerPos);

        // Scan downward from current position to find the next solid block
        // Check up to 64 blocks down (well past any reasonable structure height)
        const int maxScanDepth = 64;
        int dropDistance = -1;

        for (int dy = 2; dy < maxScanDepth; dy++)
        {
            bool foundSolid = false;
            for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit && !foundSolid; z++)
            {
                for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit && !foundSolid; x++)
                {
                    Vector3I checkPos = microBase + new Vector3I(x, -dy, z);
                    if (world.GetVoxel(checkPos).IsSolid)
                    {
                        foundSolid = true;
                    }
                }
            }
            if (foundSolid)
            {
                // Drop to 1 microvoxel above this solid surface
                dropDistance = dy - 1;
                break;
            }
        }

        if (dropDistance > 0)
        {
            // Animate the fall — use a Tween for smooth gravity drop
            float dropMeters = dropDistance * GameConfig.MicrovoxelMeters;
            Vector3 newPos = GlobalPosition - new Vector3(0, dropMeters, 0);
            GD.Print($"[Weapon] {WeaponId} owned by {OwnerSlot} lost support — falling {dropDistance} blocks.");

            // Duration scales with drop distance (feels like gravity)
            float fallDuration = Mathf.Sqrt(dropMeters * 0.15f);
            fallDuration = Mathf.Clamp(fallDuration, 0.2f, 1.0f);

            Tween tween = CreateTween();
            tween.TweenProperty(this, "global_position", newPos, fallDuration)
                .SetEase(Tween.EaseType.In)
                .SetTrans(Tween.TransitionType.Quad);
            tween.TweenCallback(Callable.From(() => _isFalling = false));
        }
        else
        {
            // No solid ground anywhere below — weapon falls to destruction
            // Animate it falling off-screen then destroy
            GD.Print($"[Weapon] {WeaponId} owned by {OwnerSlot} lost all support — falling to destruction!");
            Vector3 fallTarget = GlobalPosition - new Vector3(0, 30f, 0);
            Tween tween = CreateTween();
            tween.TweenProperty(this, "global_position", fallTarget, 1.5f)
                .SetEase(Tween.EaseType.In)
                .SetTrans(Tween.TransitionType.Quad);
            tween.TweenCallback(Callable.From(() => ApplyDamage(MaxHitPoints)));
        }
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

        // Explosion fireball (scaled to weapon size)
        ExplosionFX.Spawn(fxParent, pos, 1.2f);

        // Break the weapon into actual voxel chunks that tumble away
        if (_voxelGrid != null)
        {
            FallingChunk.CreateFromWeaponVoxels(_voxelGrid, _voxelSize, pos, pos, fxParent);
        }
        else
        {
            // Fallback: generic debris if voxel data wasn't stored
            float weaponVoxelSize = WeaponModelGenerator.GetVoxelSize(WeaponId);
            Color darkMetal = new Color(0.35f, 0.35f, 0.4f);
            DebrisFX.SpawnDebris(fxParent, pos, darkMetal, pos, 8, Voxel.VoxelMaterialType.Metal, weaponVoxelSize);
        }

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
        _voxelGrid = result.VoxelGrid;
        _voxelSize = result.VoxelSize;

        _weaponMesh = new MeshInstance3D();
        _weaponMesh.Name = "WeaponModel";
        _weaponMesh.Mesh = result.Mesh;
        // Palette texture material — same pipeline as world blocks.
        // The palette texture has per-pixel noise that provides micro-contrast,
        // preventing dark tops and washed-out colors under ACES tonemapping.
        StandardMaterial3D weaponMat = new();
        weaponMat.AlbedoTexture = result.PaletteTexture;
        weaponMat.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
        weaponMat.Metallic = 0.0f;
        weaponMat.Roughness = 0.8f;
        weaponMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        weaponMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
        _weaponMesh.MaterialOverride = weaponMat;
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
