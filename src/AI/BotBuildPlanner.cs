using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.AI.BotBases;
using VoxelSiege.Building;
using VoxelSiege.Core;
using VoxelSiege.Voxel;

namespace VoxelSiege.AI;

public enum BotDifficulty
{
    Easy,
    Medium,
    Hard,
}

/// <summary>
/// Represents a single build operation the bot wants to execute.
/// Maps directly to the existing build tool modes that GameManager uses.
/// </summary>
public sealed class PlannedBuildAction
{
    public BuildToolMode ToolMode { get; set; }
    public VoxelMaterialType Material { get; set; }
    public Vector3I Start { get; set; }
    public Vector3I End { get; set; }
    public bool Hollow { get; set; }
}

/// <summary>
/// Result of a build plan, containing all block placements, commander position,
/// and weapon positions with their types.
/// </summary>
public sealed class BotBuildPlan
{
    public List<PlannedBuildAction> Actions { get; } = new List<PlannedBuildAction>();
    public Vector3I CommanderBuildUnit { get; set; }
    public List<(Vector3I Position, string WeaponId)> WeaponPlacements { get; } = new List<(Vector3I, string)>();
}

/// <summary>
/// Generates fortress designs for bot players based on difficulty level.
///
/// Design philosophy:
///   - Every block is built bottom-up so nothing floats.
///   - Weapons sit on the outer edge of rooftops / tower tops with a clear
///     outward line of fire (nothing between the weapon and the zone border).
///   - Commander is always enclosed inside the structure.
///   - 3 visual styles per difficulty, chosen at random.
///
/// Easy  (3-4 weapons): Stockade, Watchtower, Bunker
/// Medium (4-5 weapons): Castle Keep, Steel Fortress, Brick Citadel
/// Hard   (5-7 weapons): Grand Castle, War Factory, Mountain Stronghold
/// </summary>
public sealed class BotBuildPlanner
{
    // ─────────────────────────────────────────────────
    //  PUBLIC API
    // ─────────────────────────────────────────────────

    public List<PlannedBuildAction> CreatePlan(BotDifficulty difficulty, BuildZone zone)
    {
        BotBuildPlan plan = CreateFullPlan(difficulty, zone, GameConfig.DefaultBudget);
        return plan.Actions;
    }

    public BotBuildPlan CreateFullPlan(BotDifficulty difficulty, BuildZone zone, int budget)
    {
        Random rng = new Random(System.Environment.TickCount ^ zone.OriginBuildUnits.GetHashCode());

        // Use the BotBaseRegistry to pick from all registered base designs
        BotBaseBuilder builder = BotBaseRegistry.GetRandom(difficulty, rng);
        return builder(zone, budget, rng);
    }

    // ═══════════════════════════════════════════════════════════════
    //  EASY FORTRESS — 3 styles  (3-4 weapons)
    // ═══════════════════════════════════════════════════════════════

    private static BotBuildPlan PlanEasyFortress(BuildZone zone, int budget, Random rng)
    {
        int style = rng.Next(3);
        return style switch
        {
            0 => PlanEasyStockade(zone, budget, rng),
            1 => PlanEasyWatchtower(zone, budget, rng),
            _ => PlanEasyBunker(zone, budget, rng),
        };
    }

    // ─────────────────────────────────────────────────
    //  Easy Style 1: Stockade
    //  Simple rectangular compound: stone foundation, wood walls,
    //  flat roof. Weapons on the outer edge of the roof facing outward.
    // ─────────────────────────────────────────────────

    private static BotBuildPlan PlanEasyStockade(BuildZone zone, int budget, Random rng)
    {
        BotBuildPlan plan = new BotBuildPlan();
        BudgetTracker tracker = new BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zD = zone.SizeBuildUnits.Z;

        int width = Math.Min(10, zW - 4);   // leave 2 padding each side
        int depth = Math.Min(10, zD - 4);
        int wallH = 4;

        int offX = (zW - width) / 2;
        int offZ = (zD - depth) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);    // base start (build units)
        Vector3I bE = bS + new Vector3I(width - 1, 0, depth - 1);

        // ── Layer 0: stone foundation slab ──
        AddFloor(plan, tracker, bS, bE, VoxelMaterialType.Stone);

        // ── Layers 1..wallH-1: wood walls (hollow box) ──
        for (int y = 1; y < wallH; y++)
        {
            Vector3I layerS = bS + new Vector3I(0, y, 0);
            Vector3I layerE = bE + new Vector3I(0, y, 0);
            AddHollowLayer(plan, tracker, layerS, layerE, VoxelMaterialType.Wood);
        }

        // ── Roof: solid wood floor at top of walls ──
        int roofY = wallH;
        Vector3I roofS = bS + new Vector3I(0, roofY, 0);
        Vector3I roofE = bE + new Vector3I(0, roofY, 0);
        AddFloor(plan, tracker, roofS, roofE, VoxelMaterialType.Wood);

        // ── Bark corner columns for visual reinforcement ──
        AddCornerColumns(plan, tracker, bS, bE, 1, wallH - 1, VoxelMaterialType.Bark);

        // ── Commander: center of the interior, on the floor ──
        int cmdX = Math.Clamp(width / 2, 2, width - 3);
        int cmdZ = Math.Clamp(depth / 2, 2, depth - 3);
        Vector3I cmdPos = bS + new Vector3I(cmdX, 1, cmdZ);
        plan.CommanderBuildUnit = cmdPos;

        // Inner stone walls around commander (3x3 hollow box, 3 tall)
        AddCommanderShell(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Stone, 3);
        EnsureCommanderCeiling(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Stone);

        // ── Weapons: on outer edges of the roof ──
        PlaceRoofEdgeWeapons(plan, tracker, bS, bE, roofY, cmdPos, 3 + rng.Next(2),
            new[] { "cannon", "mortar", "cannon" }, rng, zone);

