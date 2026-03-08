namespace VoxelSiege.Combat;

public partial class Mortar : WeaponBase
{
    public Mortar()
    {
        WeaponId = "mortar";
        Cost = 60;
        BaseDamage = 25;
        BlastRadiusMicrovoxels = 6f;
        ProjectileSpeed = 18f;
        CooldownTurns = 0;
    }
}
