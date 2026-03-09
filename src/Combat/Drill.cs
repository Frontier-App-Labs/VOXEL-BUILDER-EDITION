using Godot;
using VoxelSiege.Core;
using VoxelSiege.FX;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Combat;

/// <summary>
/// Tunneling drill weapon. Instead of exploding on contact, the drill
/// projectile burrows into the target structure, destroying blocks in a
/// line as it travels through solid voxels. Penetrates up to 6 blocks
/// before detonating with a small blast at the end.
/// Fire FX: activation sparks + grinding debris spray + vibration.
/// </summary>
public partial class Drill : WeaponBase
{
    /// <summary>
    /// Maximum number of solid blocks the drill can bore through.
    /// </summary>
    private const int MaxPenetrationBlocks = 6;

    public Drill()
    {
        WeaponId = "drill";
        Cost = 400;
        BaseDamage = 60;
        BlastRadiusMicrovoxels = 2f;
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
