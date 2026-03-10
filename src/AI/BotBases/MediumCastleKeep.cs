using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Medium Castle Keep: 14x14 main walls 5 tall with 4 corner towers
/// (8 tall, 4x4 footprint). Stone base with brick accent bands and
/// concrete-reinforced tower tops. Commander well-inset with concrete+metal
/// shell. Crenellations on top. Area-denial loadout (mortars + cannons).
/// </summary>
public static class MediumCastleKeep
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        // Main keep dimensions
        int mainW = Math.Min(14, zW - 4);
        int mainD = Math.Min(14, zD - 4);
        int wallH = Math.Min(5, zH - 6);
        int towerH = Math.Min(8, zH - 3);
        int towerSize = 4;

        // Center in zone
        int offX = (zW - mainW) / 2;
        int offZ = (zD - mainD) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(mainW - 1, 0, mainD - 1);

        // -- Foundation slab (stone) --
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Stone);

        // -- Main stone walls layer by layer --
        for (int y = 1; y <= wallH; y++)
        {
            Vector3I layerS = bS + new Vector3I(0, y, 0);
            Vector3I layerE = bE + new Vector3I(0, y, 0);
            B.AddHollowLayer(plan, t, layerS, layerE, VoxelMaterialType.Stone);
        }

        // -- Brick accent band at mid-height --
        int bandY = wallH / 2 + 1;
        B.AddHollowLayer(plan, t,
            bS + new Vector3I(0, bandY, 0),
            bE + new Vector3I(0, bandY, 0),
            VoxelMaterialType.Brick);

        // -- Main roof --
        int roofY = wallH + 1;
        B.AddFloor(plan, t,
            bS + new Vector3I(0, roofY, 0),
            bE + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Stone);

        // -- Crenellations on main walls --
        B.AddCrenellations(plan, t, bS, bE, roofY + 1, VoxelMaterialType.Stone);

        // -- Four corner towers (4x4, 8 tall) with concrete reinforced tops --
        Vector3I[] towerOrigins =
        {
            bS,
            new Vector3I(bE.X - towerSize + 1, bS.Y, bS.Z),
            new Vector3I(bS.X, bS.Y, bE.Z - towerSize + 1),
            new Vector3I(bE.X - towerSize + 1, bS.Y, bE.Z - towerSize + 1),
        };

        foreach (Vector3I tOrigin in towerOrigins)
        {
            Vector3I tEnd = tOrigin + new Vector3I(towerSize - 1, 0, towerSize - 1);

            // Tower walls above main wall height: stone lower, concrete upper
            for (int y = wallH + 1; y < towerH; y++)
            {
                var towerMat = y >= towerH - 2
                    ? VoxelMaterialType.Concrete
                    : VoxelMaterialType.Stone;
                B.AddHollowLayer(plan, t,
                    tOrigin + new Vector3I(0, y, 0),
                    tEnd + new Vector3I(0, y, 0),
                    towerMat);
            }

            // Tower roof (concrete for durability)
            B.AddFloor(plan, t,
                tOrigin + new Vector3I(0, towerH, 0),
                tEnd + new Vector3I(0, towerH, 0),
                VoxelMaterialType.Concrete);

            // Tower crenellations (brick to match accent band)
            B.AddCrenellations(plan, t, tOrigin, tEnd, towerH + 1, VoxelMaterialType.Brick);
        }

        // -- Commander: well-inset from walls --
        int cmdOX = mainW / 2 + rng.Next(3) - 1;
        int cmdOZ = mainD / 2 + rng.Next(3) - 1;
        cmdOX = Math.Clamp(cmdOX, 3, mainW - 4);
        cmdOZ = Math.Clamp(cmdOZ, 3, mainD - 4);
        Vector3I cmdPos = bS + new Vector3I(cmdOX, 1, cmdOZ);
        plan.CommanderBuildUnit = cmdPos;

        // Concrete inner shell + metal outer shell
        B.AddCommanderShell(plan, t, cmdPos, bS, bE, VoxelMaterialType.Concrete, 3);
        B.AddCommanderShell(plan, t, cmdPos, bS, bE, VoxelMaterialType.Metal, 5);
        B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.Concrete);

        // -- Weapons on tower tops and roof (4-5, area-denial loadout) --
        int weaponCount = 4 + rng.Next(2);
        B.PlaceRoofEdgeWeapons(plan, t, bS, bE, roofY, cmdPos, weaponCount,
            new[] { "mortar", "cannon", "mortar", "cannon", "mortar" }, rng, zone);

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, roofY, zone);

        return plan;
    }
}
