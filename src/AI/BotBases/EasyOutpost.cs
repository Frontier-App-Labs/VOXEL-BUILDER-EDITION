using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Easy base: Small 6x6 stone tower (8 tall) surrounded by a 10x10
/// low dirt perimeter wall (3 tall). Commander lives in the tower.
/// 3 weapons: 2 cannon on the perimeter wall, 1 railgun on the tower
/// top (elevation advantage for precision shots).
/// </summary>
public static class EasyOutpost
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        // Outer perimeter wall dimensions
        int outerW = Math.Min(10, zW - 4);
        int outerD = Math.Min(10, zD - 4);
        int perimH = 3;

        int offX = (zW - outerW) / 2;
        int offZ = (zD - outerD) / 2;

        Vector3I pS = o + new Vector3I(offX, 0, offZ);         // perimeter start
        Vector3I pE = pS + new Vector3I(outerW - 1, 0, outerD - 1);

        // Inner tower dimensions (centered)
        int towerSide = Math.Min(6, outerW - 4);
        int towerH = Math.Min(8, zH - 4);

        int tOffX = (outerW - towerSide) / 2;
        int tOffZ = (outerD - towerSide) / 2;

        Vector3I tS = pS + new Vector3I(tOffX, 0, tOffZ);      // tower start
        Vector3I tE = tS + new Vector3I(towerSide - 1, 0, towerSide - 1);

        // ══════════════════════════════════════
        //  Build perimeter wall (bottom-up)
        // ══════════════════════════════════════

        // Perimeter foundation
        B.AddFloor(plan, t, pS, pE, VoxelMaterialType.Dirt);

        // Perimeter walls (hollow, 3 tall)
        for (int y = 1; y < perimH; y++)
        {
            Vector3I layerS = pS + new Vector3I(0, y, 0);
            Vector3I layerE = pE + new Vector3I(0, y, 0);
            B.AddHollowLayer(plan, t, layerS, layerE, VoxelMaterialType.Dirt);
        }

        // Perimeter walkway/roof
        int perimRoofY = perimH;
        Vector3I perimRoofS = pS + new Vector3I(0, perimRoofY, 0);
        Vector3I perimRoofE = pE + new Vector3I(0, perimRoofY, 0);
        B.AddFloor(plan, t, perimRoofS, perimRoofE, VoxelMaterialType.Dirt);

        // ══════════════════════════════════════
        //  Build inner tower (bottom-up)
        // ══════════════════════════════════════

        // Tower foundation (fills on top of existing perimeter floor)
        B.AddFloor(plan, t, tS, tE, VoxelMaterialType.Stone);

        // Tower walls
        for (int y = 1; y < towerH; y++)
        {
            Vector3I layerS = tS + new Vector3I(0, y, 0);
            Vector3I layerE = tE + new Vector3I(0, y, 0);
            B.AddHollowLayer(plan, t, layerS, layerE, VoxelMaterialType.Stone);
        }

        // Brick accent band at mid-height of tower
        int towerBandY = towerH / 2;
        {
            Vector3I layerS = tS + new Vector3I(0, towerBandY, 0);
            Vector3I layerE = tE + new Vector3I(0, towerBandY, 0);
            B.AddHollowLayer(plan, t, layerS, layerE, VoxelMaterialType.Brick);
        }

        // Tower roof
        int towerRoofY = towerH;
        Vector3I towerRoofS = tS + new Vector3I(0, towerRoofY, 0);
        Vector3I towerRoofE = tE + new Vector3I(0, towerRoofY, 0);
        B.AddFloor(plan, t, towerRoofS, towerRoofE, VoxelMaterialType.Stone);

        // Crenellations on perimeter wall
        B.AddCrenellations(plan, t, pS, pE, perimRoofY + 1, VoxelMaterialType.Stone);

        // ══════════════════════════════════════
        //  Commander inside the tower
        // ══════════════════════════════════════

        int cmdX = Math.Clamp(towerSide / 2, 2, towerSide - 3);
        int cmdZ = Math.Clamp(towerSide / 2, 2, towerSide - 3);
        Vector3I cmdPos = tS + new Vector3I(cmdX, 1, cmdZ);
        plan.CommanderBuildUnit = cmdPos;
        B.AddCommanderShell(plan, t, cmdPos, tS, tE, VoxelMaterialType.Stone, 3);
        B.EnsureCommanderCeiling(plan, t, cmdPos, tS, tE, VoxelMaterialType.Stone);

        // ══════════════════════════════════════
        //  Weapons: 2 on perimeter, 1 on tower top
        // ══════════════════════════════════════

        // 2 cannons on perimeter wall roof edges
        B.PlaceRoofEdgeWeapons(plan, t, pS, pE, perimRoofY, cmdPos, 2,
            new[] { "cannon", "cannon" }, rng, zone);

        // 1 railgun on tower roof (elevated precision weapon)
        B.PlaceRoofEdgeWeapons(plan, t, tS, tE, towerRoofY, cmdPos, 1,
            new[] { "railgun" }, rng, zone);

        B.EnsureAtLeastOneWeapon(plan, t, pS, pE, perimRoofY, zone);

        return plan;
    }
}
