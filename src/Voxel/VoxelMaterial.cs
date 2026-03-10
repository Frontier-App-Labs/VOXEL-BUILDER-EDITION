using Godot;
using System.Collections.Generic;

namespace VoxelSiege.Voxel;

public enum VoxelMaterialType
{
    Air = 0,
    Dirt = 1,
    Wood = 2,
    Stone = 3,
    Brick = 4,
    Concrete = 5,
    Metal = 6,
    ReinforcedSteel = 7,
    Glass = 8,
    Obsidian = 9,
    Sand = 10,
    Ice = 11,
    ArmorPlate = 12,
    Foundation = 13,
    Leaves = 14,
    Bark = 15,
}

public readonly record struct VoxelMaterialDefinition(
    int Cost,
    int MaxHitPoints,
    float Weight,
    bool IsTransparent,
    bool IsFlammable,
    bool UsesGravity,
    bool ExteriorOnly,
    float RicochetChance);

public static class VoxelMaterials
{
    private static readonly Dictionary<VoxelMaterialType, VoxelMaterialDefinition> Definitions = new Dictionary<VoxelMaterialType, VoxelMaterialDefinition>
    {
        [VoxelMaterialType.Air] = new VoxelMaterialDefinition(0, 0, 0f, true, false, false, false, 0f),
        [VoxelMaterialType.Dirt] = new VoxelMaterialDefinition(10, 1, 0.8f, false, false, false, false, 0f),
        [VoxelMaterialType.Wood] = new VoxelMaterialDefinition(15, 3, 0.5f, false, true, false, false, 0f),
        [VoxelMaterialType.Stone] = new VoxelMaterialDefinition(20, 6, 1.2f, false, false, false, false, 0f),
        [VoxelMaterialType.Brick] = new VoxelMaterialDefinition(25, 9, 1.3f, false, false, false, false, 0f),
        [VoxelMaterialType.Concrete] = new VoxelMaterialDefinition(30, 13, 1.8f, false, false, false, false, 0f),
        [VoxelMaterialType.Metal] = new VoxelMaterialDefinition(35, 18, 2.0f, false, false, false, false, 0.12f),
        [VoxelMaterialType.ReinforcedSteel] = new VoxelMaterialDefinition(65, 22, 2.1f, false, false, false, false, 0.1f),
        [VoxelMaterialType.Glass] = new VoxelMaterialDefinition(12, 1, 0.2f, true, false, false, false, 0f),
        [VoxelMaterialType.Obsidian] = new VoxelMaterialDefinition(80, 25, 2.3f, false, false, false, false, 0.05f),
        [VoxelMaterialType.Sand] = new VoxelMaterialDefinition(10, 2, 0.7f, false, false, true, false, 0f),
        [VoxelMaterialType.Ice] = new VoxelMaterialDefinition(12, 3, 0.6f, true, false, false, false, 0f),
        [VoxelMaterialType.ArmorPlate] = new VoxelMaterialDefinition(55, 23, 2.2f, false, false, false, true, 0.08f),
        [VoxelMaterialType.Foundation] = new VoxelMaterialDefinition(0, 120, 999f, false, false, false, false, 0f),
        [VoxelMaterialType.Leaves] = new VoxelMaterialDefinition(10, 1, 0.2f, false, true, false, false, 0f),
        [VoxelMaterialType.Bark] = new VoxelMaterialDefinition(15, 5, 0.8f, false, true, false, false, 0f),
    };

    public static VoxelMaterialDefinition GetDefinition(VoxelMaterialType type)
    {
        return Definitions[type];
    }

    public static bool IsTransparent(VoxelMaterialType type)
    {
        return Definitions[type].IsTransparent;
    }

    public static int NormalizeHitPoints(VoxelMaterialType type, int hitPoints)
    {
        int max = Definitions[type].MaxHitPoints;
        if (max <= 0)
        {
            return 0;
        }

        int clamped = System.Math.Clamp(hitPoints, 0, max);
        if (clamped == 0)
        {
            return 0;
        }

        return System.Math.Clamp((int)System.MathF.Ceiling((clamped / (float)max) * 15f), 1, 15);
    }

    public static int DenormalizeHitPoints(VoxelMaterialType type, int nibble)
    {
        int max = Definitions[type].MaxHitPoints;
        if (max <= 0 || nibble <= 0)
        {
            return 0;
        }

        return System.Math.Clamp((int)System.MathF.Round((nibble / 15f) * max), 1, max);
    }

    public static Color GetPreviewColor(VoxelMaterialType type)
    {
        return type switch
        {
            VoxelMaterialType.Air => new Color(0f, 0f, 0f, 0f),
            VoxelMaterialType.Dirt => new Color("4a8c3f"),
            VoxelMaterialType.Wood => new Color("9b6a3c"),
            VoxelMaterialType.Stone => new Color("7c8797"),
            VoxelMaterialType.Brick => new Color("a45442"),
            VoxelMaterialType.Concrete => new Color("8f9499"),
            VoxelMaterialType.Metal => new Color("7ea0b8"),
            VoxelMaterialType.ReinforcedSteel => new Color("4f5f70"),
            VoxelMaterialType.Glass => new Color(0.65f, 0.85f, 1f, 0.45f),
            VoxelMaterialType.Obsidian => new Color("34284a"),
            VoxelMaterialType.Sand => new Color("cdb36c"),
            VoxelMaterialType.Ice => new Color(0.75f, 0.9f, 1f, 0.6f),
            VoxelMaterialType.ArmorPlate => new Color("58636f"),
            VoxelMaterialType.Foundation => new Color("6b7080"),
            VoxelMaterialType.Leaves => new Color("3a7d2e"),
            VoxelMaterialType.Bark => new Color("5c3a1e"),
            _ => Colors.White,
        };
    }
}
