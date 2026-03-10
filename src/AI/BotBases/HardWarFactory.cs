using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Core;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Hard base: 16x14 industrial compound with reinforced steel outer walls (5 tall),
/// metal inner layer, 3 weapon platforms on the front edge at staggered heights
/// (3-4-3), armor plate front face. Commander hidden in obsidian vault with decoy
/// rooms. Interior dividing walls for structural compartmentalization. 5-7 weapons
/// on platforms and roof edges.
/// </summary>
public static class HardWarFactory
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int mainW = Math.Min(16, zW - 4);
        int mainD = Math.Min(14, zD - 4);
        int wallH = Math.Min(5, zH - 7);

        int offX = (zW - mainW) / 2;
        int offZ = (zD - mainD) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(mainW - 1, 0, mainD - 1);

        // ── Concrete foundation ──
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Concrete);

        // ── Reinforced steel outer walls ──
        for (int y = 1; y <= wallH; y++)
        {
            B.AddHollowLayer(plan, t,
                bS + new Vector3I(0, y, 0), bE + new Vector3I(0, y, 0),
                VoxelMaterialType.ReinforcedSteel);
        }

        // ── Metal inner walls ──
        if (mainW >= 8 && mainD >= 8)
        {
            Vector3I innerS = bS + new Vector3I(1, 0, 1);
            Vector3I innerE = bE - new Vector3I(1, 0, 1);
            for (int y = 1; y <= wallH; y++)
            {
                B.AddHollowLayer(plan, t,
                    innerS + new Vector3I(0, y, 0), innerE + new Vector3I(0, y, 0),
                    VoxelMaterialType.Metal);
            }
        }

        // ── Double roof ──
        int roofY = wallH + 1;
        B.AddFloor(plan, t,
            bS + new Vector3I(0, roofY, 0), bE + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Metal);
        B.AddFloor(plan, t,
            bS + new Vector3I(0, roofY + 1, 0), bE + new Vector3I(0, roofY + 1, 0),
            VoxelMaterialType.ReinforcedSteel);

        // ── Weapon platform towers on the outward-facing Z edge ──
        var (_, _, wfUseMinZ, _) = B.GetOutwardEdges(zone);
        int wfFrontZ = wfUseMinZ ? bS.Z : bE.Z;
        int wfInset = wfUseMinZ ? 1 : -1;

        int platformCount = 3;
        int spacing = mainW / (platformCount + 1);
        int[] platformHeights = { 3, 4, 3 };
        List<Vector3I> platformTops = new List<Vector3I>();

        for (int i = 0; i < platformCount; i++)
        {
            int px = bS.X + spacing * (i + 1);
            px = Math.Clamp(px, bS.X + 2, bE.X - 2);
            int ph = platformHeights[i % platformHeights.Length];
            int baseY = roofY + 2;
            int topY = baseY + ph - 1;

            if (topY >= o.Y + zH - 2) continue;

            // Build pillar from roof up
            for (int y = baseY; y <= topY; y++)
            {
                B.AddSingle(plan, t, new Vector3I(px, y, wfFrontZ + wfInset), VoxelMaterialType.Metal);
                B.AddSingle(plan, t, new Vector3I(px + 1, y, wfFrontZ + wfInset), VoxelMaterialType.Metal);
            }

            // Platform cap (3x3 on top of pillar)
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = 0; dz <= 2; dz++)
                {
                    B.AddSingle(plan, t,
                        new Vector3I(px + dx, topY + 1, wfFrontZ + dz * wfInset),
                        VoxelMaterialType.ReinforcedSteel);
                }
            }

            platformTops.Add(new Vector3I(px, topY + 2, wfFrontZ));
        }

        // ── Armor plate on front face ──
        for (int y = 1; y <= wallH; y++)
        {
            for (int x = bS.X; x <= bE.X; x++)
            {
                B.AddSingle(plan, t, new Vector3I(x, y, wfFrontZ), VoxelMaterialType.ArmorPlate);
            }
        }

        // ── Commander vault deep inside ──
        int quadrant = rng.Next(4);
        int cmdOX, cmdOZ;
        switch (quadrant)
        {
            case 0: cmdOX = mainW / 4; cmdOZ = mainD / 4; break;
            case 1: cmdOX = mainW * 3 / 4; cmdOZ = mainD / 4; break;
            case 2: cmdOX = mainW / 4; cmdOZ = mainD * 3 / 4; break;
            default: cmdOX = mainW * 3 / 4; cmdOZ = mainD * 3 / 4; break;
        }
        cmdOX = Math.Clamp(cmdOX, 4, mainW - 5);
        cmdOZ = Math.Clamp(cmdOZ, 4, mainD - 5);
        Vector3I cmdPos = bS + new Vector3I(cmdOX, 1, cmdOZ);
        plan.CommanderBuildUnit = cmdPos;
        B.AddObsidianVault(plan, t, cmdPos, bS, bE);
        B.EnsureCommanderCeiling(plan, t, cmdPos, bS, bE, VoxelMaterialType.ReinforcedSteel);

        // ── Decoy rooms ──
        B.AddDecoyRooms(plan, t, bS, bE, mainW, mainD, quadrant, VoxelMaterialType.ReinforcedSteel, rng);

        // ── Interior dividing walls ──
        B.AddInteriorWall(plan, t, bS, bE, mainW, wallH, mainD, VoxelMaterialType.Metal, rng);
        B.AddInteriorWall(plan, t, bS, bE, mainW, wallH, mainD, VoxelMaterialType.Concrete, rng);

        // ── Weapons on platforms ──
        string[] weapons = { "cannon", "railgun", "missile", "drill", "mortar" };
        int weaponCount = 5 + rng.Next(3);

        for (int i = 0; i < platformTops.Count && plan.WeaponPlacements.Count < weaponCount; i++)
        {
            Vector3I wPos = platformTops[i];
            if (wPos.DistanceTo(cmdPos) < GameConfig.MinWeaponCommanderGap) continue;

            string wId = weapons[i % weapons.Length];
            int cost = B.GetWeaponCost(wId);
            bool guaranteed = plan.WeaponPlacements.Count < 3;

            if (guaranteed || t.TrySpend(cost))
            {
                if (guaranteed) t.TrySpend(cost);
                plan.WeaponPlacements.Add((wPos, wId));
            }
        }

        // ── Extra weapons on roof edges ──
        int extra = weaponCount - plan.WeaponPlacements.Count;
        if (extra > 0)
        {
            B.PlaceRoofEdgeWeapons(plan, t, bS, bE, roofY + 1, cmdPos,
                extra, new[] { "mortar", "cannon", "railgun" }, rng, zone);
        }

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, roofY + 1, zone);

        return plan;
    }
}
