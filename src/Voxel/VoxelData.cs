namespace VoxelSiege.Voxel;

/// <summary>
/// Compact runtime voxel representation packed into 16 bits.
/// </summary>
public readonly record struct Voxel(ushort Data)
{
    public VoxelMaterialType Material => (VoxelMaterialType)(Data & 0xFF);
    public int HealthNibble => (Data >> 8) & 0xF;
    public bool IsWeaponMount => ((Data >> 12) & 0x1) != 0;
    public bool IsCommanderSpawn => ((Data >> 13) & 0x1) != 0;
    public bool IsDamaged => ((Data >> 14) & 0x1) != 0;
    public bool ReservedFlag => ((Data >> 15) & 0x1) != 0;
    public bool IsAir => Material == VoxelMaterialType.Air || Data == 0;
    public bool IsSolid => !IsAir;
    public int HitPoints => VoxelMaterials.DenormalizeHitPoints(Material, HealthNibble);

    public static Voxel Air => new Voxel(0);

    public static Voxel Create(
        VoxelMaterialType material,
        int? hitPoints = null,
        bool isWeaponMount = false,
        bool isCommanderSpawn = false,
        bool isDamaged = false,
        bool reservedFlag = false)
    {
        if (material == VoxelMaterialType.Air)
        {
            return Air;
        }

        int resolvedHitPoints = hitPoints ?? VoxelMaterials.GetDefinition(material).MaxHitPoints;
        int healthNibble = VoxelMaterials.NormalizeHitPoints(material, resolvedHitPoints);
        ushort data = (ushort)material;
        data |= (ushort)(healthNibble << 8);
        if (isWeaponMount)
        {
            data |= 1 << 12;
        }

        if (isCommanderSpawn)
        {
            data |= 1 << 13;
        }

        if (isDamaged)
        {
            data |= 1 << 14;
        }

        if (reservedFlag)
        {
            data |= 1 << 15;
        }

        return new Voxel(data);
    }

    public Voxel WithHitPoints(int hitPoints)
    {
        if (IsAir)
        {
            return this;
        }

        int nibble = VoxelMaterials.NormalizeHitPoints(Material, hitPoints);
        ushort cleared = (ushort)(Data & ~(0xF << 8));
        return new Voxel((ushort)(cleared | (nibble << 8)));
    }

    public Voxel WithDamaged(bool value)
    {
        ushort mask = (ushort)(1 << 14);
        ushort data = value ? (ushort)(Data | mask) : (ushort)(Data & ~mask);
        return new Voxel(data);
    }

    public Voxel WithWeaponMount(bool value)
    {
        ushort mask = (ushort)(1 << 12);
        ushort data = value ? (ushort)(Data | mask) : (ushort)(Data & ~mask);
        return new Voxel(data);
    }

    public Voxel WithCommanderSpawn(bool value)
    {
        ushort mask = (ushort)(1 << 13);
        ushort data = value ? (ushort)(Data | mask) : (ushort)(Data & ~mask);
        return new Voxel(data);
    }
}
