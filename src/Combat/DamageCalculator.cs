using Godot;
using VoxelSiege.Voxel;

namespace VoxelSiege.Combat;

public static class DamageCalculator
{
    public static int CalculateExplosionDamage(int baseDamage, float radius, float distance, VoxelMaterialType material)
    {
        if (distance > radius)
        {
            return 0;
        }

        float falloff = 1f - Mathf.Clamp(distance / radius, 0f, 1f);
        VoxelMaterialDefinition definition = VoxelMaterials.GetDefinition(material);
        float materialResistance = Mathf.Clamp(definition.Weight * 0.1f, 0f, 0.75f);
        return Mathf.Max(0, Mathf.RoundToInt(baseDamage * falloff * (1f - materialResistance)));
    }

    public static int CalculateCommanderDamage(int baseDamage, float radius, float distance)
    {
        if (distance > radius)
        {
            return 0;
        }

        return Mathf.RoundToInt(baseDamage * (1f - Mathf.Clamp(distance / radius, 0f, 1f)));
    }
}
