using Godot;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Combat;

public partial class Drill : WeaponBase
{
    public Drill()
    {
        WeaponId = "drill";
        Cost = 150;
        BaseDamage = 60;
        BlastRadiusMicrovoxels = 2f;
        ProjectileSpeed = 20f;
        CooldownTurns = 0;
    }

    public override ProjectileBase? Fire(AimingSystem aimingSystem, VoxelWorld world, int currentRound)
    {
        if (!CanFire(currentRound))
        {
            return null;
        }

        Vector3 direction = aimingSystem.GetDirection();
        Vector3 point = GlobalPosition;
        for (int step = 0; step < 24; step++)
        {
            point += direction * GameConfig.MicrovoxelMeters;
            Vector3I micro = MathHelpers.WorldToMicrovoxel(point);
            VoxelValue voxel = world.GetVoxel(micro);
            if (!voxel.IsAir && voxel.Material != VoxelMaterialType.Foundation)
            {
                world.SetVoxel(micro, VoxelValue.Air, OwnerSlot);
            }
        }

        LastFiredRound = currentRound;
        EventBus.Instance?.EmitWeaponFired(new WeaponFiredEvent(OwnerSlot, WeaponId, GlobalPosition, direction));
        return null;
    }
}
