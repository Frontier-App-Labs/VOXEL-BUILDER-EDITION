using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Core;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Medium Steel Fortress: 14x16 with concrete foundation, double walls
/// (metal outer + concrete inner). 3 turret housings on front edge.
/// Commander protected by reinforced steel shell. Interior dividing walls.
/// Heavy firepower loadout (railgun + missile + drill on turrets, cannon + mortar fallback).
/// </summary>
public static class MediumSteelFortress
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int mainW = Math.Min(14, zW - 4);
        int mainD = Math.Min(16, zD - 4);
        int wallH = Math.Min(5, zH - 5);

        int offX = (zW - mainW) / 2;
        int offZ = (zD - mainD) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(mainW - 1, 0, mainD - 1);

        // -- Concrete foundation --
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Concrete);

        // -- Metal outer walls --
        for (int y = 1; y <= wallH; y++)
        {
            B.AddHollowLayer(plan, t,
                bS + new Vector3I(0, y, 0),
                bE + new Vector3I(0, y, 0),
                VoxelMaterialType.Metal);
        }

        // -- Concrete inner walls (1 block inset) --
        if (mainW >= 6 && mainD >= 6)
        {
            Vector3I innerS = bS + new Vector3I(1, 0, 1);
            Vector3I innerE = bE - new Vector3I(1, 0, 1);
            for (int y = 1; y <= wallH; y++)
            {
                B.AddHollowLayer(plan, t,
                    innerS + new Vector3I(0, y, 0),
                    innerE + new Vector3I(0, y, 0),
                    VoxelMaterialType.Concrete);
            }
        }

        // -- Double roof (metal + concrete) --
        int roofY = wallH + 1;
        B.AddFloor(plan, t,
            bS + new Vector3I(0, roofY, 0),
            bE + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Metal);
        B.AddFloor(plan, t,
            bS + new Vector3I(0, roofY + 1, 0),
            bE + new Vector3I(0, roofY + 1, 0),
            VoxelMaterialType.Concrete);

        // -- 3 turret housings on front edge (2x2 pillars) --
        var (_, _, sfUseMinZ, _) = B.GetOutwardEdges(zone);
        int frontZ = sfUseMinZ ? bS.Z : bE.Z;
        int inset = sfUseMinZ ? 1 : -1;

        int turretCount = 3;
        int spacing = mainW / (turretCount + 1);
        List<Vector3I> turretTops = new List<Vector3I>();

        for (int i = 0; i < turretCount; i++)
        {
            int tx = bS.X + spacing * (i + 1);
            tx = Math.Clamp(tx, bS.X + 1, bE.X - 2);
            int ty = roofY + 2;

            // 2x2 pillar on outward edge
            B.AddSingle(plan, t, new Vector3I(tx, ty, frontZ), VoxelMaterialType.Metal);
            B.AddSingle(plan, t, new Vector3I(tx + 1, ty, frontZ), VoxelMaterialType.Metal);
            B.AddSingle(plan, t, new Vector3I(tx, ty, frontZ + inset), VoxelMaterialType.Metal);
            B.AddSingle(plan, t, new Vector3I(tx + 1, ty, frontZ + inset), VoxelMaterialType.Metal);

            turretTops.Add(new Vector3I(tx, ty + 1, frontZ));
        }

        // -- Interior dividing walls --
        B.AddInteriorWall(plan, t, bS, bE, mainW, wallH, mainD,
            VoxelMaterialType.Concrete, rng);

        // -- Commander deep inside, away from front --
        int cmdOX = mainW / 2 + rng.Next(3) - 1;
        int cmdOZ = sfUseMinZ ? mainD * 2 / 3 : mainD / 3;
        cmdOX = Math.Clamp(cmdOX, 3, mainW - 4);
        cmdOZ = Math.Clamp(cmdOZ, 3, mainD - 4);
        Vector3I cmdPos = bS + new Vector3I(cmdOX, 1, cmdOZ);
        plan.CommanderBuildUnit = cmdPos;

        B.AddCommanderShell(plan, t, cmdPos, bS, bE, VoxelMaterialType.ReinforcedSteel, 3);
        B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.ReinforcedSteel);

        // -- Weapons on turret tops (heavy firepower loadout) --
        string[] weapons = { "railgun", "missile", "drill" };
        int weaponTarget = 4 + rng.Next(2);
        List<Vector3I> placed = new List<Vector3I>();

        for (int i = 0; i < turretTops.Count && placed.Count < weaponTarget; i++)
        {
            Vector3I wPos = turretTops[i];
            if (wPos.DistanceTo(cmdPos) < GameConfig.MinWeaponCommanderGap) continue;

            string wId = weapons[i % weapons.Length];
            int cost = B.GetWeaponCost(wId);
            bool guaranteed = placed.Count < 3;

            if (guaranteed || t.TrySpend(cost))
            {
                if (guaranteed) t.TrySpend(cost);
                plan.WeaponPlacements.Add((wPos, wId));
                placed.Add(wPos);
            }
        }

        // Extra weapons on roof edge if needed
        int remaining = weaponTarget - placed.Count;
        if (remaining > 0)
        {
            B.PlaceRoofEdgeWeapons(plan, t, bS, bE, roofY + 1, cmdPos,
                remaining, new[] { "cannon", "mortar", "cannon" }, rng, zone);
        }

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, roofY + 1, zone);

        return plan;
    }
}
