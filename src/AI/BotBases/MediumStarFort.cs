using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Core;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Medium Star Fort: Star-shaped design with a 10x10 core and 4 triangular
/// bastions (3-wide protrusions on each side, 2 deep). Concrete core walls,
/// brick bastions, stone foundation. Commander center with metal+concrete shell.
/// Balanced loadout including drill.
/// </summary>
public static class MediumStarFort
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int coreW = Math.Min(10, zW - 8); // leave room for bastions
        int coreD = Math.Min(10, zD - 8);
        int wallH = Math.Min(5, zH - 5);
        int bastionDepth = 2;
        int bastionWidth = 3;

        // Center core in zone
        int offX = (zW - coreW) / 2;
        int offZ = (zD - coreD) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(coreW - 1, 0, coreD - 1);

        // -- Core foundation (stone) --
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Stone);

        // -- Core concrete walls --
        for (int y = 1; y <= wallH; y++)
        {
            B.AddHollowLayer(plan, t,
                bS + new Vector3I(0, y, 0),
                bE + new Vector3I(0, y, 0),
                VoxelMaterialType.Concrete);
        }

        // -- Core roof --
        int roofY = wallH + 1;
        B.AddFloor(plan, t,
            bS + new Vector3I(0, roofY, 0),
            bE + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Concrete);

        // -- 4 bastions (triangular protrusions, built as rectangles tapering) --
        // Each bastion is 3 wide x 2 deep, centered on each side
        int midX = coreW / 2;
        int midZ = coreD / 2;

        // Bastion positions: (start corner, end corner) for each side
        // MinZ side (front)
        Vector3I[] bastionStarts = new Vector3I[4];
        Vector3I[] bastionEnds = new Vector3I[4];

        // Front bastion (-Z)
        int bFrontX = bS.X + midX - bastionWidth / 2;
        bFrontX = Math.Clamp(bFrontX, o.X, o.X + zW - bastionWidth);
        int bFrontZ = bS.Z - bastionDepth;
        bFrontZ = Math.Max(bFrontZ, o.Z);
        bastionStarts[0] = new Vector3I(bFrontX, o.Y, bFrontZ);
        bastionEnds[0] = new Vector3I(bFrontX + bastionWidth - 1, o.Y, bS.Z);

        // Back bastion (+Z)
        int bBackX = bS.X + midX - bastionWidth / 2;
        bBackX = Math.Clamp(bBackX, o.X, o.X + zW - bastionWidth);
        int bBackZ = bE.Z + 1;
        int bBackZEnd = Math.Min(bE.Z + bastionDepth, o.Z + zD - 1);
        bastionStarts[1] = new Vector3I(bBackX, o.Y, bBackZ);
        bastionEnds[1] = new Vector3I(bBackX + bastionWidth - 1, o.Y, bBackZEnd);

        // Left bastion (-X)
        int bLeftZ = bS.Z + midZ - bastionWidth / 2;
        bLeftZ = Math.Clamp(bLeftZ, o.Z, o.Z + zD - bastionWidth);
        int bLeftX = bS.X - bastionDepth;
        bLeftX = Math.Max(bLeftX, o.X);
        bastionStarts[2] = new Vector3I(bLeftX, o.Y, bLeftZ);
        bastionEnds[2] = new Vector3I(bS.X, o.Y, bLeftZ + bastionWidth - 1);

        // Right bastion (+X)
        int bRightZ = bS.Z + midZ - bastionWidth / 2;
        bRightZ = Math.Clamp(bRightZ, o.Z, o.Z + zD - bastionWidth);
        int bRightX = bE.X + 1;
        int bRightXEnd = Math.Min(bE.X + bastionDepth, o.X + zW - 1);
        bastionStarts[3] = new Vector3I(bRightX, o.Y, bRightZ);
        bastionEnds[3] = new Vector3I(bRightXEnd, o.Y, bRightZ + bastionWidth - 1);

        // Build each bastion
        List<Vector3I> bastionTips = new List<Vector3I>();
        for (int bi = 0; bi < 4; bi++)
        {
            Vector3I bs = bastionStarts[bi];
            Vector3I be = bastionEnds[bi];

            // Foundation
            B.AddFloor(plan, t, bs, be, VoxelMaterialType.Stone);

            // Walls (brick to contrast with concrete core)
            for (int y = 1; y <= wallH; y++)
            {
                B.AddHollowLayer(plan, t,
                    bs + new Vector3I(0, y, 0),
                    be + new Vector3I(0, y, 0),
                    VoxelMaterialType.Brick);
            }

            // Roof
            B.AddFloor(plan, t,
                bs + new Vector3I(0, roofY, 0),
                be + new Vector3I(0, roofY, 0),
                VoxelMaterialType.Brick);

            // Bastion tip for weapon placement (outward center of bastion)
            int tipX, tipZ;
            switch (bi)
            {
                case 0: // front (-Z)
                    tipX = (bs.X + be.X) / 2;
                    tipZ = bs.Z;
                    break;
                case 1: // back (+Z)
                    tipX = (bs.X + be.X) / 2;
                    tipZ = be.Z;
                    break;
                case 2: // left (-X)
                    tipX = bs.X;
                    tipZ = (bs.Z + be.Z) / 2;
                    break;
                default: // right (+X)
                    tipX = be.X;
                    tipZ = (bs.Z + be.Z) / 2;
                    break;
            }
            bastionTips.Add(new Vector3I(tipX, bS.Y + roofY + 4, tipZ));
        }

        // -- Commander at center with metal shell --
        int cmdOX = coreW / 2;
        int cmdOZ = coreD / 2;
        cmdOX = Math.Clamp(cmdOX, 2, coreW - 3);
        cmdOZ = Math.Clamp(cmdOZ, 2, coreD - 3);
        Vector3I cmdPos = bS + new Vector3I(cmdOX, 1, cmdOZ);
        plan.CommanderBuildUnit = cmdPos;

        B.AddCommanderShell(plan, t, cmdPos, bS, bE, VoxelMaterialType.Metal, 3);
        B.AddCommanderShell(plan, t, cmdPos, bS, bE, VoxelMaterialType.Concrete, 5);
        B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.Metal);

        // -- 5 weapons on bastion tips (balanced loadout with drill) --
        string[] weapons = { "drill", "railgun", "missile", "mortar", "cannon" };
        var (useMinX, useMaxX, useMinZ, useMaxZ) = B.GetOutwardEdges(zone);
        List<Vector3I> placed = new List<Vector3I>();

        // Map bastions to outward edges: 0=front(-Z), 1=back(+Z), 2=left(-X), 3=right(+X)
        bool[] bastionOutward = { useMinZ, useMaxZ, useMinX, useMaxX };

        for (int i = 0; i < bastionTips.Count && placed.Count < 5; i++)
        {
            if (!bastionOutward[i]) continue;

            Vector3I wPos = bastionTips[i];
            if (wPos.DistanceTo(cmdPos) < GameConfig.MinWeaponCommanderGap) continue;

            string wId = weapons[placed.Count % weapons.Length];
            int cost = B.GetWeaponCost(wId);
            bool guaranteed = placed.Count < 3;

            if (guaranteed || t.TrySpend(cost))
            {
                if (guaranteed) t.TrySpend(cost);

                // 3-block pillar under weapon
                for (int py = 1; py <= 3; py++)
                {
                    B.AddSingle(plan, t,
                        new Vector3I(wPos.X, bS.Y + roofY + py, wPos.Z),
                        VoxelMaterialType.Stone);
                }
                plan.WeaponPlacements.Add((wPos, wId));
                placed.Add(wPos);
            }
        }

        // Fallback: also try non-outward bastions if we need more weapons
        for (int i = 0; i < bastionTips.Count && placed.Count < 5; i++)
        {
            if (bastionOutward[i]) continue; // already tried
            if (placed.Contains(bastionTips[i])) continue;

            Vector3I wPos = bastionTips[i];
            if (wPos.DistanceTo(cmdPos) < GameConfig.MinWeaponCommanderGap) continue;

            string wId = weapons[placed.Count % weapons.Length];
            int cost = B.GetWeaponCost(wId);

            if (placed.Count < 3 || t.TrySpend(cost))
            {
                if (placed.Count < 3) t.TrySpend(cost);

                for (int py = 1; py <= 3; py++)
                {
                    B.AddSingle(plan, t,
                        new Vector3I(wPos.X, bS.Y + roofY + py, wPos.Z),
                        VoxelMaterialType.Stone);
                }
                plan.WeaponPlacements.Add((wPos, wId));
                placed.Add(wPos);
            }
        }

        // Extra weapons on core roof if still short
        if (placed.Count < 4)
        {
            B.PlaceRoofEdgeWeapons(plan, t, bS, bE, roofY, cmdPos,
                5 - placed.Count, new[] { "cannon", "mortar" }, rng, zone);
        }

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, roofY, zone);

        return plan;
    }
}
