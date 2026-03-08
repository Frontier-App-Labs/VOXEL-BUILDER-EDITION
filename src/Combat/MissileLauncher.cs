namespace VoxelSiege.Combat;

public partial class MissileLauncher : WeaponBase
{
    public MissileLauncher()
    {
        WeaponId = "missile_launcher";
        Cost = 100;
        BaseDamage = 40;
        BlastRadiusMicrovoxels = 8f;
        ProjectileSpeed = 16f;
        CooldownTurns = 0;
    }
}
