using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Voxel;
using B = VoxelSiege.AI.BotBuildPlanner;

namespace VoxelSiege.AI.BotBases;

/// <summary>
/// Hard base: Elevated design with 4 stone pillars (14 tall, 3x3 each) at
/// corners of a 14x14 footprint, supporting a massive 14x14 metal platform
/// at Y=12. Commander on the platform in a reinforced steel room with obsidian
/// vault inner layer and ArmorPlate shell. Decoy rooms on the platform.
/// Weapons on platform edges AND ground level for mixed-elevation coverage.
/// Cross-bracing walls between pillars at Y=6 for structural reinforcement.
/// All 5 weapon types with no duplicates. 6-8 weapons total.
/// </summary>
public static class HardSkyFortress
{
    public static BotBuildPlan Build(BuildZone zone, int budget, Random rng)
    {
        var plan = new BotBuildPlan();
        var t = new B.BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int footprint = Math.Min(14, Math.Min(zW - 4, zD - 4));
        int pillarH = Math.Min(14, zH - 6);
        int platformY = Math.Min(12, pillarH - 2);
        int pillarSize = 3;
        int braceY = platformY / 2;

        int offX = (zW - footprint) / 2;
        int offZ = (zD - footprint) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(footprint - 1, 0, footprint - 1);

        // ── Ground-level foundation slab ──
        B.AddFloor(plan, t, bS, bE, VoxelMaterialType.Stone);

        // ── 4 corner pillars (3x3 each, from ground to platform) ──
        Vector3I[] pillarOrigins =
        {
            bS,
            new Vector3I(bE.X - pillarSize + 1, bS.Y, bS.Z),
            new Vector3I(bS.X, bS.Y, bE.Z - pillarSize + 1),
            new Vector3I(bE.X - pillarSize + 1, bS.Y, bE.Z - pillarSize + 1),
        };

        foreach (Vector3I pOrigin in pillarOrigins)
        {
            Vector3I pEnd = pOrigin + new Vector3I(pillarSize - 1, 0, pillarSize - 1);

            for (int y = 1; y <= platformY; y++)
            {
                B.AddFloor(plan, t,
                    pOrigin + new Vector3I(0, y, 0),
                    pEnd + new Vector3I(0, y, 0),
                    VoxelMaterialType.Stone);
            }
        }

        // ── Cross-bracing walls between pillars at mid-height ──
        // X-axis braces (front and back)
        for (int x = bS.X + pillarSize; x <= bE.X - pillarSize; x++)
        {
            B.AddSingle(plan, t, new Vector3I(x, braceY, bS.Z + 1), VoxelMaterialType.Metal);
            B.AddSingle(plan, t, new Vector3I(x, braceY + 1, bS.Z + 1), VoxelMaterialType.Metal);
            B.AddSingle(plan, t, new Vector3I(x, braceY, bE.Z - 1), VoxelMaterialType.Metal);
            B.AddSingle(plan, t, new Vector3I(x, braceY + 1, bE.Z - 1), VoxelMaterialType.Metal);
        }
        // Z-axis braces (left and right)
        for (int z = bS.Z + pillarSize; z <= bE.Z - pillarSize; z++)
        {
            B.AddSingle(plan, t, new Vector3I(bS.X + 1, braceY, z), VoxelMaterialType.Metal);
            B.AddSingle(plan, t, new Vector3I(bS.X + 1, braceY + 1, z), VoxelMaterialType.Metal);
            B.AddSingle(plan, t, new Vector3I(bE.X - 1, braceY, z), VoxelMaterialType.Metal);
            B.AddSingle(plan, t, new Vector3I(bE.X - 1, braceY + 1, z), VoxelMaterialType.Metal);
        }

        // ── Massive elevated platform (14x14 metal at platformY) ──
        B.AddFloor(plan, t,
            bS + new Vector3I(0, platformY, 0),
            bE + new Vector3I(0, platformY, 0),
            VoxelMaterialType.Metal);

        // ── Reinforced steel commander room on the platform ──
        int roomH = 3;
        int roomW = Math.Min(6, footprint - 4);
        int roomD = Math.Min(6, footprint - 4);
        int roomOffX = (footprint - roomW) / 2;
        int roomOffZ = (footprint - roomD) / 2;

        Vector3I rS = bS + new Vector3I(roomOffX, platformY, roomOffZ);
        Vector3I rE = rS + new Vector3I(roomW - 1, 0, roomD - 1);

        for (int y = 1; y <= roomH; y++)
        {
            B.AddHollowLayer(plan, t,
                rS + new Vector3I(0, y, 0), rE + new Vector3I(0, y, 0),
                VoxelMaterialType.ReinforcedSteel);
        }

        // Room roof
        B.AddFloor(plan, t,
            rS + new Vector3I(0, roomH + 1, 0), rE + new Vector3I(0, roomH + 1, 0),
            VoxelMaterialType.ReinforcedSteel);

        // ── Commander inside the elevated room with obsidian vault ──
        int cmdX = roomOffX + Math.Clamp(roomW / 2, 1, roomW - 2);
        int cmdZ = roomOffZ + Math.Clamp(roomD / 2, 1, roomD - 2);
        Vector3I cmdPos = bS + new Vector3I(cmdX, platformY + 1, cmdZ);
        plan.CommanderBuildUnit = cmdPos;

        // Obsidian vault innermost layer
        B.AddObsidianVault(plan, t, cmdPos, rS, rE);
        // ArmorPlate shell around the vault
        B.AddCommanderShell(plan, t, cmdPos, rS, rE, VoxelMaterialType.ArmorPlate, 3);
        B.EnsureCommanderCeiling(plan, t, cmdPos, rS, rE, VoxelMaterialType.ReinforcedSteel);

        // ── Decoy rooms on the platform ──
        B.AddDecoyRooms(plan, t, rS, rE, roomW, roomD, rng.Next(4), VoxelMaterialType.Obsidian, rng);

        // ── Weapons: mixed elevation (platform edges + ground level) ──
        // All 5 weapon types represented, no duplicates
        string[] platformWeapons = { "railgun", "missile", "drill", "mortar" };

        // 4 weapons on platform edges (high elevation)
        int platformRoofY = platformY; // platform itself is the "roof" for PlaceRoofEdgeWeapons
        B.PlaceRoofEdgeWeapons(plan, t, bS, bE, platformRoofY, cmdPos,
            4, platformWeapons, rng, zone);

        // 2-3 weapons at ground level (low elevation, different types)
        int groundRoofY = 0;
        B.PlaceRoofEdgeWeapons(plan, t, bS, bE, groundRoofY, cmdPos,
            2 + rng.Next(2), new[] { "cannon", "mortar", "missile" }, rng, zone);

        B.EnsureAtLeastOneWeapon(plan, t, bS, bE, platformRoofY, zone);

        return plan;
    }
}
