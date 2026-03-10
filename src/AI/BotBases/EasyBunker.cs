using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Easy base: 12x12 wide, only 3 tall. Low profile bunker with thick
/// stone foundation, dirt walls, sand roof, and stone buttresses on
/// the corners. Mortar-heavy loadout (area denial from low profile).
/// Commander center with stone protection.
/// </summary>
public static class EasyBunker
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zD = zone.SizeBuildUnits.Z;

        int width = Math.Min(12, zW - 4);
        int depth = Math.Min(12, zD - 4);
        int wallH = 3;

        int offX = (zW - width) / 2;
        int offZ = (zD - depth) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(width - 1, 0, depth - 1);

        // ── Stone foundation ──
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Stone);

        // ── Dirt walls ──
        for (int y = 1; y < wallH; y++)
        {
            Vector3I layerS = bS + new Vector3I(0, y, 0);
            Vector3I layerE = bE + new Vector3I(0, y, 0);
            B.AddHollowLayer(plan, t, layerS, layerE, VoxelMaterialType.Dirt);
        }

        // ── Sand roof ──
        int roofY = wallH;
        Vector3I roofS = bS + new Vector3I(0, roofY, 0);
        Vector3I roofE = bE + new Vector3I(0, roofY, 0);
        B.AddFloor(plan, t, roofS, roofE, VoxelMaterialType.Sand);

        // ── Stone buttresses at corners (2-high pillars on roof) ──
        B.AddSingle(plan, t, bS + new Vector3I(0, roofY + 1, 0), VoxelMaterialType.Stone);
        B.AddSingle(plan, t, bS + new Vector3I(width - 1, roofY + 1, 0), VoxelMaterialType.Stone);
        B.AddSingle(plan, t, bS + new Vector3I(0, roofY + 1, depth - 1), VoxelMaterialType.Stone);
        B.AddSingle(plan, t, bS + new Vector3I(width - 1, roofY + 1, depth - 1), VoxelMaterialType.Stone);

        // ── Stone accent band at base of walls ──
        for (int lx = 0; lx < width; lx++)
        {
            B.AddSingle(plan, t, bS + new Vector3I(lx, 1, 0), VoxelMaterialType.Stone);
            B.AddSingle(plan, t, bS + new Vector3I(lx, 1, depth - 1), VoxelMaterialType.Stone);
        }
        for (int lz = 1; lz < depth - 1; lz++)
        {
            B.AddSingle(plan, t, bS + new Vector3I(0, 1, lz), VoxelMaterialType.Stone);
            B.AddSingle(plan, t, bS + new Vector3I(width - 1, 1, lz), VoxelMaterialType.Stone);
        }

        // ── Commander center ──
        int cmdBX = Math.Clamp(width / 2, 2, width - 3);
        int cmdBZ = Math.Clamp(depth / 2, 2, depth - 3);
        Vector3I cmdPos = bS + new Vector3I(cmdBX, 1, cmdBZ);
        plan.CommanderBuildUnit = cmdPos;
        B.AddCommanderShell(plan, t, cmdPos, bS, bE, VoxelMaterialType.Stone, 3);
        B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.Stone);

        // ── Weapons on roof edges (mortar-heavy for area denial) ──
        B.PlaceRoofEdgeWeapons(plan, t, bS, bE, roofY, cmdPos, 3 + rng.Next(2),
            new[] { "mortar", "cannon", "mortar" }, rng, zone);

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, roofY, zone);

        return plan;
    }
}
