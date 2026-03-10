using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Easy base: 8x10 two-room design split along the depth axis.
/// Front room faces outward (combat room), back room houses the
/// commander. Concrete walls 4 tall with window slits, stone
/// interior dividing wall. Mixed tactical loadout including a drill
/// for penetrating enemy defenses.
/// </summary>
public static class EasyBlockhouse
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zD = zone.SizeBuildUnits.Z;

        int width = Math.Min(8, zW - 4);
        int depth = Math.Min(10, zD - 4);
        int wallH = 4;

        int offX = (zW - width) / 2;
        int offZ = (zD - depth) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(width - 1, 0, depth - 1);

        // Determine which end faces outward (toward arena center)
        var (useMinX, useMaxX, useMinZ, useMaxZ) = B.GetOutwardEdges(zone);

        // The dividing wall splits the depth in half
        int divideZ = depth / 2;

        // Decide which half is the "front" (combat room) and which is "back" (commander room)
        // Front faces toward the arena center
        bool frontIsMinZ = useMinZ;

        // ── Concrete foundation ──
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Concrete);

        // ── Concrete outer walls (hollow) ──
        for (int y = 1; y < wallH; y++)
        {
            Vector3I layerS = bS + new Vector3I(0, y, 0);
            Vector3I layerE = bE + new Vector3I(0, y, 0);
            B.AddHollowLayer(plan, t, layerS, layerE, VoxelMaterialType.Concrete);
        }

        // ── Interior stone dividing wall ──
        int wallZ = frontIsMinZ ? divideZ : (depth - divideZ);
        for (int y = 1; y < wallH; y++)
        {
            for (int lx = 1; lx < width - 1; lx++)
            {
                B.AddSingle(plan, t,
                    bS + new Vector3I(lx, y, wallZ),
                    VoxelMaterialType.Stone);
            }
        }

        // ── Window slits at y=2 on long walls (every other block) ──
        // Place stone accent blocks to create a slit pattern in concrete walls
        for (int lz = 1; lz < depth - 1; lz += 2)
        {
            B.AddSingle(plan, t, bS + new Vector3I(0, 2, lz), VoxelMaterialType.Stone);
            B.AddSingle(plan, t, bS + new Vector3I(width - 1, 2, lz), VoxelMaterialType.Stone);
        }

        // ── Full roof over both rooms ──
        int roofY = wallH;
        Vector3I roofS = bS + new Vector3I(0, roofY, 0);
        Vector3I roofE = bE + new Vector3I(0, roofY, 0);
        B.AddFloor(plan, t, roofS, roofE, VoxelMaterialType.Concrete);

        // ── Stone accent strip along roof center ──
        for (int lx = 0; lx < width; lx++)
        {
            B.AddSingle(plan, t, bS + new Vector3I(lx, roofY, depth / 2), VoxelMaterialType.Stone);
        }

        // ── Commander in the back room ──
        int cmdX = Math.Clamp(width / 2, 2, width - 3);
        int cmdZ;
        if (frontIsMinZ)
        {
            // Back room is the high-Z half
            cmdZ = Math.Clamp(wallZ + (depth - wallZ) / 2, wallZ + 1, depth - 3);
        }
        else
        {
            // Back room is the low-Z half
            cmdZ = Math.Clamp(wallZ / 2, 2, wallZ - 1);
        }

        Vector3I cmdPos = bS + new Vector3I(cmdX, 1, cmdZ);
        plan.CommanderBuildUnit = cmdPos;
        B.AddCommanderShell(plan, t, cmdPos, bS, bE, VoxelMaterialType.Stone, 3);
        B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.Stone);

        // ── Weapons on front room roof only ──
        // Create bS/bE for the front room only for weapon placement
        Vector3I frontS, frontE;
        if (frontIsMinZ)
        {
            frontS = bS;
            frontE = bS + new Vector3I(width - 1, 0, wallZ - 1);
        }
        else
        {
            frontS = bS + new Vector3I(0, 0, wallZ + 1);
            frontE = bE;
        }

        B.PlaceRoofEdgeWeapons(plan, t, frontS, frontE, roofY, cmdPos, 3,
            new[] { "cannon", "drill", "mortar" }, rng, zone);

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, roofY, zone);

        return plan;
    }
}
