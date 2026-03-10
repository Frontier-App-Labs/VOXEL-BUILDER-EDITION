using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Medium Bastion: 12x14 with angled front (2-block thick concrete walls
/// tapering inward at front). Metal roof with stone accent. Raised weapon
/// platform (4 tall) in center. Commander underneath platform in reinforced
/// room. Long-range loadout (railgun + drill on platform, missile + mortar on roof).
/// </summary>
public static class MediumBastion
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int mainW = Math.Min(12, zW - 4);
        int mainD = Math.Min(14, zD - 4);
        int wallH = Math.Min(5, zH - 6);
        int platH = Math.Min(4, wallH - 1); // platform height inside

        int offX = (zW - mainW) / 2;
        int offZ = (zD - mainD) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(mainW - 1, 0, mainD - 1);

        // -- Foundation --
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Concrete);

        // -- 2-block thick concrete outer walls --
        for (int y = 1; y <= wallH; y++)
        {
            // Outer ring
            B.AddHollowLayer(plan, t,
                bS + new Vector3I(0, y, 0),
                bE + new Vector3I(0, y, 0),
                VoxelMaterialType.Concrete);

            // Inner ring (1 block inset)
            if (mainW >= 6 && mainD >= 6)
            {
                B.AddHollowLayer(plan, t,
                    bS + new Vector3I(1, y, 0),
                    bE + new Vector3I(-1, y, 0),
                    VoxelMaterialType.Concrete);
            }
        }

        // -- Angled front: fill additional blocks on front side tapering inward --
        var (_, _, sfUseMinZ, _) = B.GetOutwardEdges(zone);
        int frontZ = sfUseMinZ ? bS.Z : bE.Z;
        int insetDir = sfUseMinZ ? 1 : -1;

        // Taper: 2 extra blocks deep on the outer thirds, 1 extra on inner third
        for (int y = 1; y <= wallH; y++)
        {
            // Left third
            for (int x = bS.X; x < bS.X + mainW / 3; x++)
            {
                B.AddSingle(plan, t,
                    new Vector3I(x, bS.Y + y, frontZ + insetDir * 2),
                    VoxelMaterialType.Concrete);
            }
            // Right third
            for (int x = bE.X - mainW / 3 + 1; x <= bE.X; x++)
            {
                B.AddSingle(plan, t,
                    new Vector3I(x, bS.Y + y, frontZ + insetDir * 2),
                    VoxelMaterialType.Concrete);
            }
        }

        // -- Stone accent band at top of outer walls --
        B.AddHollowLayer(plan, t,
            bS + new Vector3I(0, wallH, 0),
            bE + new Vector3I(0, wallH, 0),
            VoxelMaterialType.Stone);

        // -- Metal roof --
        int roofY = wallH + 1;
        B.AddFloor(plan, t,
            bS + new Vector3I(0, roofY, 0),
            bE + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Metal);

        // -- Raised weapon platform in center (4 tall) --
        int platW = Math.Min(4, mainW - 4);
        int platD = Math.Min(4, mainD - 4);
        int platOffX = (mainW - platW) / 2;
        int platOffZ = (mainD - platD) / 2;

        Vector3I pS = bS + new Vector3I(platOffX, 0, platOffZ);
        Vector3I pE = pS + new Vector3I(platW - 1, 0, platD - 1);

        // Platform columns (bottom-up support)
        B.AddCornerColumns(plan, t, pS, pE, 1, platH, VoxelMaterialType.Concrete);

        // Platform floor at platH+1
        int platFloorY = platH + 1;
        B.AddFloor(plan, t,
            pS + new Vector3I(0, platFloorY, 0),
            pE + new Vector3I(0, platFloorY, 0),
            VoxelMaterialType.Metal);

        // -- Commander underneath platform in reinforced room --
        int cmdOX = platW / 2;
        int cmdOZ = platD / 2;
        cmdOX = Math.Clamp(cmdOX, 1, platW - 2);
        cmdOZ = Math.Clamp(cmdOZ, 1, platD - 2);
        Vector3I cmdPos = pS + new Vector3I(cmdOX, 1, cmdOZ);
        plan.CommanderBuildUnit = cmdPos;

        // Reinforced concrete shell around commander under the platform
        B.AddCommanderShell(plan, t, cmdPos, bS + new Vector3I(2, 0, 2), bE - new Vector3I(2, 0, 2),
            VoxelMaterialType.Concrete, 3);
        B.AddCommanderShell(plan, t, cmdPos, bS + new Vector3I(2, 0, 2), bE - new Vector3I(2, 0, 2),
            VoxelMaterialType.Metal, 5);
        B.EnsureCommanderCeiling(plan, t, cmdPos, pS, pE, VoxelMaterialType.Metal);

        // -- Weapons on platform and roof edges (4-5) --
        int weaponCount = 4 + rng.Next(2);

        // Weapons on raised platform (long-range penetration)
        int platWeapons = Math.Min(2, weaponCount);
        B.PlaceRoofEdgeWeapons(plan, t, pS, pE, platFloorY, cmdPos, platWeapons,
            new[] { "railgun", "drill" }, rng, zone);

        // Weapons on main roof edges (splash damage)
        int roofWeapons = weaponCount - plan.WeaponPlacements.Count;
        if (roofWeapons > 0)
        {
            B.PlaceRoofEdgeWeapons(plan, t, bS, bE, roofY, cmdPos, roofWeapons,
                new[] { "missile", "mortar", "cannon" }, rng, zone);
        }

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, roofY, zone);

        return plan;
    }
}
