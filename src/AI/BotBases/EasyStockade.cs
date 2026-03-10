using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Easy base: 10x10 rectangular compound with stone foundation,
/// wood walls 4 high, and a flat stone roof. Commander center with
/// stone shell. 3-4 cannon/mortar weapons on the roof edges.
/// </summary>
public static class EasyStockade
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zD = zone.SizeBuildUnits.Z;

        int width = Math.Min(10, zW - 4);
        int depth = Math.Min(10, zD - 4);
        int wallH = 4;

        int offX = (zW - width) / 2;
        int offZ = (zD - depth) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(width - 1, 0, depth - 1);

        // ── Layer 0: stone foundation slab ──
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Stone);

        // ── Layers 1..wallH-1: wood walls (hollow box) ──
        for (int y = 1; y < wallH; y++)
        {
            Vector3I layerS = bS + new Vector3I(0, y, 0);
            Vector3I layerE = bE + new Vector3I(0, y, 0);
            B.AddHollowLayer(plan, t, layerS, layerE, VoxelMaterialType.Wood);
        }

        // ── Roof: solid stone floor at top of walls ──
        int roofY = wallH;
        Vector3I roofS = bS + new Vector3I(0, roofY, 0);
        Vector3I roofE = bE + new Vector3I(0, roofY, 0);
        B.AddFloor(plan, t, roofS, roofE, VoxelMaterialType.Stone);

        // ── Corner columns for visual reinforcement ──
        B.AddCornerColumns(plan, t, bS, bE, 1, wallH - 1, VoxelMaterialType.Stone);

        // ── Window slits: gaps in walls at y=2 on each side (every 3rd block) ──
        // We skip placing windows by removing blocks; instead place stone
        // accent blocks at mid-height to break up the wood walls
        for (int lx = 2; lx < width - 2; lx += 3)
        {
            B.AddSingle(plan, t, bS + new Vector3I(lx, 2, 0), VoxelMaterialType.Stone);
            B.AddSingle(plan, t, bS + new Vector3I(lx, 2, depth - 1), VoxelMaterialType.Stone);
        }
        for (int lz = 2; lz < depth - 2; lz += 3)
        {
            B.AddSingle(plan, t, bS + new Vector3I(0, 2, lz), VoxelMaterialType.Stone);
            B.AddSingle(plan, t, bS + new Vector3I(width - 1, 2, lz), VoxelMaterialType.Stone);
        }

        // ── Commander: center of the interior, on the floor ──
        int cmdX = Math.Clamp(width / 2, 2, width - 3);
        int cmdZ = Math.Clamp(depth / 2, 2, depth - 3);
        Vector3I cmdPos = bS + new Vector3I(cmdX, 1, cmdZ);
        plan.CommanderBuildUnit = cmdPos;

        // Inner stone shell around commander (3x3 hollow box)
        B.AddCommanderShell(plan, t, cmdPos, bS, bE, VoxelMaterialType.Stone, 3);
        B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.Stone);

        // ── Weapons: on outer edges of the roof ──
        B.PlaceRoofEdgeWeapons(plan, t, bS, bE, roofY, cmdPos, 3 + rng.Next(2),
            new[] { "cannon", "mortar", "cannon" }, rng, zone);

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, roofY, zone);

        return plan;
    }
}
