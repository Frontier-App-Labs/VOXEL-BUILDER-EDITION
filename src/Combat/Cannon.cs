namespace VoxelSiege.Combat;

public partial class Cannon : WeaponBase
{
    public Cannon()
    {
        WeaponId = "cannon";
        Cost = 50;
        BaseDamage = 30;
        BlastRadiusMicrovoxels = 4f;
        ProjectileSpeed = 22f;
        CooldownTurns = 0;
    }
}
