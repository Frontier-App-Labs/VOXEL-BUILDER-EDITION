using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Medium Twin Tower: Two 6x6 towers (7 tall each) connected by a 2-wide
/// bridge at Y=4. Commander tower has brick walls with metal reinforcement;
/// weapon tower has stone walls. Concrete bridge. Siege loadout (drill + railgun
/// on weapon tower, missile + mortar on commander tower).
/// </summary>
public static class MediumTwinTower
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int towerSide = Math.Min(6, Math.Min(zW / 2 - 2, zD - 4));
        int towerH = Math.Min(7, zH - 4);
        int bridgeGap = Math.Max(2, Math.Min(4, zW - towerSide * 2 - 4));
        int bridgeWidth = 2;

        // Total width: tower + gap + tower
        int totalW = towerSide * 2 + bridgeGap;
        int offX = (zW - totalW) / 2;
        int offZ = (zD - towerSide) / 2;

        // Tower A (left/front, has commander)
        Vector3I aS = o + new Vector3I(offX, 0, offZ);
        Vector3I aE = aS + new Vector3I(towerSide - 1, 0, towerSide - 1);

        // Tower B (right/back, weapon tower)
        Vector3I bS2 = aS + new Vector3I(towerSide + bridgeGap, 0, 0);
        Vector3I bE2 = bS2 + new Vector3I(towerSide - 1, 0, towerSide - 1);

        // Use a combined bS/bE for commander shell clamping
        Vector3I combS = aS;
        Vector3I combE = bE2;

        // -- Build Tower A (commander tower: brick with metal top) --
        B.AddFloor(plan, t, aS, aE, VoxelMaterialType.Concrete);
        for (int y = 1; y < towerH; y++)
        {
            var taMat = y >= towerH - 2
                ? VoxelMaterialType.Metal
                : VoxelMaterialType.Brick;
            B.AddHollowLayer(plan, t,
                aS + new Vector3I(0, y, 0),
                aE + new Vector3I(0, y, 0),
                taMat);
        }
        // Tower A roof
        int roofY = towerH;
        B.AddFloor(plan, t,
            aS + new Vector3I(0, roofY, 0),
            aE + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Metal);
        B.AddCrenellations(plan, t, aS, aE, roofY + 1, VoxelMaterialType.Brick);

        // -- Build Tower B --
        B.AddFloor(plan, t, bS2, bE2, VoxelMaterialType.Stone);
        for (int y = 1; y < towerH; y++)
        {
            B.AddHollowLayer(plan, t,
                bS2 + new Vector3I(0, y, 0),
                bE2 + new Vector3I(0, y, 0),
                VoxelMaterialType.Stone);
        }
        // Tower B roof
        B.AddFloor(plan, t,
            bS2 + new Vector3I(0, roofY, 0),
            bE2 + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Stone);
        B.AddCrenellations(plan, t, bS2, bE2, roofY + 1, VoxelMaterialType.Stone);

        // -- Bridge connecting towers at Y=4 (concrete, 2 wide) --
        int bridgeY = Math.Min(4, towerH - 1);
        int bridgeZ = aS.Z + towerSide / 2 - bridgeWidth / 2;
        bridgeZ = Math.Clamp(bridgeZ, aS.Z + 1, aE.Z - bridgeWidth);

        Vector3I brS = new Vector3I(aE.X + 1, aS.Y + bridgeY, bridgeZ);
        Vector3I brE = new Vector3I(bS2.X - 1, aS.Y + bridgeY, bridgeZ + bridgeWidth - 1);

        if (brE.X >= brS.X)
        {
            // Bridge floor
            B.AddFloor(plan, t, brS, brE, VoxelMaterialType.Concrete);

            // Bridge railings (1 tall on each side)
            for (int x = brS.X; x <= brE.X; x++)
            {
                B.AddSingle(plan, t,
                    new Vector3I(x, brS.Y + 1, brS.Z),
                    VoxelMaterialType.Concrete);
                B.AddSingle(plan, t,
                    new Vector3I(x, brS.Y + 1, brE.Z),
                    VoxelMaterialType.Concrete);
            }

            // Bridge support columns (bottom-up from ground to bridge)
            int midBridgeX = (brS.X + brE.X) / 2;
            for (int y = 0; y < bridgeY; y++)
            {
                B.AddSingle(plan, t,
                    new Vector3I(midBridgeX, aS.Y + y, bridgeZ),
                    VoxelMaterialType.Concrete);
                B.AddSingle(plan, t,
                    new Vector3I(midBridgeX, aS.Y + y, bridgeZ + bridgeWidth - 1),
                    VoxelMaterialType.Concrete);
            }
        }

        // -- Commander in Tower A (metal shell) --
        int cmdOX = Math.Clamp(towerSide / 2, 2, towerSide - 3);
        int cmdOZ = Math.Clamp(towerSide / 2, 2, towerSide - 3);
        Vector3I cmdPos = aS + new Vector3I(cmdOX, 1, cmdOZ);
        plan.CommanderBuildUnit = cmdPos;

        B.AddCommanderShell(plan, t, cmdPos, aS, aE, VoxelMaterialType.Metal, 3);
        B.EnsureCommanderCeiling(plan, t, cmdPos, aS, aE, VoxelMaterialType.Metal);

        // -- Weapons split between towers (4-5, siege loadout) --
        int weaponCount = 4 + rng.Next(2);

        // Weapons on Tower B roof (weapon tower: penetration weapons)
        int towerBWeapons = Math.Min(3, weaponCount);
        B.PlaceRoofEdgeWeapons(plan, t, bS2, bE2, roofY, cmdPos, towerBWeapons,
            new[] { "drill", "railgun", "cannon" }, rng, zone);

        // Remaining weapons on Tower A roof (splash/lob weapons)
        int towerAWeapons = weaponCount - plan.WeaponPlacements.Count;
        if (towerAWeapons > 0)
        {
            B.PlaceRoofEdgeWeapons(plan, t, aS, aE, roofY, cmdPos, towerAWeapons,
                new[] { "missile", "mortar" }, rng, zone);
        }

        B.EnsureAtLeastOneWeapon(plan, t, bS2, bE2, roofY, zone);

        return plan;
    }
}
