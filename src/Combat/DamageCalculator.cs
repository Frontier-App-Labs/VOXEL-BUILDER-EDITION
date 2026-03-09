using Godot;
using VoxelSiege.Voxel;

namespace VoxelSiege.Combat;

/// <summary>
/// Pure-function damage calculations for explosions, railgun penetration,
/// and commander proximity damage. Accounts for material resistance,
/// ricochet chance, and sand blast absorption.
/// </summary>
public static class DamageCalculator
{
    /// <summary>
    /// Sand reduces the effective blast radius by this fraction.
    /// </summary>
    private const float SandBlastReduction = 0.4f;

    /// <summary>
    /// Damage reduction per penetration depth level for the railgun (multiplicative).
    /// At depth 1: full damage. At depth 2: 70%. At depth 3: 49%.
    /// </summary>
    private const float RailgunPenetrationFalloff = 0.7f;

    /// <summary>
    /// Calculates explosion damage to a voxel based on linear falloff from
    /// the blast center and material resistance.
    /// Sand blocks absorb blast energy (reduce effective radius).
    /// Materials with RicochetChance > 0 can deflect some damage.
    /// </summary>
    public static int CalculateExplosionDamage(int baseDamage, float radius, float distance, VoxelMaterialType material)
    {
        if (distance > radius)
        {
            return 0;
        }

        float effectiveRadius = radius;

        // Sand absorbs blast energy, reducing the effective radius
        if (material == VoxelMaterialType.Sand)
        {
            effectiveRadius *= (1f - SandBlastReduction);
            if (distance > effectiveRadius)
            {
                return 0;
            }
        }

        float falloff = 1f - Mathf.Clamp(distance / effectiveRadius, 0f, 1f);

        VoxelMaterialDefinition definition = VoxelMaterials.GetDefinition(material);

        // Material resistance: heavier materials resist more damage
        float materialResistance = Mathf.Clamp(definition.Weight * 0.1f, 0f, 0.75f);
        float damage = baseDamage * falloff * (1f - materialResistance);

        // Ricochet chance: materials like Metal, ReinforcedSteel, ArmorPlate, Obsidian
        // can deflect a portion of explosion damage
        if (definition.RicochetChance > 0f)
        {
            damage *= (1f - definition.RicochetChance);
        }

        return Mathf.Max(0, Mathf.RoundToInt(damage));
    }

    /// <summary>
    /// Damage to a commander based on proximity to the blast center.
    /// Commanders have no material resistance -- just linear falloff.
    /// </summary>
    public static int CalculateCommanderDamage(int baseDamage, float radius, float distance)
    {
        if (distance > radius)
        {
            return 0;
        }

        return Mathf.RoundToInt(baseDamage * (1f - Mathf.Clamp(distance / radius, 0f, 1f)));
    }

    /// <summary>
    /// Damage for railgun hits with penetration depth reduction.
    /// Each layer of penetration reduces damage multiplicatively.
    /// Materials with ricochet chance can deflect some of the remaining damage.
    /// </summary>
    /// <param name="baseDamage">The railgun's base damage stat.</param>
    /// <param name="penetrationDepth">How many solid blocks deep this hit is (1 = surface).</param>
    /// <param name="material">The material type of the voxel being damaged.</param>
    public static int CalculateRailgunDamage(int baseDamage, int penetrationDepth, VoxelMaterialType material)
    {
        VoxelMaterialDefinition definition = VoxelMaterials.GetDefinition(material);

        // Penetration falloff: each layer reduces damage
        float depthMultiplier = Mathf.Pow(RailgunPenetrationFalloff, penetrationDepth - 1);
        float damage = baseDamage * depthMultiplier;

        // Material resistance
        float materialResistance = Mathf.Clamp(definition.Weight * 0.1f, 0f, 0.75f);
        damage *= (1f - materialResistance);

        // Ricochet chance can deflect damage from hard materials
        if (definition.RicochetChance > 0f)
        {
            damage *= (1f - definition.RicochetChance);
        }

        return Mathf.Max(1, Mathf.RoundToInt(damage));
    }
}