        return plan;
    }

    // ─────────────────────────────────────────────────
    //  Easy Style 2: Watchtower
    //  Tall square tower with crenellations, weapons on top
    // ─────────────────────────────────────────────────

    private static BotBuildPlan PlanEasyWatchtower(BuildZone zone, int budget, Random rng)
    {
        BotBuildPlan plan = new BotBuildPlan();
        BudgetTracker tracker = new BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int side = Math.Min(8, Math.Min(zW - 4, zD - 4));
        int height = Math.Min(7, zH - 4);

        int offX = (zW - side) / 2;
        int offZ = (zD - side) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(side - 1, 0, side - 1);

        // ── Foundation ──
        AddFloor(plan, tracker, bS, bE, VoxelMaterialType.Stone);

        // ── Stone walls layer by layer ──
        for (int y = 1; y < height; y++)
        {
            Vector3I layerS = bS + new Vector3I(0, y, 0);
            Vector3I layerE = bE + new Vector3I(0, y, 0);
            AddHollowLayer(plan, tracker, layerS, layerE, VoxelMaterialType.Stone);
        }

        // ── Brick accent band at mid-height ──
        int bandY = height / 2;
        {
            Vector3I layerS = bS + new Vector3I(0, bandY, 0);
            Vector3I layerE = bE + new Vector3I(0, bandY, 0);
            AddHollowLayer(plan, tracker, layerS, layerE, VoxelMaterialType.Brick);
        }

        // ── Roof slab ──
        int roofY = height;
        Vector3I roofS = bS + new Vector3I(0, roofY, 0);
        Vector3I roofE = bE + new Vector3I(0, roofY, 0);
        AddFloor(plan, tracker, roofS, roofE, VoxelMaterialType.Stone);

        // ── Crenellations on roof (supported by the roof slab) ──
        AddCrenellations(plan, tracker, bS, bE, roofY + 1, VoxelMaterialType.Stone);

        // ── Commander inside ──
        int cmdSide = Math.Clamp(side / 2, 2, side - 3);
        Vector3I cmdPos = bS + new Vector3I(cmdSide, 1, cmdSide);
        plan.CommanderBuildUnit = cmdPos;
        AddCommanderShell(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Stone, 3);
        EnsureCommanderCeiling(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Stone);

        // ── Weapons on roof corners (clear line of fire outward) ──
        PlaceRoofEdgeWeapons(plan, tracker, bS, bE, roofY, cmdPos, 3 + rng.Next(2),
            new[] { "cannon", "mortar", "railgun" }, rng, zone);

        return plan;
    }

    // ─────────────────────────────────────────────────
    //  Easy Style 3: Bunker
    //  Wide, low-profile dirt/sand structure. Weapons on the flat roof.
    // ─────────────────────────────────────────────────

    private static BotBuildPlan PlanEasyBunker(BuildZone zone, int budget, Random rng)
    {
        BotBuildPlan plan = new BotBuildPlan();
        BudgetTracker tracker = new BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zD = zone.SizeBuildUnits.Z;

        int width = Math.Min(12, zW - 4);
        int depth = Math.Min(12, zD - 4);
        int wallH = 3;

        int offX = (zW - width) / 2;
        int offZ = (zD - depth) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(width - 1, 0, depth - 1);

        // ── Stone foundation ──
        AddFloor(plan, tracker, bS, bE, VoxelMaterialType.Stone);

        // ── Dirt walls ──
        for (int y = 1; y < wallH; y++)
        {
            Vector3I layerS = bS + new Vector3I(0, y, 0);
            Vector3I layerE = bE + new Vector3I(0, y, 0);
            AddHollowLayer(plan, tracker, layerS, layerE, VoxelMaterialType.Dirt);
        }

        // ── Sand roof ──
        int roofY = wallH;
        Vector3I roofS = bS + new Vector3I(0, roofY, 0);
        Vector3I roofE = bE + new Vector3I(0, roofY, 0);
        AddFloor(plan, tracker, roofS, roofE, VoxelMaterialType.Sand);

        // ── Commander center ──
        int cmdBX = Math.Clamp(width / 2, 2, width - 3);
        int cmdBZ = Math.Clamp(depth / 2, 2, depth - 3);
        Vector3I cmdPos = bS + new Vector3I(cmdBX, 1, cmdBZ);
        plan.CommanderBuildUnit = cmdPos;
        AddCommanderShell(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Stone, 3);
        EnsureCommanderCeiling(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Stone);

        // ── Weapons on roof edges ──
        PlaceRoofEdgeWeapons(plan, tracker, bS, bE, roofY, cmdPos, 3 + rng.Next(2),
            new[] { "cannon", "mortar", "cannon" }, rng, zone);

        return plan;
    }

    // ═══════════════════════════════════════════════════════════════
    //  MEDIUM FORTRESS — 3 styles  (4-5 weapons)
    // ═══════════════════════════════════════════════════════════════

    private static BotBuildPlan PlanMediumFortress(BuildZone zone, int budget, Random rng)
    {
        int style = rng.Next(3);
        return style switch
        {
            0 => PlanMediumCastleKeep(zone, budget, rng),
            1 => PlanMediumSteelFortress(zone, budget, rng),
            _ => PlanMediumBrickCitadel(zone, budget, rng),
        };
    }

    // ─────────────────────────────────────────────────
    //  Medium Style 1: Castle Keep
    //  Stone walls, corner towers (taller), crenellations,
    //  weapons on tower tops with clear outward fire.
    // ─────────────────────────────────────────────────

    private static BotBuildPlan PlanMediumCastleKeep(BuildZone zone, int budget, Random rng)
    {
        BotBuildPlan plan = new BotBuildPlan();
        BudgetTracker tracker = new BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int mainW = Math.Min(14, zW - 4);
        int mainD = Math.Min(14, zD - 4);
        int wallH = Math.Min(5, zH - 6);
        int towerH = Math.Min(wallH + 3, zH - 3);
        int towerSize = 3;

        int offX = (zW - mainW) / 2;
        int offZ = (zD - mainD) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(mainW - 1, 0, mainD - 1);

        // ── Foundation slab ──
        AddFloor(plan, tracker, bS, bE, VoxelMaterialType.Stone);

        // ── Main walls (stone) ──
        for (int y = 1; y <= wallH; y++)
        {
            Vector3I layerS = bS + new Vector3I(0, y, 0);
            Vector3I layerE = bE + new Vector3I(0, y, 0);
            AddHollowLayer(plan, tracker, layerS, layerE, VoxelMaterialType.Stone);
        }

        // ── Brick accent band at mid-height ──
        int bandY = wallH / 2 + 1;
        {
            Vector3I layerS = bS + new Vector3I(0, bandY, 0);
            Vector3I layerE = bE + new Vector3I(0, bandY, 0);
            AddHollowLayer(plan, tracker, layerS, layerE, VoxelMaterialType.Brick);
        }

        // ── Main roof ──
        int roofY = wallH + 1;
        Vector3I roofS = bS + new Vector3I(0, roofY, 0);
        Vector3I roofE = bE + new Vector3I(0, roofY, 0);
        AddFloor(plan, tracker, roofS, roofE, VoxelMaterialType.Stone);

        // ── Crenellations on main walls ──
        AddCrenellations(plan, tracker, bS, bE, roofY + 1, VoxelMaterialType.Stone);

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

            // Tower walls above main wall height (main wall already built the lower portion)
            for (int y = wallH + 1; y < towerH; y++)
            {
                Vector3I layerS = tOrigin + new Vector3I(0, y, 0);
                Vector3I layerE = tEnd + new Vector3I(0, y, 0);
                AddHollowLayer(plan, tracker, layerS, layerE, VoxelMaterialType.Stone);
            }

            // Tower roof
            int tRoofY = towerH;
            Vector3I trS = tOrigin + new Vector3I(0, tRoofY, 0);
            Vector3I trE = tEnd + new Vector3I(0, tRoofY, 0);
            AddFloor(plan, tracker, trS, trE, VoxelMaterialType.Stone);

            // Tower crenellations
            AddCrenellations(plan, tracker, tOrigin, tEnd, tRoofY + 1, VoxelMaterialType.Stone);
        }

        // ── Commander inside, slightly off-center but well inset from walls ──
        int cmdOX = mainW / 2 + rng.Next(3) - 1;
        int cmdOZ = mainD / 2 + rng.Next(3) - 1;
        cmdOX = Math.Clamp(cmdOX, 3, mainW - 4);
        cmdOZ = Math.Clamp(cmdOZ, 3, mainD - 4);
        Vector3I cmdPos = bS + new Vector3I(cmdOX, 1, cmdOZ);
        plan.CommanderBuildUnit = cmdPos;
        AddCommanderShell(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Concrete, 3);
        AddCommanderShell(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Metal, 5);
        EnsureCommanderCeiling(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Concrete);

        // ── Weapons on tower tops (only on outward-facing towers) ──
        var (ckUseMinX, ckUseMaxX, ckUseMinZ, ckUseMaxZ) = GetOutwardEdges(zone);
        int fortCenterX = (bS.X + bE.X) / 2;
        int fortCenterZ = (bS.Z + bE.Z) / 2;

        string[] weapons = { "cannon", "mortar", "railgun", "cannon" };
        int weaponCount = 4 + rng.Next(2);
        List<Vector3I> placed = new List<Vector3I>();
        HashSet<Vector3I> plannedBlocks = BuildPlannedBlocksSet(plan);

        const int guaranteedWeapons = 3;

        for (int i = 0; i < towerOrigins.Length && placed.Count < weaponCount; i++)
        {
            Vector3I tOrigin = towerOrigins[i];
            int tCenterX = tOrigin.X + (towerSize - 1) / 2;
            int tCenterZ = tOrigin.Z + (towerSize - 1) / 2;

            // Skip towers that are entirely on inward-facing edges
            bool onOutward = false;
            if (tCenterX < fortCenterX && ckUseMinX) onOutward = true;
            if (tCenterX >= fortCenterX && ckUseMaxX) onOutward = true;
            if (tCenterZ < fortCenterZ && ckUseMinZ) onOutward = true;
            if (tCenterZ >= fortCenterZ && ckUseMaxZ) onOutward = true;
            if (!onOutward) continue;

            // Place weapon on the outer corner of each tower (furthest from center)
            Vector3I wPos = GetOuterCorner(tOrigin, tOrigin + new Vector3I(towerSize - 1, 0, towerSize - 1),
                bS, bE, towerH + 1);

            if (wPos.DistanceTo(cmdPos) < GameConfig.MinWeaponCommanderGap) continue;
            // Skip if weapon/pillar overlaps with fortress structure
            if (WeaponOverlapsBlocks(wPos, wPos.Y - 4, plannedBlocks)) continue;

            string wId = weapons[i % weapons.Length];
            int cost = GetWeaponCost(wId);
            bool guaranteed = placed.Count < guaranteedWeapons;

            if (guaranteed || tracker.TrySpend(cost))
            {
                if (guaranteed) tracker.TrySpend(cost); // best-effort

                // Build a 3-block tall stone pillar under the weapon so it
                // clears crenellations and all rooftop structures.
                for (int py = wPos.Y - 3; py <= wPos.Y - 1; py++)
                {
                    Vector3I pillarPos = new Vector3I(wPos.X, py, wPos.Z);
                    plan.Actions.Add(new PlannedBuildAction
                    {
                        ToolMode = BuildToolMode.Single,
                        Material = VoxelMaterialType.Stone,
                        Start = pillarPos,
                        End = pillarPos,
                        Hollow = false,
                    });
                    plannedBlocks.Add(pillarPos);
                    tracker.TrySpend(VoxelMaterials.GetDefinition(VoxelMaterialType.Stone).Cost);
                }
                plan.WeaponPlacements.Add((wPos, wId));
                placed.Add(wPos);
            }
        }

        // Extra weapon on main roof edge if budget allows
        if (placed.Count < weaponCount)
        {
            PlaceRoofEdgeWeapons(plan, tracker, bS, bE, roofY, cmdPos,
                weaponCount - placed.Count, new[] { "mortar", "cannon" }, rng, zone);
        }

        EnsureAtLeastOneWeapon(plan, tracker, bS, bE, roofY, zone);

        return plan;
    }

    // ─────────────────────────────────────────────────
    //  Medium Style 2: Steel Fortress
    //  Low-profile metal fortress, double walls, turret platforms
    // ─────────────────────────────────────────────────

    private static BotBuildPlan PlanMediumSteelFortress(BuildZone zone, int budget, Random rng)
    {
        BotBuildPlan plan = new BotBuildPlan();
        BudgetTracker tracker = new BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int mainW = Math.Min(14, zW - 4);
        int mainD = Math.Min(12, zD - 4);
        int wallH = Math.Min(4, zH - 5);

        int offX = (zW - mainW) / 2;
        int offZ = (zD - mainD) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(mainW - 1, 0, mainD - 1);

        // ── Concrete foundation ──
        AddFloor(plan, tracker, bS, bE, VoxelMaterialType.Concrete);

        // ── Metal outer walls ──
        for (int y = 1; y <= wallH; y++)
        {
            AddHollowLayer(plan, tracker,
                bS + new Vector3I(0, y, 0), bE + new Vector3I(0, y, 0),
                VoxelMaterialType.Metal);
        }

        // ── Inner concrete reinforcement (1 block inset) ──
        if (mainW >= 6 && mainD >= 6)
        {
            Vector3I innerS = bS + new Vector3I(1, 0, 1);
            Vector3I innerE = bE - new Vector3I(1, 0, 1);
            for (int y = 1; y <= wallH; y++)
            {
                AddHollowLayer(plan, tracker,
                    innerS + new Vector3I(0, y, 0), innerE + new Vector3I(0, y, 0),
                    VoxelMaterialType.Concrete);
            }
        }

        // ── Double roof ──
        int roofY = wallH + 1;
        AddFloor(plan, tracker,
            bS + new Vector3I(0, roofY, 0), bE + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Metal);
        AddFloor(plan, tracker,
            bS + new Vector3I(0, roofY + 1, 0), bE + new Vector3I(0, roofY + 1, 0),
            VoxelMaterialType.Concrete);

        // ── Turret housings on roof (small 2x2 pillars, one block tall) ──
        // Place turrets on the outward-facing Z edge
        var (_, _, sfUseMinZ, _) = GetOutwardEdges(zone);
        int sfFrontZ = sfUseMinZ ? bS.Z : bE.Z;
        int sfInset = sfUseMinZ ? 1 : -1; // direction toward interior

        int turretCount = 3;
        int spacing = mainW / (turretCount + 1);
        List<Vector3I> turretTops = new List<Vector3I>();

        for (int i = 0; i < turretCount; i++)
        {
            int tx = bS.X + spacing * (i + 1);
            tx = Math.Clamp(tx, bS.X + 1, bE.X - 1);
            int ty = roofY + 2;

            // Turret pillar on the outward-facing edge
            AddSingle(plan, tracker, new Vector3I(tx, ty, sfFrontZ), VoxelMaterialType.Metal);
            AddSingle(plan, tracker, new Vector3I(tx + 1, ty, sfFrontZ), VoxelMaterialType.Metal);
            AddSingle(plan, tracker, new Vector3I(tx, ty, sfFrontZ + sfInset), VoxelMaterialType.Metal);
            AddSingle(plan, tracker, new Vector3I(tx + 1, ty, sfFrontZ + sfInset), VoxelMaterialType.Metal);

            // Weapon sits on the front edge of turret
            turretTops.Add(new Vector3I(tx, ty + 1, sfFrontZ));
        }

        // ── Commander deep inside, toward the back (away from front edge) ──
        int cmdOX = mainW / 2 + rng.Next(3) - 1;
        int cmdOZ = sfUseMinZ ? mainD * 2 / 3 : mainD / 3;
        cmdOX = Math.Clamp(cmdOX, 3, mainW - 4);
        cmdOZ = Math.Clamp(cmdOZ, 3, mainD - 4);
        Vector3I cmdPos = bS + new Vector3I(cmdOX, 1, cmdOZ);
        plan.CommanderBuildUnit = cmdPos;
        AddCommanderShell(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.ReinforcedSteel, 3);
        EnsureCommanderCeiling(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.ReinforcedSteel);

        // ── Weapons on turret tops (facing outward from front edge) ──
        string[] weapons = { "cannon", "railgun", "mortar" };
        const int guaranteedWeaponsSF = 3;
        HashSet<Vector3I> sfPlannedBlocks = BuildPlannedBlocksSet(plan);
        List<Vector3I> wpPlaced = new List<Vector3I>();
        for (int i = 0; i < turretTops.Count; i++)
        {
            Vector3I wPos = turretTops[i];
            if (wPos.DistanceTo(cmdPos) < GameConfig.MinWeaponCommanderGap) continue;
            if (sfPlannedBlocks.Contains(wPos)) continue;

            string wId = weapons[i % weapons.Length];
            int cost = GetWeaponCost(wId);
            bool guaranteed = wpPlaced.Count < guaranteedWeaponsSF;

            if (guaranteed || tracker.TrySpend(cost))
            {
                if (guaranteed) tracker.TrySpend(cost); // best-effort

                plan.WeaponPlacements.Add((wPos, wId));
                wpPlaced.Add(wPos);
            }
        }

        // Extra weapons on back roof edge
        int desired = 4 + rng.Next(2) - wpPlaced.Count;
        if (desired > 0)
        {
            PlaceRoofEdgeWeapons(plan, tracker, bS, bE, roofY + 1, cmdPos,
                desired, new[] { "mortar", "cannon" }, rng, zone);
        }

        EnsureAtLeastOneWeapon(plan, tracker, bS, bE, roofY + 1, zone);

        return plan;
    }

    // ─────────────────────────────────────────────────
    //  Medium Style 3: Brick Citadel
    //  Multi-room brick building, concrete buttresses, side wing
    // ─────────────────────────────────────────────────

    private static BotBuildPlan PlanMediumBrickCitadel(BuildZone zone, int budget, Random rng)
    {
        BotBuildPlan plan = new BotBuildPlan();
        BudgetTracker tracker = new BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        int mainW = Math.Min(12, zW - 6);  // leave room for side wing
        int mainD = Math.Min(12, zD - 4);
        int wallH = Math.Min(5, zH - 5);

        int offX = (zW - mainW) / 2;
        int offZ = (zD - mainD) / 2;

        Vector3I bS = o + new Vector3I(offX, 0, offZ);
        Vector3I bE = bS + new Vector3I(mainW - 1, 0, mainD - 1);

        // ── Concrete foundation ──
        AddFloor(plan, tracker, bS, bE, VoxelMaterialType.Concrete);

        // ── Brick walls ──
        for (int y = 1; y <= wallH; y++)
        {
            AddHollowLayer(plan, tracker,
                bS + new Vector3I(0, y, 0), bE + new Vector3I(0, y, 0),
                VoxelMaterialType.Brick);
        }

        // ── Concrete corner buttresses ──
        AddCornerColumns(plan, tracker, bS, bE, 1, wallH, VoxelMaterialType.Concrete);

        // ── Roof ──
        int roofY = wallH + 1;
        AddFloor(plan, tracker,
            bS + new Vector3I(0, roofY, 0), bE + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Brick);

        // ── Crenellations ──
        AddCrenellations(plan, tracker, bS, bE, roofY + 1, VoxelMaterialType.Brick);

        // ── Side wing (attached to one side of main building) ──
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
        AddFloor(plan, tracker, wS, wE, VoxelMaterialType.Concrete);
        // Wing walls
        for (int y = 1; y <= wallH; y++)
        {
            AddHollowLayer(plan, tracker,
                wS + new Vector3I(0, y, 0), wE + new Vector3I(0, y, 0),
                VoxelMaterialType.Brick);
        }
        // Wing roof
        AddFloor(plan, tracker,
            wS + new Vector3I(0, roofY, 0), wE + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Brick);

        // ── Interior dividing wall ──
        int divZ = bS.Z + mainD / 2;
        divZ = Math.Clamp(divZ, bS.Z + 2, bE.Z - 2);
        for (int y = 1; y < wallH; y++)
        {
            for (int x = bS.X + 1; x < bE.X; x++)
            {
                AddSingle(plan, tracker, new Vector3I(x, y, divZ), VoxelMaterialType.Concrete);
            }
        }

        // ── Commander inside main building, well inset from walls ──
        int cmdOX = mainW / 2 + rng.Next(3) - 1;
        int cmdOZ = mainD / 2 + rng.Next(3) - 1;
        cmdOX = Math.Clamp(cmdOX, 3, mainW - 4);
        cmdOZ = Math.Clamp(cmdOZ, 3, mainD - 4);
        Vector3I cmdPos = bS + new Vector3I(cmdOX, 1, cmdOZ);
        plan.CommanderBuildUnit = cmdPos;
        AddCommanderShell(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Concrete, 3);
        AddCommanderShell(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Metal, 5);
        EnsureCommanderCeiling(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Concrete);

        // ── Weapons on roof edges + wing roof ──
        int weaponCount = 4 + rng.Next(2);
        PlaceRoofEdgeWeapons(plan, tracker, bS, bE, roofY, cmdPos, weaponCount,
            new[] { "cannon", "mortar", "railgun", "missile" }, rng, zone);

        EnsureAtLeastOneWeapon(plan, tracker, bS, bE, roofY, zone);

        return plan;
    }

    // ═══════════════════════════════════════════════════════════════
    //  HARD FORTRESS — 3 styles  (5-7 weapons)
    // ═══════════════════════════════════════════════════════════════

    private static BotBuildPlan PlanHardFortress(BuildZone zone, int budget, Random rng)
    {
        int style = rng.Next(3);
        return style switch
        {
            0 => PlanHardGrandCastle(zone, budget, rng),
            1 => PlanHardWarFactory(zone, budget, rng),
            _ => PlanHardMountainStronghold(zone, budget, rng),
        };
    }

    // ─────────────────────────────────────────────────
    //  Hard Style 1: Grand Castle
    //  Curtain wall with 4 corner towers, central keep, gatehouse.
    //  Multi-material: stone exterior, metal keep, obsidian commander vault.
    // ─────────────────────────────────────────────────

    private static BotBuildPlan PlanHardGrandCastle(BuildZone zone, int budget, Random rng)
    {
        BotBuildPlan plan = new BotBuildPlan();
        BudgetTracker tracker = new BudgetTracker(budget);

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
        AddFloor(plan, tracker, bS, bE, VoxelMaterialType.Stone);

        // ── Curtain walls ──
        for (int y = 1; y <= wallH; y++)
        {
            AddHollowLayer(plan, tracker,
                bS + new Vector3I(0, y, 0), bE + new Vector3I(0, y, 0),
                VoxelMaterialType.Stone);
        }

        // ── Brick accent near top ──
        int accentY = wallH;
        AddHollowLayer(plan, tracker,
            bS + new Vector3I(0, accentY, 0), bE + new Vector3I(0, accentY, 0),
            VoxelMaterialType.Brick);

        // ── Curtain wall walkway (roof at wall top for weapons to stand on) ──
        // Only build the perimeter strip as a walkway (2 blocks wide)
        int walkwayY = wallH + 1;
        AddPerimeterWalkway(plan, tracker, bS, bE, walkwayY, 2, VoxelMaterialType.Stone);

        // ── Crenellations on curtain wall ──
        AddCrenellations(plan, tracker, bS, bE, walkwayY + 1, VoxelMaterialType.Stone);

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

            // Tower walls above curtain wall
            for (int y = wallH + 1; y < towerH; y++)
            {
                AddHollowLayer(plan, tracker,
                    tOrigin + new Vector3I(0, y, 0), tEnd + new Vector3I(0, y, 0),
                    VoxelMaterialType.Stone);
            }

            // Tower roof
            int tRoofY = towerH;
            AddFloor(plan, tracker,
                tOrigin + new Vector3I(0, tRoofY, 0), tEnd + new Vector3I(0, tRoofY, 0),
                VoxelMaterialType.Stone);

            // Tower crenellations
            AddCrenellations(plan, tracker, tOrigin, tEnd, tRoofY + 1, VoxelMaterialType.Stone);
        }

        // ── Central keep (metal, inside the courtyard) ──
        int keepW = Math.Min(outerW - 8, 8);
        int keepD = Math.Min(outerD - 8, 8);
        int keepH = Math.Min(wallH + 2, zH - 4);

        if (keepW >= 5 && keepD >= 5)
        {
            Vector3I kS = bS + new Vector3I((outerW - keepW) / 2, 0, (outerD - keepD) / 2);
            Vector3I kE = kS + new Vector3I(keepW - 1, 0, keepD - 1);

            // Keep floor
            AddFloor(plan, tracker, kS, kE, VoxelMaterialType.Metal);

            // Keep walls
            for (int y = 1; y <= keepH; y++)
            {
                AddHollowLayer(plan, tracker,
                    kS + new Vector3I(0, y, 0), kE + new Vector3I(0, y, 0),
                    VoxelMaterialType.Metal);
            }

            // Keep roof
            int keepRoofY = keepH + 1;
            AddFloor(plan, tracker,
                kS + new Vector3I(0, keepRoofY, 0), kE + new Vector3I(0, keepRoofY, 0),
                VoxelMaterialType.Metal);
        }

        // ── Gatehouse (protruding structure on front wall) ──
        int gateW = 4;
        int gateD = 2;
        int gateX = bS.X + (outerW - gateW) / 2;
        int gateZ = bS.Z - gateD;
        if (gateZ >= o.Z)
        {
            Vector3I gS = new Vector3I(gateX, o.Y, gateZ);
            Vector3I gE = new Vector3I(gateX + gateW - 1, o.Y, bS.Z);

            // Gatehouse foundation
            AddFloor(plan, tracker, gS, gE, VoxelMaterialType.Stone);

            // Gatehouse walls
            for (int y = 1; y <= wallH; y++)
            {
                AddHollowLayer(plan, tracker,
                    gS + new Vector3I(0, y, 0), gE + new Vector3I(0, y, 0),
                    VoxelMaterialType.Stone);
            }

            // Gatehouse roof
            AddFloor(plan, tracker,
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
        AddObsidianVault(plan, tracker, cmdPos, bS, bE);
        EnsureCommanderCeiling(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Stone);

        // ── Decoy rooms ──
        AddDecoyRooms(plan, tracker, bS, bE, outerW, outerD, quadrant, VoxelMaterialType.Obsidian, rng);

        // ── Weapons on tower tops (only outward-facing towers) ──
        var (gcUseMinX, gcUseMaxX, gcUseMinZ, gcUseMaxZ) = GetOutwardEdges(zone);
        int gcFortCX = (bS.X + bE.X) / 2;
        int gcFortCZ = (bS.Z + bE.Z) / 2;

        string[] weapons = { "railgun", "cannon", "mortar", "missile" };
        int weaponCount = 5 + rng.Next(3);
        const int guaranteedWeaponsGC = 3;
        List<Vector3I> placed = new List<Vector3I>();
        HashSet<Vector3I> gcPlannedBlocks = BuildPlannedBlocksSet(plan);

        for (int i = 0; i < towerOrigins.Length && placed.Count < weaponCount; i++)
        {
            Vector3I tOrigin = towerOrigins[i];
            int tCX = tOrigin.X + (towerSize - 1) / 2;
            int tCZ = tOrigin.Z + (towerSize - 1) / 2;

            // Skip towers on inward-facing edges
            bool onOutward = false;
            if (tCX < gcFortCX && gcUseMinX) onOutward = true;
            if (tCX >= gcFortCX && gcUseMaxX) onOutward = true;
            if (tCZ < gcFortCZ && gcUseMinZ) onOutward = true;
            if (tCZ >= gcFortCZ && gcUseMaxZ) onOutward = true;
            if (!onOutward) continue;

            Vector3I tEnd = tOrigin + new Vector3I(towerSize - 1, 0, towerSize - 1);
            Vector3I wPos = GetOuterCorner(tOrigin, tEnd, bS, bE, towerH + 1);

            if (wPos.DistanceTo(cmdPos) < GameConfig.MinWeaponCommanderGap) continue;
            if (WeaponOverlapsBlocks(wPos, wPos.Y - 4, gcPlannedBlocks)) continue;

            string wId = weapons[i % weapons.Length];
            int cost = GetWeaponCost(wId);
            bool guaranteed = placed.Count < guaranteedWeaponsGC;

            if (guaranteed || tracker.TrySpend(cost))
            {
                if (guaranteed) tracker.TrySpend(cost); // best-effort

                // Build a 3-block tall stone pillar under the weapon so it
                // clears crenellations and all rooftop structures.
                for (int py = wPos.Y - 3; py <= wPos.Y - 1; py++)
                {
                    Vector3I pillarPos = new Vector3I(wPos.X, py, wPos.Z);
                    plan.Actions.Add(new PlannedBuildAction
                    {
                        ToolMode = BuildToolMode.Single,
                        Material = VoxelMaterialType.Stone,
                        Start = pillarPos,
                        End = pillarPos,
                        Hollow = false,
                    });
                    gcPlannedBlocks.Add(pillarPos);
                    tracker.TrySpend(VoxelMaterials.GetDefinition(VoxelMaterialType.Stone).Cost);
                }
                plan.WeaponPlacements.Add((wPos, wId));
                placed.Add(wPos);
            }
        }

        // ── Extra weapons on curtain wall walkway ──
        int remaining = weaponCount - placed.Count;
        if (remaining > 0)
        {
            PlaceRoofEdgeWeapons(plan, tracker, bS, bE, walkwayY, cmdPos,
                remaining, new[] { "cannon", "mortar", "drill" }, rng, zone);
        }

        EnsureAtLeastOneWeapon(plan, tracker, bS, bE, walkwayY, zone);

        return plan;
    }

    // ─────────────────────────────────────────────────
    //  Hard Style 2: War Factory
    //  Industrial-style: reinforced steel + metal, weapon platforms
    //  at varied heights, thick double walls.
    // ─────────────────────────────────────────────────

    private static BotBuildPlan PlanHardWarFactory(BuildZone zone, int budget, Random rng)
    {
        BotBuildPlan plan = new BotBuildPlan();
        BudgetTracker tracker = new BudgetTracker(budget);

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
        AddFloor(plan, tracker, bS, bE, VoxelMaterialType.Concrete);

        // ── Reinforced steel outer walls ──
        for (int y = 1; y <= wallH; y++)
        {
            AddHollowLayer(plan, tracker,
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
                AddHollowLayer(plan, tracker,
                    innerS + new Vector3I(0, y, 0), innerE + new Vector3I(0, y, 0),
                    VoxelMaterialType.Metal);
            }
        }

        // ── Double roof ──
        int roofY = wallH + 1;
        AddFloor(plan, tracker,
            bS + new Vector3I(0, roofY, 0), bE + new Vector3I(0, roofY, 0),
            VoxelMaterialType.Metal);
        AddFloor(plan, tracker,
            bS + new Vector3I(0, roofY + 1, 0), bE + new Vector3I(0, roofY + 1, 0),
            VoxelMaterialType.ReinforcedSteel);

        // ── Weapon platform towers on the outward-facing Z edge ──
        var (_, _, wfUseMinZ, _) = GetOutwardEdges(zone);
        int wfFrontZ = wfUseMinZ ? bS.Z : bE.Z;
        int wfInset = wfUseMinZ ? 1 : -1; // direction toward interior

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

            // Build pillar from roof up (structurally supported by roof)
            for (int y = baseY; y <= topY; y++)
            {
                AddSingle(plan, tracker, new Vector3I(px, y, wfFrontZ + wfInset), VoxelMaterialType.Metal);
                AddSingle(plan, tracker, new Vector3I(px + 1, y, wfFrontZ + wfInset), VoxelMaterialType.Metal);
            }

            // Platform cap (3x3 on top of pillar)
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = 0; dz <= 2; dz++)
                {
                    AddSingle(plan, tracker,
                        new Vector3I(px + dx, topY + 1, wfFrontZ + dz * wfInset),
                        VoxelMaterialType.ReinforcedSteel);
                }
            }

            // Weapon goes on the front edge of the platform
            platformTops.Add(new Vector3I(px, topY + 2, wfFrontZ));
        }

        // ── Armor plate on front face ──
        for (int y = 1; y <= wallH; y++)
        {
            for (int x = bS.X; x <= bE.X; x++)
            {
                AddSingle(plan, tracker, new Vector3I(x, y, wfFrontZ), VoxelMaterialType.ArmorPlate);
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
        AddObsidianVault(plan, tracker, cmdPos, bS, bE);
        EnsureCommanderCeiling(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.ReinforcedSteel);

        // ── Decoy rooms ──
        AddDecoyRooms(plan, tracker, bS, bE, mainW, mainD, quadrant, VoxelMaterialType.ReinforcedSteel, rng);

        // ── Interior dividing walls ──
        AddInteriorWall(plan, tracker, bS, bE, mainW, wallH, mainD, VoxelMaterialType.Metal, rng);
        AddInteriorWall(plan, tracker, bS, bE, mainW, wallH, mainD, VoxelMaterialType.Concrete, rng);

        // ── Weapons on platforms (facing outward, front edge) ──
        string[] weapons = { "cannon", "railgun", "missile", "drill", "mortar" };
        HashSet<Vector3I> wfPlannedBlocks = BuildPlannedBlocksSet(plan);
        List<Vector3I> wpPlaced = new List<Vector3I>();
        int weaponCount = 5 + rng.Next(3);
        const int guaranteedWeaponsWF = 3;

        for (int i = 0; i < platformTops.Count && wpPlaced.Count < weaponCount; i++)
        {
            Vector3I wPos = platformTops[i];
            if (wPos.DistanceTo(cmdPos) < GameConfig.MinWeaponCommanderGap) continue;
            if (wfPlannedBlocks.Contains(wPos)) continue;

            string wId = weapons[i % weapons.Length];
            int cost = GetWeaponCost(wId);
            bool guaranteed = wpPlaced.Count < guaranteedWeaponsWF;

            if (guaranteed || tracker.TrySpend(cost))
            {
                if (guaranteed) tracker.TrySpend(cost); // best-effort

                plan.WeaponPlacements.Add((wPos, wId));
                wpPlaced.Add(wPos);
            }
        }

        // Extra weapons on roof edges
        int extra = weaponCount - wpPlaced.Count;
        if (extra > 0)
        {
            PlaceRoofEdgeWeapons(plan, tracker, bS, bE, roofY + 1, cmdPos,
                extra, new[] { "mortar", "cannon", "railgun" }, rng, zone);
        }

        EnsureAtLeastOneWeapon(plan, tracker, bS, bE, roofY + 1, zone);

        return plan;
    }

    // ─────────────────────────────────────────────────
    //  Hard Style 3: Mountain Stronghold
    //  Three-tiered stepped pyramid. Each tier smaller than the last.
    //  Weapons at every terrace level with clear outward fire.
    // ─────────────────────────────────────────────────

    private static BotBuildPlan PlanHardMountainStronghold(BuildZone zone, int budget, Random rng)
    {
        BotBuildPlan plan = new BotBuildPlan();
        BudgetTracker tracker = new BudgetTracker(budget);

        Vector3I o = zone.OriginBuildUnits;
        int zW = zone.SizeBuildUnits.X;
        int zH = zone.SizeBuildUnits.Y;
        int zD = zone.SizeBuildUnits.Z;

        // Tier dimensions (each tier is 2 blocks smaller on each side)
        int baseW = Math.Min(18, zW - 4);
        int baseD = Math.Min(18, zD - 4);
        int tierH = 3;

        int offX = (zW - baseW) / 2;
        int offZ = (zD - baseD) / 2;

        Vector3I t1S = o + new Vector3I(offX, 0, offZ);
        Vector3I t1E = t1S + new Vector3I(baseW - 1, 0, baseD - 1);

        // ════ TIER 1: Base (stone + armor plate front) ════

        // Foundation
        AddFloor(plan, tracker, t1S, t1E, VoxelMaterialType.Stone);

        // Walls
        for (int y = 1; y <= tierH; y++)
        {
            AddHollowLayer(plan, tracker,
                t1S + new Vector3I(0, y, 0), t1E + new Vector3I(0, y, 0),
                VoxelMaterialType.Stone);
        }

        // Armor plate on front face
        for (int y = 1; y <= tierH; y++)
        {
            for (int x = t1S.X; x <= t1E.X; x++)
            {
                AddSingle(plan, tracker, new Vector3I(x, y, t1S.Z), VoxelMaterialType.ArmorPlate);
            }
        }

        // Tier 1 roof (serves as terrace floor for weapons + tier 2 base)
        int t1RoofY = tierH + 1;
        AddFloor(plan, tracker,
            t1S + new Vector3I(0, t1RoofY, 0), t1E + new Vector3I(0, t1RoofY, 0),
            VoxelMaterialType.Stone);

        // Crenellations on tier 1 exposed edges
        AddCrenellations(plan, tracker, t1S, t1E, t1RoofY + 1, VoxelMaterialType.Stone);

        // ════ TIER 2: Mid (concrete, inset by 3 on each side) ════

        int midW = baseW - 6;
        int midD = baseD - 6;
        // Tier 2 top absolute Y = o.Y + 2*tierH + 3 (walls + roof + crenellation)
        bool hasTier2 = midW >= 6 && midD >= 6 && 2 * tierH + 3 < zH;

        Vector3I t2S = t1S + new Vector3I(3, t1RoofY, 3);
        Vector3I t2E = t2S + new Vector3I(midW - 1, 0, midD - 1);

        if (hasTier2)
        {
            // Walls
            for (int y = 1; y <= tierH; y++)
            {
                AddHollowLayer(plan, tracker,
                    t2S + new Vector3I(0, y, 0), t2E + new Vector3I(0, y, 0),
                    VoxelMaterialType.Concrete);
            }

            // Roof
            AddFloor(plan, tracker,
                t2S + new Vector3I(0, tierH + 1, 0), t2E + new Vector3I(0, tierH + 1, 0),
                VoxelMaterialType.Concrete);

            // Crenellations
            AddCrenellations(plan, tracker, t2S, t2E, tierH + 2, VoxelMaterialType.Concrete);

            // ════ TIER 3: Top (metal, inset by another 3) ════

            int topW = midW - 6;
            int topD = midD - 6;
            // Tier 3 top absolute Y = o.Y + 3*tierH + 4 (walls + roof + crenellation)
            bool hasTier3 = topW >= 4 && topD >= 4 && 3 * tierH + 4 < zH;

            Vector3I t3S = t2S + new Vector3I(3, tierH + 1, 3);
            Vector3I t3E = t3S + new Vector3I(topW - 1, 0, topD - 1);

            if (hasTier3)
            {
                // Walls
                for (int y = 1; y <= tierH; y++)
                {
                    AddHollowLayer(plan, tracker,
                        t3S + new Vector3I(0, y, 0), t3E + new Vector3I(0, y, 0),
                        VoxelMaterialType.Metal);
                }

                // Reinforced steel roof
                AddFloor(plan, tracker,
                    t3S + new Vector3I(0, tierH + 1, 0), t3E + new Vector3I(0, tierH + 1, 0),
                    VoxelMaterialType.ReinforcedSteel);

                // Crenellations
                AddCrenellations(plan, tracker, t3S, t3E, tierH + 2, VoxelMaterialType.Metal);
            }

            // ── Commander in base tier (deepest protection) ──
            int quadrant = rng.Next(4);
            int cmdOX, cmdOZ;
            switch (quadrant)
            {
                case 0: cmdOX = baseW / 4; cmdOZ = baseD / 4; break;
                case 1: cmdOX = baseW * 3 / 4; cmdOZ = baseD / 4; break;
                case 2: cmdOX = baseW / 4; cmdOZ = baseD * 3 / 4; break;
                default: cmdOX = baseW * 3 / 4; cmdOZ = baseD * 3 / 4; break;
            }
            cmdOX = Math.Clamp(cmdOX, 4, baseW - 5);
            cmdOZ = Math.Clamp(cmdOZ, 4, baseD - 5);
            Vector3I cmdPos = t1S + new Vector3I(cmdOX, 1, cmdOZ);
            plan.CommanderBuildUnit = cmdPos;
            AddObsidianVault(plan, tracker, cmdPos, t1S, t1E);
            EnsureCommanderCeiling(plan, tracker, cmdPos, t1S, t1E, VoxelMaterialType.Stone);
            AddDecoyRooms(plan, tracker, t1S, t1E, baseW, baseD, quadrant, VoxelMaterialType.Obsidian, rng);

            // ── Weapons across all tiers ──
            int weaponCount = 5 + rng.Next(3);
            string[] weapons = { "cannon", "mortar", "railgun", "missile", "drill" };
            List<Vector3I> wpPlaced = new List<Vector3I>();

            // Tier 1 terrace weapons (outer corners)
            PlaceTerraceCornerWeapons(plan, tracker, t1S, t1E, t1RoofY, cmdPos,
                2, weapons, wpPlaced, rng, zone);

            // Tier 2 terrace weapons (roofY is relative to t2S)
            if (hasTier2)
            {
                PlaceTerraceCornerWeapons(plan, tracker, t2S, t2E, tierH + 1, cmdPos,
                    2, weapons, wpPlaced, rng, zone);
            }

            // Tier 3 top weapon
            if (hasTier3)
            {
                // Tier 3 roof is at absolute Y = t3S.Y + tierH + 1
                int t3RoofAbsY = t3S.Y + tierH + 1;
                // Weapon sits 4 units above roof on a 3-block pillar to clear
                // crenellations and all rooftop structures.
                // Inset by 1 from front edge to avoid perimeter wall overlap.
                int t3WeaponX = t3S.X + Math.Max(topW / 2, 1);
                int t3WeaponZ = Math.Min(t3S.Z + 1, t3E.Z);
                Vector3I topWPos = new Vector3I(t3WeaponX, t3RoofAbsY + 4, t3WeaponZ);
                HashSet<Vector3I> t3PlannedBlocks = BuildPlannedBlocksSet(plan);
                if (topWPos.DistanceTo(cmdPos) >= GameConfig.MinWeaponCommanderGap
                    && wpPlaced.Count < weaponCount
                    && !WeaponOverlapsBlocks(topWPos, t3RoofAbsY, t3PlannedBlocks))
                {
                    string wId = "railgun";
                    int t3Cost = GetWeaponCost(wId);
                    bool t3Guaranteed = wpPlaced.Count < 3;

                    if (t3Guaranteed || tracker.TrySpend(t3Cost))
                    {
                        if (t3Guaranteed) tracker.TrySpend(t3Cost); // best-effort

                        // Build 3-block reinforced steel pillar (roof+1 to roof+3)
                        for (int py = 1; py <= 3; py++)
                        {
                            plan.Actions.Add(new PlannedBuildAction
                            {
                                ToolMode = BuildToolMode.Single,
                                Material = VoxelMaterialType.ReinforcedSteel,
                                Start = new Vector3I(topWPos.X, t3RoofAbsY + py, topWPos.Z),
                                End = new Vector3I(topWPos.X, t3RoofAbsY + py, topWPos.Z),
                                Hollow = false,
                            });
                            tracker.TrySpend(VoxelMaterials.GetDefinition(VoxelMaterialType.ReinforcedSteel).Cost);
                        }
                        plan.WeaponPlacements.Add((topWPos, wId));
                        wpPlaced.Add(topWPos);
                    }
                }
            }

            // Fill remaining weapon slots on tier 1 terrace
            int remainingWeapons = weaponCount - wpPlaced.Count;
            if (remainingWeapons > 0)
            {
                PlaceRoofEdgeWeapons(plan, tracker, t1S, t1E, t1RoofY, cmdPos,
                    remainingWeapons, weapons, rng, zone);
            }

            EnsureAtLeastOneWeapon(plan, tracker, t1S, t1E, t1RoofY, zone);
            return plan;
        }

        // ── Fallback: only base tier fits ──
        int fbX = Math.Clamp(baseW / 2, 2, baseW - 3);
        int fbZ = Math.Clamp(baseD / 2, 2, baseD - 3);
        Vector3I cmdFallback = t1S + new Vector3I(fbX, 1, fbZ);
        plan.CommanderBuildUnit = cmdFallback;
        AddCommanderShell(plan, tracker, cmdFallback, t1S, t1E, VoxelMaterialType.Concrete, 3);
        EnsureCommanderCeiling(plan, tracker, cmdFallback, t1S, t1E, VoxelMaterialType.Concrete);
        PlaceRoofEdgeWeapons(plan, tracker, t1S, t1E, t1RoofY, cmdFallback,
            5 + rng.Next(3), new[] { "cannon", "mortar", "railgun", "missile" }, rng, zone);
        EnsureAtLeastOneWeapon(plan, tracker, t1S, t1E, t1RoofY, zone);
        return plan;
    }

    // ═══════════════════════════════════════════════════════════════
    //  BUILDING PRIMITIVES — all build bottom-up, no floating blocks
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds a solid floor slab at the Y level of start/end.
    /// </summary>
    internal static void AddFloor(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I start, Vector3I end, VoxelMaterialType material)
    {
        int w = end.X - start.X + 1;
        int d = end.Z - start.Z + 1;
        int cost = w * d * VoxelMaterials.GetDefinition(material).Cost;
        if (tracker.TrySpend(cost))
        {
            plan.Actions.Add(new PlannedBuildAction
            {
                ToolMode = BuildToolMode.Floor,
                Material = material,
                Start = start,
                End = end,
                Hollow = false,
            });
        }
    }

    /// <summary>
    /// Adds a single hollow ring (perimeter only) at the Y level of start/end.
    /// This is one layer of a wall -- used to build walls layer-by-layer from
    /// bottom to top so there are never floating blocks.
    /// </summary>
    internal static void AddHollowLayer(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I start, Vector3I end, VoxelMaterialType material)
    {
        int w = end.X - start.X + 1;
        int d = end.Z - start.Z + 1;
        // Shell of a 1-tall box = perimeter ring
        int perimeterCount = 2 * (w + d) - 4;
        if (perimeterCount <= 0) return;

        int cost = perimeterCount * VoxelMaterials.GetDefinition(material).Cost;
        if (tracker.TrySpend(cost))
        {
            plan.Actions.Add(new PlannedBuildAction
            {
                ToolMode = BuildToolMode.Box,
                Material = material,
                Start = start,
                End = end,
                Hollow = true,
            });
        }
    }

    /// <summary>
    /// Places a single build-unit block.
    /// </summary>
    internal static void AddSingle(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I pos, VoxelMaterialType material)
    {
        int cost = VoxelMaterials.GetDefinition(material).Cost;
        if (tracker.TrySpend(cost))
        {
            plan.Actions.Add(new PlannedBuildAction
            {
                ToolMode = BuildToolMode.Single,
                Material = material,
                Start = pos,
                End = pos,
                Hollow = false,
            });
        }
    }

    /// <summary>
    /// Adds solid columns at each corner of the bounding box, from yLow to yHigh.
    /// Built bottom-up (each column is a vertical box from low to high).
    /// </summary>
    internal static void AddCornerColumns(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I bS, Vector3I bE, int yLow, int yHigh, VoxelMaterialType material)
    {
        if (yHigh < yLow) return;
        int h = yHigh - yLow + 1;
        int colCost = h * VoxelMaterials.GetDefinition(material).Cost;

        Vector3I[] corners =
        {
            new Vector3I(bS.X, bS.Y + yLow, bS.Z),
            new Vector3I(bE.X, bS.Y + yLow, bS.Z),
            new Vector3I(bS.X, bS.Y + yLow, bE.Z),
            new Vector3I(bE.X, bS.Y + yLow, bE.Z),
        };

        foreach (Vector3I c in corners)
        {
            if (tracker.TrySpend(colCost))
            {
                plan.Actions.Add(new PlannedBuildAction
                {
                    ToolMode = BuildToolMode.Box,
                    Material = material,
                    Start = c,
                    End = c + new Vector3I(0, h - 1, 0),
                    Hollow = false,
                });
            }
        }
    }

    /// <summary>
    /// Adds crenellations (alternating raised blocks) along the perimeter at a
    /// given Y. These sit on the roof below, so they are structurally supported.
    /// </summary>
    internal static void AddCrenellations(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I bS, Vector3I bE, int crenY, VoxelMaterialType material)
    {
        int actualY = bS.Y + crenY;

        // Front and back edges
        for (int x = bS.X; x <= bE.X; x += 2)
        {
            AddSingle(plan, tracker, new Vector3I(x, actualY, bS.Z), material);
            AddSingle(plan, tracker, new Vector3I(x, actualY, bE.Z), material);
        }

        // Left and right edges (skip corners already placed)
        for (int z = bS.Z + 2; z <= bE.Z - 1; z += 2)
        {
            AddSingle(plan, tracker, new Vector3I(bS.X, actualY, z), material);
            AddSingle(plan, tracker, new Vector3I(bE.X, actualY, z), material);
        }
    }

    /// <summary>
    /// Adds a perimeter walkway (ring of floor blocks, N blocks wide) at a given Y.
    /// Used for castle wall walkways where weapons can be placed.
    /// </summary>
    internal static void AddPerimeterWalkway(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I bS, Vector3I bE, int walkwayY, int walkwayWidth, VoxelMaterialType material)
    {
        int actualY = bS.Y + walkwayY;

        // Build as a full floor, then the inner portion is just air (hollow box with height=1)
        // For simplicity and correctness, just place the full floor. The fortress interior
        // below already exists and the roof above will be solid.
        AddFloor(plan, tracker,
            new Vector3I(bS.X, actualY, bS.Z),
            new Vector3I(bE.X, actualY, bE.Z),
            material);
    }

    // ═══════════════════════════════════════════════════════════════
    //  COMMANDER PROTECTION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a hollow box shell around the commander position.
    /// Size is clamped to stay within the fortress bounds.
    /// Built as a hollow box which starts at y=0 of commander (the floor level).
    /// If the shell is too small to build as a full box, a ceiling slab is placed
    /// above the commander so they are never left exposed from above.
    /// </summary>
    internal static void AddCommanderShell(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I cmdPos, Vector3I bS, Vector3I bE, VoxelMaterialType material, int size)
    {
        int half = size / 2;
        Vector3I shellS = new Vector3I(
            Math.Max(cmdPos.X - half, bS.X + 1),
            Math.Max(cmdPos.Y, bS.Y),
            Math.Max(cmdPos.Z - half, bS.Z + 1));
        Vector3I shellE = new Vector3I(
            Math.Min(cmdPos.X + half, bE.X - 1),
            Math.Min(cmdPos.Y + size - 1, bE.Y + 20), // walls can be tall
            Math.Min(cmdPos.Z + half, bE.Z - 1));

        int sw = shellE.X - shellS.X + 1;
        int sh = shellE.Y - shellS.Y + 1;
        int sd = shellE.Z - shellS.Z + 1;

        if (sw < 3 || sh < 3 || sd < 3)
        {
            // Shell is too small for a proper hollow box — at minimum place a
            // ceiling slab so the commander is protected from above.
            int ceilingY = cmdPos.Y + 2;
            Vector3I ceilS = new Vector3I(
                Math.Max(cmdPos.X - 1, bS.X),
                ceilingY,
                Math.Max(cmdPos.Z - 1, bS.Z));
            Vector3I ceilE = new Vector3I(
                Math.Min(cmdPos.X + 1, bE.X),
                ceilingY,
                Math.Min(cmdPos.Z + 1, bE.Z));
            AddFloor(plan, tracker, ceilS, ceilE, material);
            return;
        }

        // Build layer by layer from bottom to top
        for (int y = shellS.Y; y <= shellE.Y; y++)
        {
            Vector3I layerS = new Vector3I(shellS.X, y, shellS.Z);
            Vector3I layerE = new Vector3I(shellE.X, y, shellE.Z);

            if (y == shellS.Y || y == shellE.Y)
            {
                // Floor/ceiling: solid
                AddFloor(plan, tracker, layerS, layerE, material);
            }
            else
            {
                // Wall ring: hollow
                AddHollowLayer(plan, tracker, layerS, layerE, material);
            }
        }
    }

    /// <summary>
    /// Ensures the commander has a solid ceiling above them. Places a 3x3 floor
    /// slab 2 build units above the commander position. Call after all other
    /// build actions to guarantee overhead cover even if the main shell was
    /// skipped or the fortress roof was damaged by weapon placement.
    /// </summary>
    internal static void EnsureCommanderCeiling(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I cmdPos, Vector3I bS, Vector3I bE, VoxelMaterialType material)
    {
        int ceilingY = cmdPos.Y + 2;
        Vector3I ceilS = new Vector3I(
            Math.Max(cmdPos.X - 1, bS.X),
            ceilingY,
            Math.Max(cmdPos.Z - 1, bS.Z));
        Vector3I ceilE = new Vector3I(
            Math.Min(cmdPos.X + 1, bE.X),
            ceilingY,
            Math.Min(cmdPos.Z + 1, bE.Z));
        AddFloor(plan, tracker, ceilS, ceilE, material);
    }

    /// <summary>
    /// Adds a multi-layered obsidian + reinforced steel + concrete vault
    /// around the commander. Respects MaxObsidianBlocks.
    /// </summary>
    internal static void AddObsidianVault(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I cmdPos, Vector3I bS, Vector3I bE)
    {
        // Inner vault (3x3x3)
        int vaultSize = 3;
        VoxelMaterialType vaultMat = VoxelMaterialType.Obsidian;
        int obsidianCount = EstimateShellBlockCount(vaultSize, vaultSize, vaultSize);
        if (obsidianCount > GameConfig.MaxObsidianBlocks)
        {
            vaultMat = VoxelMaterialType.ReinforcedSteel;
        }

        AddCommanderShell(plan, tracker, cmdPos, bS, bE, vaultMat, 3);

        // Reinforced steel wrapping (5x5x5)
        AddCommanderShell(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.ReinforcedSteel, 5);

        // Concrete outer layer (7x7x7)
        AddCommanderShell(plan, tracker, cmdPos, bS, bE, VoxelMaterialType.Concrete, 7);
    }

    /// <summary>
    /// Adds decoy rooms in quadrants opposite to the commander.
    /// </summary>
    internal static void AddDecoyRooms(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I bS, Vector3I bE, int outerW, int outerD,
        int commanderQuadrant, VoxelMaterialType decoyMat, Random rng)
    {
        int decoyCount = 1 + rng.Next(2);
        for (int d = 0; d < decoyCount; d++)
        {
            int decoyQuadrant = (commanderQuadrant + 1 + d) % 4;
            int dx, dz;
            switch (decoyQuadrant)
            {
                case 0: dx = outerW / 4; dz = outerD / 4; break;
                case 1: dx = outerW * 3 / 4; dz = outerD / 4; break;
                case 2: dx = outerW / 4; dz = outerD * 3 / 4; break;
                default: dx = outerW * 3 / 4; dz = outerD * 3 / 4; break;
            }
            dx = Math.Clamp(dx, 3, outerW - 4);
            dz = Math.Clamp(dz, 3, outerD - 4);

            Vector3I center = bS + new Vector3I(dx, 1, dz);
            // Build a small 3x3x3 room layer by layer
            AddCommanderShell(plan, tracker, center,
                bS + new Vector3I(1, 0, 1), bE - new Vector3I(1, 0, 1),
                decoyMat, 3);
        }
    }

    /// <summary>
    /// Adds an interior dividing wall for structural compartmentalization.
    /// </summary>
    internal static void AddInteriorWall(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I bS, Vector3I bE, int mainW, int wallH, int mainD,
        VoxelMaterialType material, Random rng)
    {
        bool xAligned = rng.Next(2) == 0;

        if (xAligned)
        {
            int wallZ = bS.Z + 3 + rng.Next(Math.Max(1, mainD - 6));
            wallZ = Math.Clamp(wallZ, bS.Z + 2, bE.Z - 2);
            for (int y = 1; y < wallH; y++)
            {
                for (int x = bS.X + 1; x < bE.X; x++)
                {
                    AddSingle(plan, tracker, new Vector3I(x, bS.Y + y, wallZ), material);
                }
            }
        }
        else
        {
            int wallX = bS.X + 3 + rng.Next(Math.Max(1, mainW - 6));
            wallX = Math.Clamp(wallX, bS.X + 2, bE.X - 2);
            for (int y = 1; y < wallH; y++)
            {
                for (int z = bS.Z + 1; z < bE.Z; z++)
                {
                    AddSingle(plan, tracker, new Vector3I(wallX, bS.Y + y, z), material);
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  WEAPON PLACEMENT — always on outer edges with clear line of fire
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines which edges of the fortress face outward toward the arena
    /// center (where enemies are). Weapons should only be placed on these
    /// edges so they have a clear line of fire without shooting through
    /// their own fortress.
    /// </summary>
    internal static (bool useMinX, bool useMaxX, bool useMinZ, bool useMaxZ) GetOutwardEdges(BuildZone zone)
    {
        // Arena center is at (0, y, 0) in build units
        int zoneCenterX = zone.OriginBuildUnits.X + zone.SizeBuildUnits.X / 2;
        int zoneCenterZ = zone.OriginBuildUnits.Z + zone.SizeBuildUnits.Z / 2;

        // The outward edge faces toward the arena center (where enemies are)
        bool useMinX = zoneCenterX > 0;  // Zone is right of center → min-X faces center
        bool useMaxX = zoneCenterX < 0;  // Zone is left of center → max-X faces center
        bool useMinZ = zoneCenterZ > 0;  // Zone is below center → min-Z faces center
        bool useMaxZ = zoneCenterZ < 0;  // Zone is above center → max-Z faces center

        // If zone is exactly centered on an axis, allow both edges
        if (zoneCenterX == 0) { useMinX = true; useMaxX = true; }
        if (zoneCenterZ == 0) { useMinZ = true; useMaxZ = true; }

        return (useMinX, useMaxX, useMinZ, useMaxZ);
    }

    /// <summary>
    /// Places weapons on the outer edges of a roof/platform. Only places on
    /// edges that face toward the arena center (outward) so weapons don't
    /// fire into their own structure. A pillar is placed underneath for support.
    /// </summary>
    internal static void PlaceRoofEdgeWeapons(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I bS, Vector3I bE, int roofY, Vector3I cmdPos,
        int count, string[] weaponPool, Random rng, BuildZone zone)
    {
        int actualRoofY = bS.Y + roofY;
        // Weapons sit 4 units above the roof surface on a 3-block tall stone
        // pillar (roofY+1 to roofY+3).  This ensures the weapon is well above
        // crenellations (roofY+1), corner towers, and other rooftop structures
        // so it has a clear line of fire and won't shoot into its own fortress.
        int weaponY = actualRoofY + 4;

        // Inset candidates by 1 unit from every edge so weapons don't overlap
        // with perimeter wall blocks (crenellations, corner towers, buttresses).
        int xMin = bS.X + 1;
        int xMax = bE.X - 1;
        int zMin = bS.Z + 1;
        int zMax = bE.Z - 1;

        // Need at least 1 valid position per axis
        if (xMin > xMax) { xMin = bS.X; xMax = bE.X; }
        if (zMin > zMax) { zMin = bS.Z; zMax = bE.Z; }

        // Only generate candidates on edges that face toward enemies
        var (useMinX, useMaxX, useMinZ, useMaxZ) = GetOutwardEdges(zone);
        List<Vector3I> candidates = new List<Vector3I>();

        if (useMinZ) // Front edge (min-Z faces toward arena center)
        {
            for (int x = xMin; x <= xMax; x += 2)
                candidates.Add(new Vector3I(x, weaponY, zMin));
        }
        if (useMaxZ) // Back edge (max-Z faces toward arena center)
        {
            for (int x = xMin; x <= xMax; x += 2)
                candidates.Add(new Vector3I(x, weaponY, zMax));
        }
        if (useMinX) // Left edge (min-X faces toward arena center)
        {
            for (int z = zMin + 2; z < zMax; z += 2)
                candidates.Add(new Vector3I(xMin, weaponY, z));
        }
        if (useMaxX) // Right edge (max-X faces toward arena center)
        {
            for (int z = zMin + 2; z < zMax; z += 2)
                candidates.Add(new Vector3I(xMax, weaponY, z));
        }

        // Shuffle candidates for variety
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        // Prioritize positions closest to the arena center (most outward-facing)
        candidates.Sort((a, b) =>
        {
            float distA = a.X * a.X + a.Z * a.Z;
            float distB = b.X * b.X + b.Z * b.Z;
            return distA.CompareTo(distB);
        });

        // Build a set of already-planned block positions so weapons don't
        // overlap with fortress structure (which causes self-destruct).
        HashSet<Vector3I> plannedBlocks = new HashSet<Vector3I>();
        foreach (PlannedBuildAction action in plan.Actions)
        {
            if (action.ToolMode == BuildToolMode.Single)
            {
                plannedBlocks.Add(action.Start);
            }
            else
            {
                for (int ax = action.Start.X; ax <= action.End.X; ax++)
                    for (int ay = action.Start.Y; ay <= action.End.Y; ay++)
                        for (int az = action.Start.Z; az <= action.End.Z; az++)
                            plannedBlocks.Add(new Vector3I(ax, ay, az));
            }
        }

        // The first 3 weapons are guaranteed (skip budget check) so bots
        // always have a minimum armament even when the structure used most of
        // the budget.
        const int guaranteedWeapons = 3;
        int alreadyPlaced = plan.WeaponPlacements.Count;

        List<Vector3I> placed = new List<Vector3I>();
        foreach (Vector3I candidate in candidates)
        {
            if (placed.Count >= count) break;
            if (candidate.DistanceTo(cmdPos) < GameConfig.MinWeaponCommanderGap) continue;
            if (placed.Exists(p => p.DistanceTo(candidate) < 3)) continue;

            // Skip if weapon or its pillar would overlap with existing structure
            bool overlaps = plannedBlocks.Contains(candidate);
            if (!overlaps)
            {
                for (int py = 1; py <= 3; py++)
                {
                    if (plannedBlocks.Contains(new Vector3I(candidate.X, actualRoofY + py, candidate.Z)))
                    {
                        overlaps = true;
                        break;
                    }
                }
            }
            if (overlaps) continue;

            string weaponId = weaponPool[placed.Count % weaponPool.Length];
            int cost = GetWeaponCost(weaponId);
            bool guaranteed = (alreadyPlaced + placed.Count) < guaranteedWeapons;

            if (guaranteed || tracker.TrySpend(cost))
            {
                if (!guaranteed)
                {
                    // Budget was already spent by TrySpend above
                }
                else
                {
                    // Try to spend, but proceed even if budget is exhausted
                    tracker.TrySpend(cost);
                }

                // Build a 3-block tall stone pillar under the weapon (roofY+1 to roofY+3)
                // so the weapon platform is clearly above all wall structures.
                for (int py = 1; py <= 3; py++)
                {
                    plan.Actions.Add(new PlannedBuildAction
                    {
                        ToolMode = BuildToolMode.Single,
                        Material = VoxelMaterialType.Stone,
                        Start = new Vector3I(candidate.X, actualRoofY + py, candidate.Z),
                        End = new Vector3I(candidate.X, actualRoofY + py, candidate.Z),
                        Hollow = false,
                    });
                    tracker.TrySpend(VoxelMaterials.GetDefinition(VoxelMaterialType.Stone).Cost);
                }
                plan.WeaponPlacements.Add((candidate, weaponId));
                placed.Add(candidate);
            }
        }
    }

    /// <summary>
    /// Places weapons at the outer corners of a terrace (stepped tier).
    /// Only uses corners on outward-facing edges.
    /// </summary>
    internal static void PlaceTerraceCornerWeapons(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I tierS, Vector3I tierE, int roofY, Vector3I cmdPos,
        int maxCount, string[] weaponPool, List<Vector3I> allPlaced, Random rng, BuildZone zone)
    {
        int actualRoofY = tierS.Y + roofY;
        // Weapons sit 4 units above the roof on a 3-block tall stone pillar
        // (roofY+1 to roofY+3) to ensure clear line of fire above all
        // crenellations and rooftop structures.
        int weaponY = actualRoofY + 4;

        // Inset corners by 1 unit from the edges to avoid overlapping with
        // perimeter crenellations and wall blocks
        int xInner = tierS.X + 1;
        int xOuter = tierE.X - 1;
        int zInner = tierS.Z + 1;
        int zOuter = tierE.Z - 1;

        // Fallback if tier is too small to inset
        if (xInner > xOuter) { xInner = tierS.X; xOuter = tierE.X; }
        if (zInner > zOuter) { zInner = tierS.Z; zOuter = tierE.Z; }

        // Only include corners on at least one outward-facing edge
        var (useMinX, useMaxX, useMinZ, useMaxZ) = GetOutwardEdges(zone);
        List<Vector3I> cornerList = new List<Vector3I>();

        if (useMinX || useMinZ) cornerList.Add(new Vector3I(xInner, weaponY, zInner));
        if (useMaxX || useMinZ) cornerList.Add(new Vector3I(xOuter, weaponY, zInner));
        if (useMinX || useMaxZ) cornerList.Add(new Vector3I(xInner, weaponY, zOuter));
        if (useMaxX || useMaxZ) cornerList.Add(new Vector3I(xOuter, weaponY, zOuter));

        Vector3I[] corners = cornerList.ToArray();

        // Shuffle to add variety
        for (int i = corners.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (corners[i], corners[j]) = (corners[j], corners[i]);
        }

        const int guaranteedWeapons = 3;

        // Check weapon/pillar positions against existing fortress blocks
        HashSet<Vector3I> plannedBlocks = BuildPlannedBlocksSet(plan);

        int placed = 0;
        foreach (Vector3I corner in corners)
        {
            if (placed >= maxCount) break;
            if (corner.DistanceTo(cmdPos) < GameConfig.MinWeaponCommanderGap) continue;
            if (allPlaced.Exists(p => p.DistanceTo(corner) < 3)) continue;
            if (WeaponOverlapsBlocks(corner, actualRoofY, plannedBlocks)) continue;

            string wId = weaponPool[allPlaced.Count % weaponPool.Length];
            int cost = GetWeaponCost(wId);
            bool guaranteed = allPlaced.Count < guaranteedWeapons;

            if (guaranteed || tracker.TrySpend(cost))
            {
                if (guaranteed) tracker.TrySpend(cost); // best-effort

                // Build a 3-block tall stone pillar under the weapon
                for (int py = 1; py <= 3; py++)
                {
                    Vector3I pillarPos = new Vector3I(corner.X, actualRoofY + py, corner.Z);
                    plan.Actions.Add(new PlannedBuildAction
                    {
                        ToolMode = BuildToolMode.Single,
                        Material = VoxelMaterialType.Stone,
                        Start = pillarPos,
                        End = pillarPos,
                        Hollow = false,
                    });
                    plannedBlocks.Add(pillarPos);
                    tracker.TrySpend(VoxelMaterials.GetDefinition(VoxelMaterialType.Stone).Cost);
                }
                plan.WeaponPlacements.Add((corner, wId));
                allPlaced.Add(corner);
                placed++;
            }
        }
    }

    /// <summary>
    /// Returns the outer corner position of a tower (the corner furthest from
    /// the fortress center), suitable for weapon placement with clear outward fire.
    /// The weapon is placed 4 units above the tower roof so it clears crenellations
    /// and all rooftop structures.  A 3-block pillar must be built by the caller
    /// from towerRoof+1 to towerRoof+3.  The position is also inset by 1 from
    /// each edge so it doesn't overlap with perimeter wall blocks.
    /// </summary>
    private static Vector3I GetOuterCorner(Vector3I towerS, Vector3I towerE,
        Vector3I fortS, Vector3I fortE, int weaponY)
    {
        int fortCenterX = (fortS.X + fortE.X) / 2;
        int fortCenterZ = (fortS.Z + fortE.Z) / 2;
        int towerCenterX = (towerS.X + towerE.X) / 2;
        int towerCenterZ = (towerS.Z + towerE.Z) / 2;

        // Pick the corner furthest from fortress center, but inset by 1 to
        // avoid overlapping with crenellation blocks on the tower perimeter.
        int outerX, outerZ;
        if (towerCenterX < fortCenterX)
            outerX = Math.Min(towerS.X + 1, towerE.X); // inset from left edge
        else
            outerX = Math.Max(towerE.X - 1, towerS.X); // inset from right edge

        if (towerCenterZ < fortCenterZ)
            outerZ = Math.Min(towerS.Z + 1, towerE.Z); // inset from front edge
        else
            outerZ = Math.Max(towerE.Z - 1, towerS.Z); // inset from back edge

        // +3 extra Y so the weapon sits 4 units above tower roof (pillar from +1 to +3)
        return new Vector3I(outerX, towerS.Y + weaponY + 3, outerZ);
    }

    /// <summary>
    /// Ensures at least one cannon exists. Places it on an outward-facing edge
    /// of the roof. Always succeeds regardless of remaining budget.
    /// </summary>
    internal static void EnsureAtLeastOneWeapon(BotBuildPlan plan, BudgetTracker tracker,
        Vector3I bS, Vector3I bE, int roofY, BuildZone zone)
    {
        if (plan.WeaponPlacements.Count > 0) return;

        int actualRoofY = bS.Y + roofY;
        HashSet<Vector3I> plannedBlocks = BuildPlannedBlocksSet(plan);

        // Try outward-facing corners, then edges, to find a non-overlapping spot
        var (useMinX, useMaxX, useMinZ, useMaxZ) = GetOutwardEdges(zone);
        List<Vector3I> fallbackCandidates = new List<Vector3I>();
        // Primary: outward corners inset by 1
        int fbX1 = useMinX ? bS.X + 1 : bE.X - 1;
        int fbZ1 = useMinZ ? bS.Z + 1 : bE.Z - 1;
        fallbackCandidates.Add(new Vector3I(fbX1, actualRoofY + 4, fbZ1));
        // Secondary: opposite corners
        int fbX2 = useMaxX ? bE.X - 1 : bS.X + 1;
        int fbZ2 = useMaxZ ? bE.Z - 1 : bS.Z + 1;
        fallbackCandidates.Add(new Vector3I(fbX2, actualRoofY + 4, fbZ2));
        fallbackCandidates.Add(new Vector3I(fbX1, actualRoofY + 4, fbZ2));
        fallbackCandidates.Add(new Vector3I(fbX2, actualRoofY + 4, fbZ1));
        // Center of roof as last resort
        fallbackCandidates.Add(new Vector3I((bS.X + bE.X) / 2, actualRoofY + 4, (bS.Z + bE.Z) / 2));

        Vector3I fallback = fallbackCandidates[0]; // default
        foreach (Vector3I candidate in fallbackCandidates)
        {
            if (!WeaponOverlapsBlocks(candidate, actualRoofY, plannedBlocks))
            {
                fallback = candidate;
                break;
            }
        }

        // Force-place 3-block stone pillar (skip budget check -- last-resort weapon)
        for (int py = 1; py <= 3; py++)
        {
            plan.Actions.Add(new PlannedBuildAction
            {
                ToolMode = BuildToolMode.Single,
                Material = VoxelMaterialType.Stone,
                Start = new Vector3I(fallback.X, actualRoofY + py, fallback.Z),
                End = new Vector3I(fallback.X, actualRoofY + py, fallback.Z),
                Hollow = false,
            });
            tracker.TrySpend(VoxelMaterials.GetDefinition(VoxelMaterialType.Stone).Cost);
        }

        int cost = GetWeaponCost("cannon");
        if (!tracker.TrySpend(cost))
        {
            GD.Print("[BotBuildPlanner] Budget exhausted, placing free fallback cannon.");
        }
        plan.WeaponPlacements.Add((fallback, "cannon"));
    }

    // ═══════════════════════════════════════════════════════════════
    //  UTILITY
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a set of all block positions already in the plan so weapon
    /// placement methods can detect overlaps with fortress structure.
    /// </summary>
    private static HashSet<Vector3I> BuildPlannedBlocksSet(BotBuildPlan plan)
    {
        HashSet<Vector3I> set = new HashSet<Vector3I>();
        foreach (PlannedBuildAction action in plan.Actions)
        {
            if (action.ToolMode == BuildToolMode.Single)
            {
                set.Add(action.Start);
            }
            else
            {
                for (int ax = action.Start.X; ax <= action.End.X; ax++)
                    for (int ay = action.Start.Y; ay <= action.End.Y; ay++)
                        for (int az = action.Start.Z; az <= action.End.Z; az++)
                            set.Add(new Vector3I(ax, ay, az));
            }
        }
        return set;
    }

    /// <summary>
    /// Returns true if the weapon position or its 3-block support pillar
    /// overlaps with any block in the planned set.
    /// </summary>
    private static bool WeaponOverlapsBlocks(Vector3I weaponPos, int pillarBaseY, HashSet<Vector3I> plannedBlocks)
    {
        if (plannedBlocks.Contains(weaponPos)) return true;
        for (int py = 1; py <= 3; py++)
        {
            if (plannedBlocks.Contains(new Vector3I(weaponPos.X, pillarBaseY + py, weaponPos.Z)))
                return true;
        }
        return false;
    }

    private static int EstimateShellBlockCount(int w, int h, int d)
    {
        if (w <= 0 || h <= 0 || d <= 0) return 0;
        int total = w * h * d;
        int innerW = Math.Max(0, w - 2);
        int innerH = Math.Max(0, h - 2);
        int innerD = Math.Max(0, d - 2);
        int interior = innerW * innerH * innerD;
        return total - interior;
    }

    internal static int GetWeaponCost(string weaponId)
    {
        return weaponId switch
        {
            "cannon" => 50,
            "mortar" => 60,
            "railgun" => 120,
            "missile" => 100,
            "drill" => 150,
            _ => 50,
        };
    }

    // ─────────────────────────────────────────────────
    //  BUDGET TRACKER
    // ─────────────────────────────────────────────────

    internal sealed class BudgetTracker
    {
        private int _remaining;

        public BudgetTracker(int budget)
        {
            _remaining = budget;
        }

        public bool CanSpend(int amount) => _remaining >= amount;

        public bool TrySpend(int amount)
        {
            if (amount <= 0) return true;
            if (_remaining < amount) return false;
            _remaining -= amount;
            return true;
        }
    }
}
