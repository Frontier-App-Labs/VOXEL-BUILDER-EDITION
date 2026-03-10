using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Medium Courtyard: 16x16 perimeter wall (4 tall, concrete) enclosing an
/// open courtyard. Small 6x6 keep in one corner (6 tall, metal walls).
/// Commander in keep with concrete shell. 4-5 weapons on walls and keep.
/// </summary>
public static class MediumCourtyard
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int outerW = Math.Min(16, zW - 4);
        int outerD = Math.Min(16, zD - 4);
        int wallH = Math.Min(4, zH - 6);
        int keepSide = Math.Min(6, outerW - 4);
        int keepH = Math.Min(6, zH - 4);

        int offX = (zW - outerW) / 2;
        int offZ = (zD - outerD) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(outerW - 1, 0, outerD - 1);

        // -- Foundation slab --
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Concrete);

        // -- Perimeter concrete walls (4 tall, hollow) --
        for (int y = 1; y <= wallH; y++)
        {
            B.AddHollowLayer(plan, t,
                bS + new Vector3I(0, y, 0),
                bE + new Vector3I(0, y, 0),
                VoxelMaterialType.Concrete);
        }

        // -- Perimeter walkway at top of walls (brick for contrast) --
        int walkwayY = wallH + 1;
        B.AddPerimeterWalkway(plan, t, bS, bE, walkwayY, 2, VoxelMaterialType.Brick);

        // -- Crenellations on perimeter (stone for a classic look) --
        B.AddCrenellations(plan, t, bS, bE, walkwayY + 1, VoxelMaterialType.Stone);

        // -- Small keep in one corner (away from outward edges) --
        // Place keep in the corner furthest from enemies
        var (useMinX, useMaxX, useMinZ, useMaxZ) = B.GetOutwardEdges(zone);
        int keepOffX = useMinX ? outerW - keepSide - 1 : 1;
        int keepOffZ = useMinZ ? outerD - keepSide - 1 : 1;
        keepOffX = Math.Clamp(keepOffX, 1, outerW - keepSide - 1);
        keepOffZ = Math.Clamp(keepOffZ, 1, outerD - keepSide - 1);

        Vector3I kS = bS + new Vector3I(keepOffX, 0, keepOffZ);
        Vector3I kE = kS + new Vector3I(keepSide - 1, 0, keepSide - 1);

        // Keep foundation
        B.AddFloor(plan, t, kS, kE, VoxelMaterialType.Metal);

        // Keep metal walls
        for (int y = 1; y <= keepH; y++)
        {
            B.AddHollowLayer(plan, t,
                kS + new Vector3I(0, y, 0),
                kE + new Vector3I(0, y, 0),
                VoxelMaterialType.Metal);
        }

        // Keep roof
        int keepRoofY = keepH + 1;
        B.AddFloor(plan, t,
            kS + new Vector3I(0, keepRoofY, 0),
            kE + new Vector3I(0, keepRoofY, 0),
            VoxelMaterialType.Metal);

        // -- Commander inside the keep --
        int cmdOX = Math.Clamp(keepSide / 2, 2, keepSide - 3);
        int cmdOZ = Math.Clamp(keepSide / 2, 2, keepSide - 3);
        Vector3I cmdPos = kS + new Vector3I(cmdOX, 1, cmdOZ);
        plan.CommanderBuildUnit = cmdPos;

        B.AddCommanderShell(plan, t, cmdPos, kS, kE, VoxelMaterialType.Concrete, 3);
        B.EnsureCommanderCeiling(plan, t, cmdPos, kS, kE, VoxelMaterialType.Concrete);

        // -- Weapons on perimeter walls and keep (4-5) --
        int weaponCount = 4 + rng.Next(2);

        // Weapons on keep roof (precision weapons on the high ground)
        int keepWeapons = Math.Min(2, weaponCount);
        B.PlaceRoofEdgeWeapons(plan, t, kS, kE, keepRoofY, cmdPos, keepWeapons,
            new[] { "railgun", "drill" }, rng, zone);

        // Weapons on perimeter walkway
        int wallWeapons = weaponCount - plan.WeaponPlacements.Count;
        if (wallWeapons > 0)
        {
            B.PlaceRoofEdgeWeapons(plan, t, bS, bE, walkwayY, cmdPos, wallWeapons,
                new[] { "cannon", "mortar", "missile" }, rng, zone);
        }

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, walkwayY, zone);

        return plan;
    }
}
