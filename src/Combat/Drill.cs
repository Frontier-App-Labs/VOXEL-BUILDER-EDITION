using Godot;
using VoxelSiege.Core;
using VoxelSiege.FX;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Combat;

/// <summary>
/// Bunker Buster drill weapon — the anti-fortress tool. Bores a 3x3
/// cross-section tunnel through structures, penetrating up to 5 blocks
/// deep and punching through air gaps to hit the next wall. Detonates
/// with a large interior blast (radius 5) when penetration is exhausted
/// or lifetime expires. Foundation blocks stop it cold.
/// Fire FX: activation sparks + grinding debris spray + vibration.
/// </summary>
public partial class Drill : WeaponBase
{
    /// <summary>
    /// Maximum number of solid blocks the drill can bore through.
    /// Shorter penetration but higher damage — designed to crack tough blocks
    /// rather than tunnel deep.
    /// </summary>
    private const int MaxPenetrationBlocks = 5;

    public Drill()
    {
        WeaponId = "drill";
        Cost = 550;
        BaseDamage = 70;
        BlastRadiusMicrovoxels = 4f;
        ProjectileSpeed = 14f;
        CooldownTurns = 0;
    }

    public override ProjectileBase? Fire(AimingSystem aimingSystem, VoxelWorld world, int currentRound)
    {
        if (!CanFire(currentRound))
        {
            return null;
        }

        Vector3 launchVelocity = aimingSystem.GetLaunchVelocity(ProjectileSpeed);

        ProjectileBase projectile = CreateProjectile();
        // Drill is a direct-fire weapon: aiming computes a straight-line trajectory
        // (Atan2 pitch, no ballistic arc), so disable gravity on the projectile to
        // match. Without this, gravity pulls the drill below the aimed direction and
        // it misses the clicked voxel.
        projectile.GravityMultiplier = 0f;
        GetTree().CurrentScene.AddChild(projectile);
        projectile.GlobalPosition = GlobalPosition;
        projectile.Initialize(world, OwnerSlot, launchVelocity, BaseDamage, BlastRadiusMicrovoxels);

        // Enable drill/burrowing behavior: don't explode on surface contact,
        // instead tunnel through solid blocks and destroy them
        projectile.SetDrillMode(MaxPenetrationBlocks);

        // Drill trail (dirt/debris chunks)
        TrailFX.CreateDrillTrail(projectile);

        LastFiredRound = currentRound;

        // Drill-specific FX: activation sparks + grinding debris + vibration
        SpawnWeaponFireFX(aimingSystem.GetDirection());

        AudioDirector.Instance?.PlaySFX("drill_fire", GlobalPosition);
        EventBus.Instance?.EmitWeaponFired(new WeaponFiredEvent(OwnerSlot, WeaponId, GlobalPosition, aimingSystem.GetDirection()));
        return projectile;
    }

    protected override void SpawnWeaponFireFX(Vector3 aimDirection)
    {
        WeaponFX.SpawnDrillFireFX(this, GlobalPosition, aimDirection);

        // Vibration: drill shakes instead of recoiling
        if (WeaponMesh != null)
            WeaponFX.AnimateVibration(WeaponMesh, intensity: 0.03f, duration: 0.3f);

        EnableIdleSmoke();
    }

    protected override string GetProjectileType()
    {
        return "drill_bit";
    }
}
