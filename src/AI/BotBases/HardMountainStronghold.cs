using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Core;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Hard base: 3-tier stepped pyramid. Tier 1: 18x18 stone (3 tall) with armor
/// plate front. Tier 2: 12x12 concrete (3 tall), inset 3 from tier 1. Tier 3:
/// 6x6 metal (3 tall), inset 3 from tier 2, reinforced steel roof. Commander
/// in tier 1 obsidian vault with decoys. Weapons across all tiers. Crenellations
/// on each tier edge.
/// </summary>
public static class HardMountainStronghold
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int baseW = Math.Min(18, zW - 4);
        int baseD = Math.Min(18, zD - 4);
        int tierH = 3;

        int offX = (zW - baseW) / 2;
        int offZ = (zD - baseD) / 2;

        Vector3I t1S = o + new Vector3I(offX, 0, offZ);
        Vector3I t1E = t1S + new Vector3I(baseW - 1, 0, baseD - 1);

        // ════ TIER 1: Base (stone + armor plate front) ════
        B.AddFloor(plan, t, t1S, t1E, VoxelMaterialType.Stone);

        for (int y = 1; y <= tierH; y++)
        {
            B.AddHollowLayer(plan, t,
                t1S + new Vector3I(0, y, 0), t1E + new Vector3I(0, y, 0),
                VoxelMaterialType.Stone);
        }

        // Armor plate on front face
        var (_, _, apUseMinZ, _) = B.GetOutwardEdges(zone);
        int frontZ = apUseMinZ ? t1S.Z : t1E.Z;
        for (int y = 1; y <= tierH; y++)
        {
            for (int x = t1S.X; x <= t1E.X; x++)
            {
                B.AddSingle(plan, t, new Vector3I(x, y, frontZ), VoxelMaterialType.ArmorPlate);
            }
        }

        // Tier 1 roof
        int t1RoofY = tierH + 1;
        B.AddFloor(plan, t,
            t1S + new Vector3I(0, t1RoofY, 0), t1E + new Vector3I(0, t1RoofY, 0),
            VoxelMaterialType.Stone);

        // Crenellations on tier 1
        B.AddCrenellations(plan, t, t1S, t1E, t1RoofY + 1, VoxelMaterialType.Stone);

        // ════ TIER 2: Mid (concrete, inset by 3) ════
        int midW = baseW - 6;
        int midD = baseD - 6;
        bool hasTier2 = midW >= 6 && midD >= 6 && 2 * tierH + 3 < zH;

        Vector3I t2S = t1S + new Vector3I(3, t1RoofY, 3);
        Vector3I t2E = t2S + new Vector3I(midW - 1, 0, midD - 1);

        if (hasTier2)
        {
            for (int y = 1; y <= tierH; y++)
            {
                B.AddHollowLayer(plan, t,
                    t2S + new Vector3I(0, y, 0), t2E + new Vector3I(0, y, 0),
                    VoxelMaterialType.Concrete);
            }

            B.AddFloor(plan, t,
                t2S + new Vector3I(0, tierH + 1, 0), t2E + new Vector3I(0, tierH + 1, 0),
                VoxelMaterialType.Concrete);

            B.AddCrenellations(plan, t, t2S, t2E, tierH + 2, VoxelMaterialType.Concrete);

            // ════ TIER 3: Top (metal, inset by another 3) ════
            int topW = midW - 6;
            int topD = midD - 6;
            bool hasTier3 = topW >= 4 && topD >= 4 && 3 * tierH + 4 < zH;

            Vector3I t3S = t2S + new Vector3I(3, tierH + 1, 3);
            Vector3I t3E = t3S + new Vector3I(topW - 1, 0, topD - 1);

            if (hasTier3)
            {
                for (int y = 1; y <= tierH; y++)
                {
                    B.AddHollowLayer(plan, t,
                        t3S + new Vector3I(0, y, 0), t3E + new Vector3I(0, y, 0),
                        VoxelMaterialType.Metal);
                }

                // Reinforced steel roof
                B.AddFloor(plan, t,
                    t3S + new Vector3I(0, tierH + 1, 0), t3E + new Vector3I(0, tierH + 1, 0),
                    VoxelMaterialType.ReinforcedSteel);

                B.AddCrenellations(plan, t, t3S, t3E, tierH + 2, VoxelMaterialType.Metal);
            }

            // ── Commander in base tier (deepest protection) ──
            int quadrant = rng.Next(4);
            int cmdOX, cmdOZ;
            switch (quadrant)
            {
                case 0: cmdOX = baseW / 4; cmdOZ = baseD / 4; break;
                case 1: cmdOX = baseW * 3 / 4; cmdOZ = baseD / 4; break;
                case 2: cmdOX = baseW / 4; cmdOZ = baseD * 3 / 4; break;
                default: cmdOX = baseW * 3 / 4; cmdOZ = baseD * 3 / 4; break;
            }
            cmdOX = Math.Clamp(cmdOX, 4, baseW - 5);
            cmdOZ = Math.Clamp(cmdOZ, 4, baseD - 5);
            Vector3I cmdPos = t1S + new Vector3I(cmdOX, 1, cmdOZ);
            plan.CommanderBuildUnit = cmdPos;
            B.AddObsidianVault(plan, t, cmdPos, t1S, t1E);
            B.EnsureCommanderCeiling(plan, t, cmdPos, t1S, t1E, VoxelMaterialType.Stone);
            B.AddDecoyRooms(plan, t, t1S, t1E, baseW, baseD, quadrant, VoxelMaterialType.Obsidian, rng);

            // ── Weapons across all tiers ──
            int weaponCount = 5 + rng.Next(3);
            string[] weapons = { "cannon", "mortar", "railgun", "missile", "drill" };
            List<Vector3I> wpPlaced = new List<Vector3I>();

            // Tier 1 terrace weapons (outer corners)
            B.PlaceTerraceCornerWeapons(plan, t, t1S, t1E, t1RoofY, cmdPos,
                2, weapons, wpPlaced, rng, zone);

            // Tier 2 terrace weapons
            B.PlaceTerraceCornerWeapons(plan, t, t2S, t2E, tierH + 1, cmdPos,
                2, weapons, wpPlaced, rng, zone);

            // Tier 3 top weapon
            if (hasTier3)
            {
                int t3RoofAbsY = t3S.Y + tierH + 1;
                int t3WeaponX = t3S.X + Math.Max(topW / 2, 1);
                int t3WeaponZ = Math.Min(t3S.Z + 1, t3E.Z);
                Vector3I topWPos = new Vector3I(t3WeaponX, t3RoofAbsY + 4, t3WeaponZ);

                if (topWPos.DistanceTo(cmdPos) >= GameConfig.MinWeaponCommanderGap
                    && wpPlaced.Count < weaponCount)
                {
                    string wId = "railgun";
                    int cost = B.GetWeaponCost(wId);
                    bool guaranteed = wpPlaced.Count < 3;

                    if (guaranteed || t.TrySpend(cost))
                    {
                        if (guaranteed) t.TrySpend(cost);

                        // Build 3-block reinforced steel pillar
                        for (int py = 1; py <= 3; py++)
                        {
                            plan.Actions.Add(new PlannedBuildAction
                            {
                                ToolMode = BuildToolMode.Single,
                                Material = VoxelMaterialType.ReinforcedSteel,
                                Start = new Vector3I(topWPos.X, t3RoofAbsY + py, topWPos.Z),
                                End = new Vector3I(topWPos.X, t3RoofAbsY + py, topWPos.Z),
                                Hollow = false,
                            });
                            t.TrySpend(VoxelMaterials.GetDefinition(VoxelMaterialType.ReinforcedSteel).Cost);
                        }
                        plan.WeaponPlacements.Add((topWPos, wId));
                        wpPlaced.Add(topWPos);
                    }
                }
            }

            // Fill remaining weapon slots on tier 1 terrace
            int remainingWeapons = weaponCount - wpPlaced.Count;
            if (remainingWeapons > 0)
            {
                B.PlaceRoofEdgeWeapons(plan, t, t1S, t1E, t1RoofY, cmdPos,
                    remainingWeapons, weapons, rng, zone);
            }

            B.EnsureAtLeastOneWeapon(plan, t, t1S, t1E, t1RoofY, zone);
            return plan;
        }

        // ── Fallback: only base tier fits ──
        int fbX = Math.Clamp(baseW / 2, 2, baseW - 3);
        int fbZ = Math.Clamp(baseD / 2, 2, baseD - 3);
        Vector3I cmdFallback = t1S + new Vector3I(fbX, 1, fbZ);
        plan.CommanderBuildUnit = cmdFallback;
        B.AddCommanderShell(plan, t, cmdFallback, t1S, t1E, VoxelMaterialType.Concrete, 3);
        B.EnsureCommanderCeiling(plan, t, cmdFallback, t1S, t1E, VoxelMaterialType.Concrete);
        B.PlaceRoofEdgeWeapons(plan, t, t1S, t1E, t1RoofY, cmdFallback,
            5 + rng.Next(3), new[] { "cannon", "mortar", "railgun", "missile", "drill" }, rng, zone);
        B.EnsureAtLeastOneWeapon(plan, t, t1S, t1E, t1RoofY, zone);
        return plan;
    }
}
