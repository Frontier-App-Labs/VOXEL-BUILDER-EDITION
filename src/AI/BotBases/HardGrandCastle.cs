using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Hard base: 18x18 curtain walls (6 tall, ArmorPlate front face) with 4 corner
/// towers (4x4, 10 tall, ReinforcedSteel upper section), central 8x8 ReinforcedSteel
/// keep, gatehouse protrusion on the front wall. Commander hidden in an obsidian
/// vault in a random quadrant with decoy rooms in opposite quadrants. 5-7 weapons
/// on towers, keep, and curtain walkway. Crenellations and perimeter walkways
/// throughout. Premium materials: ArmorPlate exterior, ReinforcedSteel keep/towers.
/// </summary>
public static class HardGrandCastle
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int outerW = Math.Min(18, zW - 4);
        int outerD = Math.Min(18, zD - 4);
        int wallH = Math.Min(6, zH - 8);
        int towerH = Math.Min(wallH + 4, zH - 4);
        int towerSize = 4;

        int offX = (zW - outerW) / 2;
        int offZ = (zD - outerD) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(outerW - 1, 0, outerD - 1);

        // ── Foundation ──
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Stone);

        // ── Curtain walls ──
        for (int y = 1; y <= wallH; y++)
        {
            B.AddHollowLayer(plan, t,
                bS + new Vector3I(0, y, 0), bE + new Vector3I(0, y, 0),
                VoxelMaterialType.Stone);
        }

        // ── Brick accent near top ──
        int accentY = wallH;
        B.AddHollowLayer(plan, t,
            bS + new Vector3I(0, accentY, 0), bE + new Vector3I(0, accentY, 0),
            VoxelMaterialType.Brick);

        // ── ArmorPlate on front face of curtain wall ──
        var (_, _, apUseMinZ, _) = B.GetOutwardEdges(zone);
        int apFrontZ = apUseMinZ ? bS.Z : bE.Z;
        for (int y = 1; y <= wallH; y++)
        {
            for (int x = bS.X; x <= bE.X; x++)
            {
                B.AddSingle(plan, t, new Vector3I(x, y, apFrontZ), VoxelMaterialType.ArmorPlate);
            }
        }

        // ── Curtain wall walkway (2 blocks wide perimeter at wall top) ──
        int walkwayY = wallH + 1;
        B.AddPerimeterWalkway(plan, t, bS, bE, walkwayY, 2, VoxelMaterialType.Stone);

        // ── Crenellations on curtain wall ──
        B.AddCrenellations(plan, t, bS, bE, walkwayY + 1, VoxelMaterialType.Stone);

        // ── Four corner towers ──
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

            // Tower walls above curtain wall (ReinforcedSteel for premium defense)
            for (int y = wallH + 1; y < towerH; y++)
            {
                B.AddHollowLayer(plan, t,
                    tOrigin + new Vector3I(0, y, 0), tEnd + new Vector3I(0, y, 0),
                    VoxelMaterialType.ReinforcedSteel);
            }

            // Tower roof (ReinforcedSteel)
            int tRoofY = towerH;
            B.AddFloor(plan, t,
                tOrigin + new Vector3I(0, tRoofY, 0), tEnd + new Vector3I(0, tRoofY, 0),
                VoxelMaterialType.ReinforcedSteel);

            // Tower crenellations
            B.AddCrenellations(plan, t, tOrigin, tEnd, tRoofY + 1, VoxelMaterialType.ReinforcedSteel);
        }

        // ── Central keep (metal, inside the courtyard) ──
        int keepW = Math.Min(outerW - 8, 8);
        int keepD = Math.Min(outerD - 8, 8);
        int keepH = Math.Min(wallH + 2, zH - 4);

        Vector3I kS = bS + new Vector3I((outerW - keepW) / 2, 0, (outerD - keepD) / 2);
        Vector3I kE = kS + new Vector3I(keepW - 1, 0, keepD - 1);

        if (keepW >= 5 && keepD >= 5)
        {
            // Keep floor
            B.AddFloor(plan, t, kS, kE, VoxelMaterialType.ReinforcedSteel);

            // Keep walls (ReinforcedSteel for premium defense)
            for (int y = 1; y <= keepH; y++)
            {
                B.AddHollowLayer(plan, t,
                    kS + new Vector3I(0, y, 0), kE + new Vector3I(0, y, 0),
                    VoxelMaterialType.ReinforcedSteel);
            }

            // Keep roof (ArmorPlate for top-down protection)
            int keepRoofY = keepH + 1;
            B.AddFloor(plan, t,
                kS + new Vector3I(0, keepRoofY, 0), kE + new Vector3I(0, keepRoofY, 0),
                VoxelMaterialType.ArmorPlate);
        }

        // ── Gatehouse (protruding structure on front wall) ──
        var (_, _, ghUseMinZ, _) = B.GetOutwardEdges(zone);
        int gateW = 4;
        int gateD = 2;
        int gateX = bS.X + (outerW - gateW) / 2;
        int gateZ = ghUseMinZ ? bS.Z - gateD : bE.Z + 1;
        int gateZEnd = ghUseMinZ ? bS.Z : bE.Z + gateD;

        if (gateZ >= o.Z && gateZEnd < o.Z + zD)
        {
            Vector3I gS = new Vector3I(gateX, o.Y, Math.Min(gateZ, gateZEnd));
            Vector3I gE = new Vector3I(gateX + gateW - 1, o.Y, Math.Max(gateZ, gateZEnd));

            B.AddFloor(plan, t, gS, gE, VoxelMaterialType.Stone);
            for (int y = 1; y <= wallH; y++)
            {
                B.AddHollowLayer(plan, t,
                    gS + new Vector3I(0, y, 0), gE + new Vector3I(0, y, 0),
                    VoxelMaterialType.Stone);
            }
            B.AddFloor(plan, t,
                gS + new Vector3I(0, walkwayY, 0), gE + new Vector3I(0, walkwayY, 0),
                VoxelMaterialType.Stone);
        }

        // ── Commander vault (obsidian) in a random quadrant ──
        int quadrant = rng.Next(4);
        int cmdOX, cmdOZ;
        switch (quadrant)
        {
            case 0: cmdOX = outerW / 4; cmdOZ = outerD / 4; break;
            case 1: cmdOX = outerW * 3 / 4; cmdOZ = outerD / 4; break;
            case 2: cmdOX = outerW / 4; cmdOZ = outerD * 3 / 4; break;
            default: cmdOX = outerW * 3 / 4; cmdOZ = outerD * 3 / 4; break;
        }
        cmdOX = Math.Clamp(cmdOX, 4, outerW - 5);
        cmdOZ = Math.Clamp(cmdOZ, 4, outerD - 5);
        Vector3I cmdPos = bS + new Vector3I(cmdOX, 1, cmdOZ);
        plan.CommanderBuildUnit = cmdPos;
        B.AddObsidianVault(plan, t, cmdPos, bS, bE);
        B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.Stone);

        // ── Decoy rooms ──
        B.AddDecoyRooms(plan, t, bS, bE, outerW, outerD, quadrant, VoxelMaterialType.Obsidian, rng);

        // ── Weapons on tower tops and keep roof ──
        string[] weapons = { "railgun", "cannon", "mortar", "missile", "drill" };
        int weaponCount = 5 + rng.Next(3);

        // Place weapons on keep roof first (if keep was built)
        if (keepW >= 5 && keepD >= 5)
        {
            int keepRoofForWeapons = keepH + 1;
            B.PlaceRoofEdgeWeapons(plan, t, kS, kE, keepRoofForWeapons, cmdPos,
                2, new[] { "railgun", "missile" }, rng, zone);
        }

        // Place weapons on curtain wall walkway
        int remaining = weaponCount - plan.WeaponPlacements.Count;
        if (remaining > 0)
        {
            B.PlaceRoofEdgeWeapons(plan, t, bS, bE, walkwayY, cmdPos,
                remaining, weapons, rng, zone);
        }

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, walkwayY, zone);

        return plan;
    }
}
