using Godot;
using VoxelSiege.Voxel;

namespace VoxelSiege.Combat;

/// <summary>
/// Pure-function damage calculations for explosions, railgun penetration,
/// and commander proximity damage. Accounts for material resistance,
/// ricochet chance, and sand blast absorption.
///
/// Design goal: explosions should blow satisfying craters in structures.
/// Soft materials (Dirt, Wood, Glass, Sand, Ice, Leaves, Bark) should be
/// destroyed in one hit at close range. Medium materials (Stone, Brick)
/// should be destroyed at the blast center and heavily damaged at the edges.
/// Hard materials (Concrete, Metal, ReinforcedSteel, ArmorPlate, Obsidian)
/// should take visible damage but require 2-3 direct hits to destroy,
/// giving players a reason to build with expensive materials.
/// </summary>
public static class DamageCalculator
{
    /// <summary>
    /// Sand reduces the effective blast radius by this fraction.
    /// </summary>
    private const float SandBlastReduction = 0.40f;

    /// <summary>
    /// Damage reduction per penetration depth level for the railgun (multiplicative).
    /// At depth 1: full damage. At depth 2: 70%. At depth 3: 49%.
    /// </summary>
    private const float RailgunPenetrationFalloff = 0.7f;

    /// <summary>
    /// Calculates explosion damage to a voxel based on smooth falloff from
    /// the blast center and material resistance.
    /// Damage is compared directly to material MaxHitPoints so that the
    /// base-damage stat of a weapon represents its destructive *capability*
    /// rather than a raw subtraction. A base-damage of 100 at the center
    /// of the blast (distance 0) will one-shot any material with
    /// MaxHP &lt;= 100 after resistance.
    ///
    /// Falloff uses a linear curve from the center of the blast to
    /// the edges.
    /// </summary>
    public static int CalculateExplosionDamage(int baseDamage, float radius, float distance, VoxelMaterialType material)
    {
        if (distance > radius)
        {
            return 0;
        }

        float effectiveRadius = radius;

        // Sand absorbs blast energy, reducing the effective radius slightly
        if (material == VoxelMaterialType.Sand)
        {
            effectiveRadius *= (1f - SandBlastReduction);
            if (distance > effectiveRadius)
            {
                return 0;
            }
        }

        // Linear falloff: full damage at center, drops off toward edges.
        float t = Mathf.Clamp(distance / effectiveRadius, 0f, 1f);
        float falloff = 1f - t;

        VoxelMaterialDefinition definition = VoxelMaterials.GetDefinition(material);

        // Material resistance: capped at 75% for the heaviest materials.
        // Weight 2.8 (Obsidian) -> resistance 0.28; Weight 0.5 (Wood) -> 0.05
        float materialResistance = Mathf.Clamp(definition.Weight * 0.1f, 0f, 0.75f);
        float damage = baseDamage * falloff * (1f - materialResistance);

        // Ricochet: armored materials deflect a portion of
        // explosion damage. This is on top of material resistance.
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

        // Material resistance: same capped formula as explosions (max 75%)
        float materialResistance = Mathf.Clamp(definition.Weight * 0.1f, 0f, 0.75f);
        damage *= (1f - materialResistance);

        // Ricochet chance for railgun
        if (definition.RicochetChance > 0f)
        {
            damage *= (1f - definition.RicochetChance);
        }

        return Mathf.Max(1, Mathf.RoundToInt(damage));
    }
}
