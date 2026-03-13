using Godot;
using VoxelSiege.Core;
using VoxelSiege.FX;
using VoxelSiege.Voxel;

namespace VoxelSiege.Combat;

/// <summary>
/// Guided missile launcher. Fires a slower projectile that gently curves
/// toward the target, producing a large explosion on impact. The homing
/// is subtle -- the missile adjusts its velocity each frame by steering
/// toward the predicted aim point.
/// Fire FX: backblast flame + launch smoke cloud + side flame jets.
/// </summary>
public partial class MissileLauncher : WeaponBase
{
    public MissileLauncher()
    {
        WeaponId = "missile";
        Cost = 850;
        BaseDamage = 50;
        BlastRadiusMicrovoxels = 8f;
        ProjectileSpeed = 20f;
        CooldownTurns = 0;
    }

    public override ProjectileBase? Fire(AimingSystem aimingSystem, VoxelWorld world, int currentRound)
    {
        if (!CanFire(currentRound))
        {
            return null;
        }

        Vector3 launchVelocity = aimingSystem.GetLaunchVelocity(ProjectileSpeed);
        Vector3 targetDirection = aimingSystem.GetDirection();

        // Determine the homing target point:
        // When the player clicked a specific voxel (HasTarget), use that exact world
        // position so the missile homes precisely to the clicked point. Otherwise
        // estimate a target by projecting the launch velocity forward.
        Vector3 targetPoint;
        if (aimingSystem.HasTarget)
        {
            targetPoint = aimingSystem.TargetPoint;
        }
        else
        {
            float estimatedFlightTime = 3f;
            float gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
            targetPoint = GlobalPosition + launchVelocity * estimatedFlightTime;
            targetPoint.Y -= 0.5f * gravity * estimatedFlightTime * estimatedFlightTime;
        }

        ProjectileBase projectile = CreateProjectile();
        GetTree().CurrentScene.AddChild(projectile);
        projectile.GlobalPosition = GlobalPosition;
        projectile.GravityMultiplier = 0.3f; // Reduced gravity — homing thrust counteracts most of it
        projectile.Initialize(world, OwnerSlot, launchVelocity, BaseDamage, BlastRadiusMicrovoxels);

        // Enable homing behavior on the projectile
        projectile.SetHoming(targetPoint, homingStrength: 3.5f);

        // Rocket trail (fire + smoke)
        TrailFX.CreateRocketTrail(projectile);

        LastFiredRound = currentRound;

        RecordFireDirection(targetDirection);

        // Missile-specific FX: backblast + smoke cloud + side jets
        SpawnWeaponFireFX(targetDirection);

        AudioDirector.Instance?.PlaySFX("missile_fire", GlobalPosition);
        EventBus.Instance?.EmitWeaponFired(new WeaponFiredEvent(OwnerSlot, WeaponId, GlobalPosition, targetDirection));
        return projectile;
    }

    protected override void SpawnWeaponFireFX(Vector3 aimDirection)
    {
        WeaponFX.SpawnMissileFireFX(this, GlobalPosition, aimDirection);

        // Recoil: launcher rocks backward from the launch force
        if (WeaponMesh != null)
            WeaponFX.AnimateRecoil(WeaponMesh, aimDirection, distance: 0.08f, duration: 0.4f);

        EnableIdleSmoke();
    }

    protected override string GetProjectileType()
    {
        return "missile";
    }
}
