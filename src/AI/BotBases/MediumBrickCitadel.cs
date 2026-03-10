using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Medium Brick Citadel: 12x12 main building with a 4-wide side wing.
/// Brick walls with obsidian accent band, concrete buttresses at corners.
/// Commander with concrete+metal shell. Penetration loadout (drill + railgun).
/// </summary>
public static class MediumBrickCitadel
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int mainW = Math.Min(12, zW - 6); // leave room for side wing
        int mainD = Math.Min(12, zD - 4);
        int wallH = Math.Min(5, zH - 5);

        int offX = (zW - mainW) / 2;
        int offZ = (zD - mainD) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(mainW - 1, 0, mainD - 1);

        // -- Concrete foundation --
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Concrete);

        // -- Brick walls --
        for (int y = 1; y <= wallH; y++)
        {
            B.AddHollowLayer(plan, t,
                bS + new Vector3I(0, y, 0),
                bE + new Vector3I(0, y, 0),
                VoxelMaterialType.Brick);
        }

        // -- Concrete corner buttresses --
        B.AddCornerColumns(plan, t, bS, bE, 1, wallH, VoxelMaterialType.Concrete);

        // -- Main roof --
        int roofY = wallH + 1;
        B.AddFloor(plan, t,
            bS + new Vector3I(0, roofY, 0),
            bE + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Brick);

        // -- Obsidian accent band at mid-height --
        int accentY = wallH / 2 + 1;
        B.AddHollowLayer(plan, t,
            bS + new Vector3I(0, accentY, 0),
            bE + new Vector3I(0, accentY, 0),
            VoxelMaterialType.Obsidian);

        // -- Crenellations --
        B.AddCrenellations(plan, t, bS, bE, roofY + 1, VoxelMaterialType.Brick);

        // -- Side wing (4 wide, attached to one side) --
        int wingW = 4;
        int wingD = Math.Min(6, mainD - 2);
        bool wingLeft = rng.Next(2) == 0;
        int wingX = wingLeft ? bS.X - wingW : bE.X + 1;
        wingX = Math.Clamp(wingX, o.X + 1, o.X + zW - wingW - 1);
        int wingZ = bS.Z + (mainD - wingD) / 2;
        wingZ = Math.Clamp(wingZ, o.Z + 1, o.Z + zD - wingD - 1);

        Vector3I wS = new Vector3I(wingX, o.Y, wingZ);
        Vector3I wE = wS + new Vector3I(wingW - 1, 0, wingD - 1);

        // Wing foundation
        B.AddFloor(plan, t, wS, wE, VoxelMaterialType.Concrete);

        // Wing walls
        for (int y = 1; y <= wallH; y++)
        {
            B.AddHollowLayer(plan, t,
                wS + new Vector3I(0, y, 0),
                wE + new Vector3I(0, y, 0),
                VoxelMaterialType.Brick);
        }

        // Wing roof
        B.AddFloor(plan, t,
            wS + new Vector3I(0, roofY, 0),
            wE + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Brick);

        // -- Interior dividing wall in main building --
        int divZ = bS.Z + mainD / 2;
        divZ = Math.Clamp(divZ, bS.Z + 2, bE.Z - 2);
        for (int y = 1; y < wallH; y++)
        {
            for (int x = bS.X + 1; x < bE.X; x++)
            {
                B.AddSingle(plan, t, new Vector3I(x, bS.Y + y, divZ), VoxelMaterialType.Concrete);
            }
        }

        // -- Commander inside main building, well inset --
        int cmdOX = mainW / 2 + rng.Next(3) - 1;
        int cmdOZ = mainD / 2 + rng.Next(3) - 1;
        cmdOX = Math.Clamp(cmdOX, 3, mainW - 4);
        cmdOZ = Math.Clamp(cmdOZ, 3, mainD - 4);
        Vector3I cmdPos = bS + new Vector3I(cmdOX, 1, cmdOZ);
        plan.CommanderBuildUnit = cmdPos;

        B.AddCommanderShell(plan, t, cmdPos, bS, bE, VoxelMaterialType.Concrete, 3);
        B.AddCommanderShell(plan, t, cmdPos, bS, bE, VoxelMaterialType.Metal, 5);
        B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.Concrete);

        // -- Weapons: wing gets drill + railgun, main gets cannons (4-5) --
        int weaponCount = 4 + rng.Next(2);

        // Penetration weapons on wing roof (elevated, good firing line)
        int wingWeapons = Math.Min(2, weaponCount);
        B.PlaceRoofEdgeWeapons(plan, t, wS, wE, roofY, cmdPos, wingWeapons,
            new[] { "drill", "railgun" }, rng, zone);

        // Remaining weapons on main roof
        int mainWeapons = weaponCount - plan.WeaponPlacements.Count;
        if (mainWeapons > 0)
        {
            B.PlaceRoofEdgeWeapons(plan, t, bS, bE, roofY, cmdPos, mainWeapons,
                new[] { "cannon", "mortar", "cannon" }, rng, zone);
        }

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, roofY, zone);

        return plan;
    }
}
