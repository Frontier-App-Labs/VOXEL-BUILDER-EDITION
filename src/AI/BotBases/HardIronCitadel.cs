using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Hard base: 14x14 all-metal fortress with layered defense. Outer metal wall
/// (5 tall), 2-block gap, inner reinforced steel wall (5 tall), double armor
/// plate roof over the inner section. Commander in center obsidian vault with
/// decoy rooms. 5-7 weapons on outer wall tops (varied types) and inner wall
/// tops at different heights for overlapping fields of fire. All 5 weapon
/// types represented.
/// </summary>
public static class HardIronCitadel
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int outerW = Math.Min(14, zW - 4);
        int outerD = Math.Min(14, zD - 4);
        int wallH = Math.Min(5, zH - 7);

        int offX = (zW - outerW) / 2;
        int offZ = (zD - outerD) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(outerW - 1, 0, outerD - 1);

        // ── Concrete foundation (full footprint) ──
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Concrete);

        // ── Outer metal walls (5 tall) ──
        for (int y = 1; y <= wallH; y++)
        {
            B.AddHollowLayer(plan, t,
                bS + new Vector3I(0, y, 0), bE + new Vector3I(0, y, 0),
                VoxelMaterialType.Metal);
        }

        // ── Outer wall roof (metal, for outer wall weapons) ──
        int outerRoofY = wallH + 1;
        // Perimeter walkway on outer wall (2 blocks wide)
        B.AddPerimeterWalkway(plan, t, bS, bE, outerRoofY, 2, VoxelMaterialType.Metal);

        // ── Crenellations on outer wall ──
        B.AddCrenellations(plan, t, bS, bE, outerRoofY + 1, VoxelMaterialType.Metal);

        // ── Inner reinforced steel walls (inset by 3 = 2 gap + 1 wall) ──
        int innerInset = 3;
        int innerW = outerW - innerInset * 2;
        int innerD = outerD - innerInset * 2;

        if (innerW >= 6 && innerD >= 6)
        {
            Vector3I iS = bS + new Vector3I(innerInset, 0, innerInset);
            Vector3I iE = bS + new Vector3I(innerInset + innerW - 1, 0, innerInset + innerD - 1);

            // Inner floor
            B.AddFloor(plan, t, iS, iE, VoxelMaterialType.ReinforcedSteel);

            // Inner reinforced steel walls (5 tall)
            for (int y = 1; y <= wallH; y++)
            {
                B.AddHollowLayer(plan, t,
                    iS + new Vector3I(0, y, 0), iE + new Vector3I(0, y, 0),
                    VoxelMaterialType.ReinforcedSteel);
            }

            // ── Armor plate roof over inner section ──
            int innerRoofY = wallH + 1;
            B.AddFloor(plan, t,
                iS + new Vector3I(0, innerRoofY, 0), iE + new Vector3I(0, innerRoofY, 0),
                VoxelMaterialType.ArmorPlate);

            // ── Second armor plate layer for extra protection ──
            B.AddFloor(plan, t,
                iS + new Vector3I(0, innerRoofY + 1, 0), iE + new Vector3I(0, innerRoofY + 1, 0),
                VoxelMaterialType.ArmorPlate);

            // ── Commander in center with obsidian vault ──
            int quadrant = rng.Next(4);
            int cmdOX = innerInset + innerW / 2;
            int cmdOZ = innerInset + innerD / 2;
            // Offset commander to a random quadrant for unpredictability
            switch (quadrant)
            {
                case 0: cmdOX = innerInset + innerW / 4; cmdOZ = innerInset + innerD / 4; break;
                case 1: cmdOX = innerInset + innerW * 3 / 4; cmdOZ = innerInset + innerD / 4; break;
                case 2: cmdOX = innerInset + innerW / 4; cmdOZ = innerInset + innerD * 3 / 4; break;
                default: cmdOX = innerInset + innerW * 3 / 4; cmdOZ = innerInset + innerD * 3 / 4; break;
            }
            cmdOX = Math.Clamp(cmdOX, 4, outerW - 5);
            cmdOZ = Math.Clamp(cmdOZ, 4, outerD - 5);
            Vector3I cmdPos = bS + new Vector3I(cmdOX, 1, cmdOZ);
            plan.CommanderBuildUnit = cmdPos;
            B.AddObsidianVault(plan, t, cmdPos, bS, bE);
            B.EnsureCommanderCeiling(plan, t, cmdPos, iS, iE, VoxelMaterialType.ArmorPlate);

            // ── Decoy rooms ──
            B.AddDecoyRooms(plan, t, iS, iE, innerW, innerD, quadrant, VoxelMaterialType.Obsidian, rng);

            // ── Weapons on outer wall tops (varied types for coverage) ──
            string[] outerWeapons = { "cannon", "mortar", "missile" };
            B.PlaceRoofEdgeWeapons(plan, t, bS, bE, outerRoofY, cmdPos,
                3, outerWeapons, rng, zone);

            // ── Weapons on inner wall tops (premium weapons at higher elevation) ──
            string[] innerWeapons = { "railgun", "drill", "missile", "mortar" };
            B.PlaceRoofEdgeWeapons(plan, t, iS, iE, innerRoofY + 1, cmdPos,
                4, innerWeapons, rng, zone);

            B.EnsureAtLeastOneWeapon(plan, t, bS, bE, outerRoofY, zone);
        }
        else
        {
            // ── Fallback: inner section too small, single-wall design ──
            B.AddFloor(plan, t,
                bS + new Vector3I(0, outerRoofY, 0), bE + new Vector3I(0, outerRoofY, 0),
                VoxelMaterialType.Metal);

            int cmdOX = Math.Clamp(outerW / 2, 3, outerW - 4);
            int cmdOZ = Math.Clamp(outerD / 2, 3, outerD - 4);
            Vector3I cmdPos = bS + new Vector3I(cmdOX, 1, cmdOZ);
            plan.CommanderBuildUnit = cmdPos;
            B.AddObsidianVault(plan, t, cmdPos, bS, bE);
            B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.Metal);

            string[] weapons = { "cannon", "mortar", "railgun", "missile", "drill" };
            B.PlaceRoofEdgeWeapons(plan, t, bS, bE, outerRoofY, cmdPos,
                5 + rng.Next(3), weapons, rng, zone);

            B.EnsureAtLeastOneWeapon(plan, t, bS, bE, outerRoofY, zone);
        }

        return plan;
    }
}
