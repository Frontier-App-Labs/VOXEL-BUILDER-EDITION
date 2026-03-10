using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Easy base: Diamond/rotated shape built within an 8x8 footprint.
/// The diamond is created by filling only blocks where Manhattan
/// distance from center is within radius. Stone walls 4 tall, brick
/// floor with concrete accent pillars at the diamond tips.
/// Mortar-heavy loadout for area denial. Commander center.
/// </summary>
public static class EasyRedoubt
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zD = zone.SizeBuildUnits.Z;

        int side = Math.Min(8, Math.Min(zW - 4, zD - 4));
        int wallH = 4;
        int radius = side / 2;  // diamond radius from center

        int offX = (zW - side) / 2;
        int offZ = (zD - side) / 2;

        // bS/bE define the bounding box for the diamond
        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(side - 1, 0, side - 1);

        int cx = side / 2;  // local center
        int cz = side / 2;

        // ── Brick floor (diamond shape) ──
        for (int lx = 0; lx < side; lx++)
        {
            for (int lz = 0; lz < side; lz++)
            {
                if (Math.Abs(lx - cx) + Math.Abs(lz - cz) <= radius)
                {
                    B.AddSingle(plan, t,
                        bS + new Vector3I(lx, 0, lz),
                        VoxelMaterialType.Brick);
                }
            }
        }

        // ── Stone walls (diamond perimeter ring, layers 1..wallH-1) ──
        for (int y = 1; y < wallH; y++)
        {
            for (int lx = 0; lx < side; lx++)
            {
                for (int lz = 0; lz < side; lz++)
                {
                    int dist = Math.Abs(lx - cx) + Math.Abs(lz - cz);
                    if (dist <= radius)
                    {
                        // Only place blocks on the perimeter (edge of diamond)
                        bool isEdge = (dist == radius)
                            || Math.Abs(lx - cx) + Math.Abs(lz - cz + 1) > radius
                            || Math.Abs(lx - cx) + Math.Abs(lz - cz - 1) > radius
                            || Math.Abs(lx - cx + 1) + Math.Abs(lz - cz) > radius
                            || Math.Abs(lx - cx - 1) + Math.Abs(lz - cz) > radius;

                        if (isEdge)
                        {
                            B.AddSingle(plan, t,
                                bS + new Vector3I(lx, y, lz),
                                VoxelMaterialType.Stone);
                        }
                    }
                }
            }
        }

        // ── Stone roof (diamond shape) ──
        int roofY = wallH;
        for (int lx = 0; lx < side; lx++)
        {
            for (int lz = 0; lz < side; lz++)
            {
                if (Math.Abs(lx - cx) + Math.Abs(lz - cz) <= radius)
                {
                    B.AddSingle(plan, t,
                        bS + new Vector3I(lx, roofY, lz),
                        VoxelMaterialType.Stone);
                }
            }
        }

        // ── Concrete accent pillars at diamond cardinal tips ──
        // The 4 tips of the diamond are at (cx +/- radius, cz) and (cx, cz +/- radius)
        int[][] tips = { new[]{cx, cz - radius}, new[]{cx, cz + radius},
                         new[]{cx - radius, cz}, new[]{cx + radius, cz} };
        foreach (int[] tip in tips)
        {
            if (tip[0] >= 0 && tip[0] < side && tip[1] >= 0 && tip[1] < side)
            {
                for (int y = 1; y < wallH; y++)
                {
                    B.AddSingle(plan, t,
                        bS + new Vector3I(tip[0], y, tip[1]),
                        VoxelMaterialType.Concrete);
                }
            }
        }

        // ── Commander at center ──
        Vector3I cmdPos = bS + new Vector3I(cx, 1, cz);
        plan.CommanderBuildUnit = cmdPos;
        B.AddCommanderShell(plan, t, cmdPos, bS, bE, VoxelMaterialType.Stone, 3);
        B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.Stone);

        // ── Weapons on diamond tips (mortar-heavy for area denial) ──
        B.PlaceRoofEdgeWeapons(plan, t, bS, bE, roofY, cmdPos, 3,
            new[] { "mortar", "mortar", "cannon" }, rng, zone);

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, roofY, zone);

        return plan;
    }
}
