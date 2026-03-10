using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Easy base: 8x8 square tower, 7 tall, stone walls with a brick
/// accent band at mid-height. Crenellations on top. Commander center
/// with stone shell + ceiling. 3-4 weapons on the roof.
/// </summary>
public static class EasyWatchtower
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int side = Math.Min(8, Math.Min(zW - 4, zD - 4));
        int height = Math.Min(7, zH - 4);

        int offX = (zW - side) / 2;
        int offZ = (zD - side) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(side - 1, 0, side - 1);

        // ── Foundation ──
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Stone);

        // ── Stone walls layer by layer ──
        for (int y = 1; y < height; y++)
        {
            Vector3I layerS = bS + new Vector3I(0, y, 0);
            Vector3I layerE = bE + new Vector3I(0, y, 0);
            B.AddHollowLayer(plan, t, layerS, layerE, VoxelMaterialType.Stone);
        }

        // ── Brick accent band at mid-height ──
        int bandY = height / 2;
        {
            Vector3I layerS = bS + new Vector3I(0, bandY, 0);
            Vector3I layerE = bE + new Vector3I(0, bandY, 0);
            B.AddHollowLayer(plan, t, layerS, layerE, VoxelMaterialType.Brick);
        }

        // ── Roof slab ──
        int roofY = height;
        Vector3I roofS = bS + new Vector3I(0, roofY, 0);
        Vector3I roofE = bE + new Vector3I(0, roofY, 0);
        B.AddFloor(plan, t, roofS, roofE, VoxelMaterialType.Stone);

        // ── Crenellations on roof ──
        B.AddCrenellations(plan, t, bS, bE, roofY + 1, VoxelMaterialType.Stone);

        // ── Commander inside ──
        int cmdSide = Math.Clamp(side / 2, 2, side - 3);
        Vector3I cmdPos = bS + new Vector3I(cmdSide, 1, cmdSide);
        plan.CommanderBuildUnit = cmdPos;
        B.AddCommanderShell(plan, t, cmdPos, bS, bE, VoxelMaterialType.Stone, 3);
        B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.Stone);

        // ── Weapons on roof edges ──
        B.PlaceRoofEdgeWeapons(plan, t, bS, bE, roofY, cmdPos, 3 + rng.Next(2),
            new[] { "cannon", "mortar", "railgun" }, rng, zone);

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, roofY, zone);

        return plan;
    }
}
