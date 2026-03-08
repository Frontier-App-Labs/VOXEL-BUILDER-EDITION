using Godot;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;

namespace VoxelSiege.Combat;

public partial class Railgun : WeaponBase
{
    public Railgun()
    {
        WeaponId = "railgun";
        Cost = 120;
        BaseDamage = 50;
        BlastRadiusMicrovoxels = 0f;
        ProjectileSpeed = 120f;
        CooldownTurns = 0;
    }

    public override ProjectileBase? Fire(AimingSystem aimingSystem, VoxelWorld world, int currentRound)
    {
        if (!CanFire(currentRound))
        {
            return null;
        }

        Vector3 start = GlobalPosition;
        Vector3 direction = aimingSystem.GetDirection();
        for (int step = 1; step <= 96; step++)
        {
            Vector3 point = start + (direction * step * GameConfig.MicrovoxelMeters);
            Vector3I micro = MathHelpers.WorldToMicrovoxel(point);
            Voxel.Voxel voxel = world.GetVoxel(micro);
            if (!voxel.IsAir)
            {
                int nextHp = voxel.HitPoints - BaseDamage;
                world.SetVoxel(micro, nextHp <= 0 ? Voxel.Voxel.Air : voxel.WithHitPoints(nextHp).WithDamaged(true), OwnerSlot);
            }
        }

        LastFiredRound = currentRound;
        EventBus.Instance?.EmitWeaponFired(new WeaponFiredEvent(OwnerSlot, WeaponId, GlobalPosition, direction));
        return null;
    }
}
