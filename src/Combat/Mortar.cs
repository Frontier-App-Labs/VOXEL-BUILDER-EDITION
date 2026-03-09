using Godot;
using VoxelSiege.Core;
using VoxelSiege.FX;
using VoxelSiege.Voxel;

namespace VoxelSiege.Combat;

/// <summary>
/// High-arc mortar weapon. Launches shells in a steep parabola for lobbing
/// over walls. Larger blast radius than the cannon but less damage per block.
/// Fire FX: upward smoke burst from tube mouth + subtle flash + mild recoil.
/// </summary>
public partial class Mortar : WeaponBase
{
    /// <summary>
    /// Extra upward velocity bias to force a high arc trajectory.
    /// </summary>
    private const float ArcBiasY = 0.25f;

    public Mortar()
    {
        WeaponId = "mortar";
        Cost = 600;
        BaseDamage = 25;
        BlastRadiusMicrovoxels = 6f;
        ProjectileSpeed = 30f;
        CooldownTurns = 0;
    }

    public override ProjectileBase? Fire(AimingSystem aimingSystem, VoxelWorld world, int currentRound)
    {
        if (!CanFire(currentRound))
        {
            return null;
        }

        // Get base launch velocity; only add upward arc bias for manual aiming
        // (click-to-target already computes a high-arc ballistic solution)
        Vector3 baseVelocity = aimingSystem.GetLaunchVelocity(ProjectileSpeed);
        Vector3 mortarVelocity = baseVelocity;
        if (!aimingSystem.HasTarget)
        {
            float horizontalSpeed = new Vector2(baseVelocity.X, baseVelocity.Z).Length();
            mortarVelocity.Y += horizontalSpeed * ArcBiasY;
        }

        ProjectileBase projectile = CreateProjectile();
        GetTree().CurrentScene.AddChild(projectile);
        projectile.GlobalPosition = GlobalPosition;
        projectile.Initialize(world, OwnerSlot, mortarVelocity, BaseDamage, BlastRadiusMicrovoxels);

        // Puffy smoke trail with launch sparks behind the mortar shell
        TrailFX.CreateMortarTrail(projectile);

        LastFiredRound = currentRound;

        // Mortar-specific FX: upward smoke burst + mild recoil
        SpawnWeaponFireFX(aimingSystem.GetDirection());

        AudioDirector.Instance?.PlaySFX("mortar_fire", GlobalPosition);
        EventBus.Instance?.EmitWeaponFired(new WeaponFiredEvent(OwnerSlot, WeaponId, GlobalPosition, aimingSystem.GetDirection()));
        return projectile;
    }

    protected override void SpawnWeaponFireFX(Vector3 aimDirection)
    {
        WeaponFX.SpawnMortarFireFX(this, GlobalPosition, aimDirection);

        // Gentle recoil: mortar has a short, heavy thump
        if (WeaponMesh != null)
            WeaponFX.AnimateRecoil(WeaponMesh, Vector3.Up, distance: 0.06f, duration: 0.25f);

        EnableIdleSmoke();
    }

    protected override string GetProjectileType()
    {
        return "mortar_shell";
    }
}
