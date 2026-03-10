using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Easy base: 14x8 rectangular fort with wood walls 5 tall, stone
/// corner pillars, and bark crenellations. Commander in the back corner
/// with wood shell. Cannon-only loadout (cheap wooden fort, lots of guns).
/// </summary>
public static class EasyPalisade
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zD = zone.SizeBuildUnits.Z;

        int width = Math.Min(14, zW - 4);
        int depth = Math.Min(8, zD - 4);
        int wallH = 5;

        int offX = (zW - width) / 2;
        int offZ = (zD - depth) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(width - 1, 0, depth - 1);

        // ── Wood foundation ──
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Wood);

        // ── Wood walls (hollow) ──
        for (int y = 1; y < wallH; y++)
        {
            Vector3I layerS = bS + new Vector3I(0, y, 0);
            Vector3I layerE = bE + new Vector3I(0, y, 0);
            B.AddHollowLayer(plan, t, layerS, layerE, VoxelMaterialType.Wood);
        }

        // ── Stone corner pillars (full height) ──
        B.AddCornerColumns(plan, t, bS, bE, 0, wallH - 1, VoxelMaterialType.Stone);

        // ── Flat wood roof ──
        int roofY = wallH;
        Vector3I roofS = bS + new Vector3I(0, roofY, 0);
        Vector3I roofE = bE + new Vector3I(0, roofY, 0);
        B.AddFloor(plan, t, roofS, roofE, VoxelMaterialType.Wood);

        // ── Bark crenellations on top ──
        B.AddCrenellations(plan, t, bS, bE, roofY + 1, VoxelMaterialType.Bark);

        // ── Commander in back corner with wood shell ──
        // Place commander toward the back of the fort (away from outward edges)
        var (useMinX, useMaxX, useMinZ, useMaxZ) = B.GetOutwardEdges(zone);
        int cmdX, cmdZ;
        // Put commander on the side opposite the outward-facing edge
        if (useMinZ)
            cmdZ = Math.Clamp(depth - 3, 2, depth - 3);
        else
            cmdZ = Math.Clamp(2, 2, depth - 3);

        if (useMinX)
            cmdX = Math.Clamp(width - 3, 2, width - 3);
        else
            cmdX = Math.Clamp(2, 2, width - 3);

        Vector3I cmdPos = bS + new Vector3I(cmdX, 1, cmdZ);
        plan.CommanderBuildUnit = cmdPos;
        B.AddCommanderShell(plan, t, cmdPos, bS, bE, VoxelMaterialType.Wood, 3);
        B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.Stone);

        // ── Weapons on outward-facing roof edges (all cannons - cheap volume) ──
        B.PlaceRoofEdgeWeapons(plan, t, bS, bE, roofY, cmdPos, 3 + rng.Next(2),
            new[] { "cannon", "cannon", "cannon" }, rng, zone);

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, roofY, zone);

        return plan;
    }
}
