using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Core;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Hard base: 16x16 complex with maze-like interior walls (3 random dividing
/// walls of varying materials). Thick concrete outer walls (2 blocks thick) with
/// ArmorPlate on the front face. Metal roof. Corner watchtowers (2 blocks above
/// roof) with elevated weapons. Commander hidden in a random quadrant with
/// obsidian vault + decoy rooms. 6-8 weapons at varied heights across the roof
/// and watchtowers. All 5 weapon types represented.
/// </summary>
public static class HardLabyrinth
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int mainW = Math.Min(16, zW - 4);
        int mainD = Math.Min(16, zD - 4);
        int wallH = Math.Min(5, zH - 6);

        int offX = (zW - mainW) / 2;
        int offZ = (zD - mainD) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(mainW - 1, 0, mainD - 1);

        // ── Concrete foundation ──
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Concrete);

        // ── Thick outer walls (2 layers of concrete) ──
        for (int y = 1; y <= wallH; y++)
        {
            // Outer ring
            B.AddHollowLayer(plan, t,
                bS + new Vector3I(0, y, 0), bE + new Vector3I(0, y, 0),
                VoxelMaterialType.Concrete);

            // Inner ring (1 block inset)
            if (mainW >= 6 && mainD >= 6)
            {
                B.AddHollowLayer(plan, t,
                    bS + new Vector3I(1, y, 1), bE - new Vector3I(1, -y, 1),
                    VoxelMaterialType.Concrete);
            }
        }

        // ── Metal roof ──
        int roofY = wallH + 1;
        B.AddFloor(plan, t,
            bS + new Vector3I(0, roofY, 0), bE + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Metal);

        // ── 3 maze interior walls (varying materials for visual variety) ──
        VoxelMaterialType[] wallMats = {
            VoxelMaterialType.ReinforcedSteel,
            VoxelMaterialType.Metal,
            VoxelMaterialType.Concrete,
        };
        for (int i = 0; i < 3; i++)
        {
            B.AddInteriorWall(plan, t, bS, bE, mainW, wallH, mainD,
                wallMats[i % wallMats.Length], rng);
        }

        // ── ArmorPlate on front face ──
        var (_, _, apUseMinZ, _) = B.GetOutwardEdges(zone);
        int frontZ = apUseMinZ ? bS.Z : bE.Z;
        for (int y = 1; y <= wallH; y++)
        {
            for (int x = bS.X; x <= bE.X; x++)
            {
                B.AddSingle(plan, t, new Vector3I(x, y, frontZ), VoxelMaterialType.ArmorPlate);
            }
        }

        // ── Corner columns (reinforced steel) ──
        B.AddCornerColumns(plan, t, bS, bE, 1, wallH, VoxelMaterialType.ReinforcedSteel);

        // ── Corner watchtowers (2 blocks above roof for elevated weapon positions) ──
        int towerTopY = roofY + 2;
        Vector3I[] towerCorners =
        {
            bS + new Vector3I(1, 0, 1),
            new Vector3I(bE.X - 1, bS.Y, bS.Z + 1),
            new Vector3I(bS.X + 1, bS.Y, bE.Z - 1),
            new Vector3I(bE.X - 1, bS.Y, bE.Z - 1),
        };
        foreach (Vector3I tc in towerCorners)
        {
            for (int y = roofY + 1; y <= towerTopY; y++)
            {
                B.AddSingle(plan, t, new Vector3I(tc.X, y, tc.Z), VoxelMaterialType.ReinforcedSteel);
            }
        }

        // ── Commander vault in a random quadrant ──
        int quadrant = rng.Next(4);
        int cmdOX, cmdOZ;
        switch (quadrant)
        {
            case 0: cmdOX = mainW / 4; cmdOZ = mainD / 4; break;
            case 1: cmdOX = mainW * 3 / 4; cmdOZ = mainD / 4; break;
            case 2: cmdOX = mainW / 4; cmdOZ = mainD * 3 / 4; break;
            default: cmdOX = mainW * 3 / 4; cmdOZ = mainD * 3 / 4; break;
        }
        cmdOX = Math.Clamp(cmdOX, 4, mainW - 5);
        cmdOZ = Math.Clamp(cmdOZ, 4, mainD - 5);
        Vector3I cmdPos = bS + new Vector3I(cmdOX, 1, cmdOZ);
        plan.CommanderBuildUnit = cmdPos;
        B.AddObsidianVault(plan, t, cmdPos, bS, bE);
        B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.Metal);

        // ── Decoy rooms in opposite quadrants ──
        B.AddDecoyRooms(plan, t, bS, bE, mainW, mainD, quadrant, VoxelMaterialType.Obsidian, rng);

        // ── Elevated weapons on watchtower corners (railgun + missile for height advantage) ──
        var (wtUseMinX, wtUseMaxX, wtUseMinZ2, wtUseMaxZ2) = B.GetOutwardEdges(zone);
        string[] elevatedWeapons = { "railgun", "missile" };
        int elevPlaced = 0;
        foreach (Vector3I tc in towerCorners)
        {
            if (elevPlaced >= 2) break;
            Vector3I wPos = new Vector3I(tc.X, towerTopY + 4, tc.Z);
            if (wPos.DistanceTo(cmdPos) < GameConfig.MinWeaponCommanderGap) continue;

            // Only place on outward-facing corners
            bool isOutward = false;
            if (wtUseMinZ2 && tc.Z <= bS.Z + 2) isOutward = true;
            if (wtUseMaxZ2 && tc.Z >= bE.Z - 2) isOutward = true;
            if (wtUseMinX && tc.X <= bS.X + 2) isOutward = true;
            if (wtUseMaxX && tc.X >= bE.X - 2) isOutward = true;
            if (!isOutward) continue;

            string wId = elevatedWeapons[elevPlaced % elevatedWeapons.Length];
            int cost = B.GetWeaponCost(wId);
            bool guaranteed = plan.WeaponPlacements.Count < 3;
            if (guaranteed || t.TrySpend(cost))
            {
                if (guaranteed) t.TrySpend(cost);
                // Build pillar under weapon
                for (int py = 1; py <= 3; py++)
                {
                    plan.Actions.Add(new PlannedBuildAction
                    {
                        ToolMode = BuildToolMode.Single,
                        Material = VoxelMaterialType.ReinforcedSteel,
                        Start = new Vector3I(tc.X, towerTopY + py, tc.Z),
                        End = new Vector3I(tc.X, towerTopY + py, tc.Z),
                        Hollow = false,
                    });
                    t.TrySpend(VoxelMaterials.GetDefinition(VoxelMaterialType.ReinforcedSteel).Cost);
                }
                plan.WeaponPlacements.Add((wPos, wId));
                elevPlaced++;
            }
        }

        // ── Remaining weapons on roof (varied types, no duplicates) ──
        int totalWeapons = 6 + rng.Next(3);
        int roofRemaining = totalWeapons - plan.WeaponPlacements.Count;
        string[] roofWeapons = { "mortar", "drill", "cannon", "missile" };
        if (roofRemaining > 0)
        {
            B.PlaceRoofEdgeWeapons(plan, t, bS, bE, roofY, cmdPos, roofRemaining, roofWeapons, rng, zone);
        }

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, roofY, zone);

        return plan;
    }
}
