using Godot;
using VoxelSiege.Core;
using VoxelSiege.FX;
using VoxelSiege.Voxel;

namespace VoxelSiege.Combat;

/// <summary>
/// Basic ballistic cannon. Fires a standard cannonball with an arc trajectory
/// and moderate blast radius. The bread-and-butter weapon.
/// Fire FX: big yellow-orange flash + smoke ring + recoil slide-back.
/// </summary>
public partial class Cannon : WeaponBase
{
    public Cannon()
    {
        WeaponId = "cannon";
        Cost = 500;
        BaseDamage = 30;
        BlastRadiusMicrovoxels = 4f;
        ProjectileSpeed = 28f;
        CooldownTurns = 0;
    }

    public override ProjectileBase? Fire(AimingSystem aimingSystem, VoxelWorld world, int currentRound)
    {
        if (!CanFire(currentRound))
        {
            return null;
        }

        ProjectileBase projectile = CreateProjectile();
        GetTree().CurrentScene.AddChild(projectile);
        projectile.GlobalPosition = GlobalPosition;
        projectile.Initialize(world, OwnerSlot, aimingSystem.GetLaunchVelocity(ProjectileSpeed), BaseDamage, BlastRadiusMicrovoxels);

        // Smoke trail behind the cannonball
        TrailFX.CreateSmokeTrail(projectile);

        LastFiredRound = currentRound;

        // Cannon-specific FX: big flash + smoke ring + recoil
        SpawnWeaponFireFX(aimingSystem.GetDirection());

        AudioDirector.Instance?.PlaySFX("cannon_fire", GlobalPosition);
        EventBus.Instance?.EmitWeaponFired(new WeaponFiredEvent(OwnerSlot, WeaponId, GlobalPosition, aimingSystem.GetDirection()));
        return projectile;
    }

    protected override void SpawnWeaponFireFX(Vector3 aimDirection)
    {
        WeaponFX.SpawnCannonFireFX(this, GlobalPosition, aimDirection);

        // Recoil: weapon model slides back briefly then returns
        if (WeaponMesh != null)
            WeaponFX.AnimateRecoil(WeaponMesh, aimDirection, distance: 0.12f, duration: 0.35f);

        EnableIdleSmoke();
    }

    protected override string GetProjectileType()
    {
        return "cannonball";
    }
}
