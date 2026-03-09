using Godot;
using System;
using System.Collections.Generic;
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
/// Each difficulty produces structurally distinct, randomized fortresses
/// that stay within budget and maintain ground connectivity.
/// </summary>
public sealed class BotBuildPlanner
{
    // ─────────────────────────────────────────────────
    //  PUBLIC API
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a build plan as a list of PlannedBuildAction for the GameManager to execute.
    /// This is the entry point called by BotController.
    /// </summary>
    public List<PlannedBuildAction> CreatePlan(BotDifficulty difficulty, BuildZone zone)
    {
        BotBuildPlan plan = CreateFullPlan(difficulty, zone, GameConfig.DefaultBudget);
        return plan.Actions;
    }

    /// <summary>
    /// Creates a complete build plan including commander and weapon positions.
    /// </summary>
    public BotBuildPlan CreateFullPlan(BotDifficulty difficulty, BuildZone zone, int budget)
    {
        Random rng = new Random(System.Environment.TickCount ^ zone.OriginBuildUnits.GetHashCode());

        return difficulty switch
        {
            BotDifficulty.Easy => PlanEasyFortress(zone, budget, rng),
            BotDifficulty.Medium => PlanMediumFortress(zone, budget, rng),
            BotDifficulty.Hard => PlanHardFortress(zone, budget, rng),
            _ => PlanEasyFortress(zone, budget, rng),
        };
    }

    // ─────────────────────────────────────────────────
    //  EASY FORTRESS
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Simple rectangular box (8x5x8 build units).
    /// Random cheap materials. Commander roughly at center.
    /// 1-2 weapons on top.
    /// </summary>
    private static BotBuildPlan PlanEasyFortress(BuildZone zone, int budget, Random rng)
    {
        BotBuildPlan plan = new BotBuildPlan();
        BudgetTracker tracker = new BudgetTracker(budget);

        // Randomize dimensions slightly: width 6-10, height 4-6, depth 6-10
        int width = 6 + rng.Next(5);   // 6..10
        int height = 4 + rng.Next(3);  // 4..6
        int depth = 6 + rng.Next(5);   // 6..10

        // Clamp to zone bounds
        width = Math.Min(width, zone.SizeBuildUnits.X - 2);
        height = Math.Min(height, zone.SizeBuildUnits.Y - 2);
        depth = Math.Min(depth, zone.SizeBuildUnits.Z - 2);

        // Random offset within zone
        int maxOffsetX = zone.SizeBuildUnits.X - width - 2;
        int maxOffsetZ = zone.SizeBuildUnits.Z - depth - 2;
        int offsetX = maxOffsetX > 0 ? 1 + rng.Next(maxOffsetX) : 1;
        int offsetZ = maxOffsetZ > 0 ? 1 + rng.Next(maxOffsetZ) : 1;

        Vector3I shellStart = zone.OriginBuildUnits + new Vector3I(offsetX, 0, offsetZ);
        Vector3I shellEnd = shellStart + new Vector3I(width - 1, height - 1, depth - 1);

        // Pick cheap materials randomly
        VoxelMaterialType[] cheapMaterials = { VoxelMaterialType.Dirt, VoxelMaterialType.Wood, VoxelMaterialType.Stone };
        VoxelMaterialType shellMaterial = cheapMaterials[rng.Next(cheapMaterials.Length)];

        int shellCost = EstimateHollowBoxCost(width, height, depth, shellMaterial);
        if (!tracker.TrySpend(shellCost))
        {
            // Fall back to dirt if too expensive
            shellMaterial = VoxelMaterialType.Dirt;
            shellCost = EstimateHollowBoxCost(width, height, depth, VoxelMaterialType.Dirt);
            tracker.TrySpend(shellCost);
        }

        plan.Actions.Add(new PlannedBuildAction
        {
            ToolMode = BuildToolMode.Box,
            Material = shellMaterial,
            Start = shellStart,
            End = shellEnd,
            Hollow = true,
        });

        // Commander at center of the box, one unit above floor
        Vector3I commanderPos = shellStart + new Vector3I(width / 2, 1, depth / 2);
        plan.CommanderBuildUnit = commanderPos;

        // 1-2 weapons on top
        int weaponCount = 1 + rng.Next(2);
        int topY = shellEnd.Y + 1;
        string[] easyWeapons = { "cannon", "mortar" };

        for (int i = 0; i < weaponCount; i++)
        {
            int wx = shellStart.X + 1 + rng.Next(Math.Max(1, width - 2));
            int wz = shellStart.Z + rng.Next(2); // near front
            Vector3I weaponPos = new Vector3I(wx, topY, wz);

            // Ensure weapon isn't too close to commander
            if (weaponPos.DistanceTo(commanderPos) >= GameConfig.MinWeaponCommanderGap)
            {
                string weaponId = easyWeapons[rng.Next(easyWeapons.Length)];
                int weaponCost = GetWeaponCost(weaponId);
                if (tracker.TrySpend(weaponCost))
                {
                    plan.WeaponPlacements.Add((weaponPos, weaponId));
                }
            }
        }

        // If no weapons placed, force at least one cannon
        if (plan.WeaponPlacements.Count == 0)
        {
            Vector3I fallbackWeaponPos = new Vector3I(shellStart.X + 1, topY, shellStart.Z);
            int cannonCost = GetWeaponCost("cannon");
            if (tracker.TrySpend(cannonCost))
            {
                plan.WeaponPlacements.Add((fallbackWeaponPos, "cannon"));
            }
        }

        return plan;
    }

    // ─────────────────────────────────────────────────
    //  MEDIUM FORTRESS
    // ─────────────────────────────────────────────────

    /// <summary>
    /// L-shaped or cross-shaped base with interior rooms.
    /// Mix of materials. Commander placed in interior room, off-center.
    /// 2-3 weapons spread across exterior.
    /// </summary>
    private static BotBuildPlan PlanMediumFortress(BuildZone zone, int budget, Random rng)
    {
        BotBuildPlan plan = new BotBuildPlan();
        BudgetTracker tracker = new BudgetTracker(budget);

        // Choose between L-shape (0) and cross-shape (1) and T-shape (2)
        int shapeVariant = rng.Next(3);

        Vector3I origin = zone.OriginBuildUnits;
        int zoneW = zone.SizeBuildUnits.X;
        int zoneH = zone.SizeBuildUnits.Y;
        int zoneD = zone.SizeBuildUnits.Z;

        // Primary block dimensions
        int mainW = Math.Min(10 + rng.Next(3), zoneW - 2);  // 10..12
        int mainH = Math.Min(5 + rng.Next(2), zoneH - 2);   // 5..6
        int mainD = Math.Min(8 + rng.Next(3), zoneD - 2);   // 8..10

        // Center the main block in the zone
        int mainOffX = Math.Max(1, (zoneW - mainW) / 2);
        int mainOffZ = Math.Max(1, (zoneD - mainD) / 2);

        Vector3I mainStart = origin + new Vector3I(mainOffX, 0, mainOffZ);
        Vector3I mainEnd = mainStart + new Vector3I(mainW - 1, mainH - 1, mainD - 1);

        // Outer shell: stone
        VoxelMaterialType outerMaterial = VoxelMaterialType.Stone;
        int mainCost = EstimateHollowBoxCost(mainW, mainH, mainD, outerMaterial);
        if (tracker.TrySpend(mainCost))
        {
            plan.Actions.Add(new PlannedBuildAction
            {
                ToolMode = BuildToolMode.Box,
                Material = outerMaterial,
                Start = mainStart,
                End = mainEnd,
                Hollow = true,
            });
        }

        // Secondary wing based on shape variant
        int wingW, wingH, wingD;
        Vector3I wingStart;

        switch (shapeVariant)
        {
            case 0: // L-shape: wing extends from one side
            {
                wingW = 4 + rng.Next(3);  // 4..6
                wingH = mainH;
                wingD = 5 + rng.Next(3);  // 5..7
                wingW = Math.Min(wingW, zoneW - mainOffX - mainW);
                if (wingW < 3) wingW = 3;
                bool leftSide = rng.Next(2) == 0;
                int wingX = leftSide
                    ? mainStart.X - wingW
                    : mainEnd.X + 1;
                wingX = Math.Clamp(wingX, origin.X + 1, origin.X + zoneW - wingW - 1);
                int wingZ = mainStart.Z + (rng.Next(2) == 0 ? 0 : mainD - wingD);
                wingZ = Math.Clamp(wingZ, origin.Z + 1, origin.Z + zoneD - wingD - 1);
                wingStart = new Vector3I(wingX, 0 + origin.Y, wingZ);
                break;
            }
            case 1: // Cross-shape: wing extends on the Z axis
            {
                wingW = 4 + rng.Next(2);
                wingH = mainH;
                wingD = mainD + 2 + rng.Next(3);
                wingD = Math.Min(wingD, zoneD - 2);
                int wingX = mainStart.X + (mainW - wingW) / 2;
                int wingZ = origin.Z + Math.Max(1, (zoneD - wingD) / 2);
                wingStart = new Vector3I(wingX, origin.Y, wingZ);
                break;
            }
            default: // T-shape: wing extends from front
            {
                wingW = mainW + 2 + rng.Next(3);
                wingW = Math.Min(wingW, zoneW - 2);
                wingH = mainH;
                wingD = 3 + rng.Next(2);
                int wingX = origin.X + Math.Max(1, (zoneW - wingW) / 2);
                int wingZ = mainStart.Z - wingD;
                wingZ = Math.Max(origin.Z + 1, wingZ);
                wingStart = new Vector3I(wingX, origin.Y, wingZ);
                break;
            }
        }

        // Clamp wing dimensions
        wingW = Math.Max(3, wingW);
        wingD = Math.Max(3, wingD);
        Vector3I wingEnd = wingStart + new Vector3I(wingW - 1, wingH - 1, wingD - 1);

        // Clamp wing end to zone
        wingEnd = ClampToZone(wingEnd, zone);
        wingStart = ClampToZone(wingStart, zone);

        VoxelMaterialType wingMaterial = VoxelMaterialType.Brick;
        int wingCost = EstimateHollowBoxCost(
            wingEnd.X - wingStart.X + 1,
            wingEnd.Y - wingStart.Y + 1,
            wingEnd.Z - wingStart.Z + 1,
            wingMaterial);
        if (tracker.TrySpend(wingCost))
        {
            plan.Actions.Add(new PlannedBuildAction
            {
                ToolMode = BuildToolMode.Box,
                Material = wingMaterial,
                Start = wingStart,
                End = wingEnd,
                Hollow = true,
            });
        }

        // Interior reinforced core around commander
        int coreW = 3;
        int coreH = 3;
        int coreD = 3;
        // Place commander off-center in the main block
        int cmdOffX = mainW / 2 + (rng.Next(3) - 1); // center +/- 1
        int cmdOffZ = mainD / 2 + (rng.Next(3) - 1);
        cmdOffX = Math.Clamp(cmdOffX, 2, mainW - 3);
        cmdOffZ = Math.Clamp(cmdOffZ, 2, mainD - 3);

        Vector3I cmdPos = mainStart + new Vector3I(cmdOffX, 1, cmdOffZ);
        plan.CommanderBuildUnit = cmdPos;

        // Build reinforced shell around commander
        Vector3I coreStart = cmdPos - new Vector3I(1, 0, 1);
        Vector3I coreEnd = cmdPos + new Vector3I(1, coreH - 1, 1);
        VoxelMaterialType coreMaterial = VoxelMaterialType.Concrete;
        int coreCost = EstimateHollowBoxCost(coreW, coreH, coreD, coreMaterial);
        if (tracker.TrySpend(coreCost))
        {
            plan.Actions.Add(new PlannedBuildAction
            {
                ToolMode = BuildToolMode.Box,
                Material = coreMaterial,
                Start = coreStart,
                End = coreEnd,
                Hollow = true,
            });
        }

        // Interior dividing wall for variety
        if (tracker.CanSpend(EstimateWallCost(mainW, mainH, VoxelMaterialType.Concrete)))
        {
            int wallZ = mainStart.Z + mainD / 2 + (rng.Next(3) - 1);
            wallZ = Math.Clamp(wallZ, mainStart.Z + 2, mainEnd.Z - 2);
            int wallCost = EstimateWallCost(mainW, mainH, VoxelMaterialType.Concrete);
            if (tracker.TrySpend(wallCost))
            {
                plan.Actions.Add(new PlannedBuildAction
                {
                    ToolMode = BuildToolMode.Wall,
                    Material = VoxelMaterialType.Concrete,
                    Start = new Vector3I(mainStart.X, mainStart.Y, wallZ),
                    End = new Vector3I(mainEnd.X, mainEnd.Y, wallZ),
                    Hollow = false,
                });
            }
        }

        // 2-3 weapons spread across the fortress
        int weaponCount = 2 + rng.Next(2);
        string[] mediumWeapons = { "cannon", "mortar", "cannon", "railgun" };
        int topY = mainEnd.Y + 1;

        List<Vector3I> weaponPositions = new List<Vector3I>();
        for (int i = 0; i < weaponCount; i++)
        {
            Vector3I candidatePos = GenerateWeaponPosition(mainStart, mainEnd, wingStart, wingEnd, topY, rng, i, weaponPositions, cmdPos);
            string weaponId = mediumWeapons[rng.Next(mediumWeapons.Length)];
            int weaponCost = GetWeaponCost(weaponId);
            if (tracker.TrySpend(weaponCost))
            {
                plan.WeaponPlacements.Add((candidatePos, weaponId));
                weaponPositions.Add(candidatePos);
            }
        }

        // Ensure at least one weapon
        if (plan.WeaponPlacements.Count == 0)
        {
            Vector3I fallback = new Vector3I(mainStart.X + 1, topY, mainStart.Z);
            if (tracker.TrySpend(GetWeaponCost("cannon")))
            {
                plan.WeaponPlacements.Add((fallback, "cannon"));
            }
        }

        return plan;
    }

    // ─────────────────────────────────────────────────
    //  HARD FORTRESS
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Complex multi-room fortress with thick walls, strategic material layering,
    /// obsidian/reinforced steel commander chamber, decoy rooms, and 3-4 weapons.
    /// </summary>
    private static BotBuildPlan PlanHardFortress(BuildZone zone, int budget, Random rng)
    {
        BotBuildPlan plan = new BotBuildPlan();
        BudgetTracker tracker = new BudgetTracker(budget);

        Vector3I origin = zone.OriginBuildUnits;
        int zoneW = zone.SizeBuildUnits.X;
        int zoneH = zone.SizeBuildUnits.Y;
        int zoneD = zone.SizeBuildUnits.Z;

        // Outer dimensions: large, fills most of the zone
        int outerW = Math.Min(zoneW - 2, 14 + rng.Next(4));   // 14..17
        int outerH = Math.Min(zoneH - 2, 7 + rng.Next(3));    // 7..9
        int outerD = Math.Min(zoneD - 2, 14 + rng.Next(4));   // 14..17

        int offX = Math.Max(1, (zoneW - outerW) / 2);
        int offZ = Math.Max(1, (zoneD - outerD) / 2);

        Vector3I outerStart = origin + new Vector3I(offX, 0, offZ);
        Vector3I outerEnd = outerStart + new Vector3I(outerW - 1, outerH - 1, outerD - 1);

        // ── Layer 1: Outer shell (stone) ──
        VoxelMaterialType outerMat = VoxelMaterialType.Stone;
        int outerCost = EstimateHollowBoxCost(outerW, outerH, outerD, outerMat);
        if (tracker.TrySpend(outerCost))
        {
            plan.Actions.Add(new PlannedBuildAction
            {
                ToolMode = BuildToolMode.Box,
                Material = outerMat,
                Start = outerStart,
                End = outerEnd,
                Hollow = true,
            });
        }

        // ── Layer 2: Inner reinforced shell (concrete) ──
        int innerW = outerW - 4;
        int innerH = outerH - 2;
        int innerD = outerD - 4;
        if (innerW >= 6 && innerH >= 4 && innerD >= 6)
        {
            Vector3I innerStart = outerStart + new Vector3I(2, 0, 2);
            Vector3I innerEnd = innerStart + new Vector3I(innerW - 1, innerH - 1, innerD - 1);
            VoxelMaterialType innerMat = VoxelMaterialType.Concrete;
            int innerCost = EstimateHollowBoxCost(innerW, innerH, innerD, innerMat);
            if (tracker.TrySpend(innerCost))
            {
                plan.Actions.Add(new PlannedBuildAction
                {
                    ToolMode = BuildToolMode.Box,
                    Material = innerMat,
                    Start = innerStart,
                    End = innerEnd,
                    Hollow = true,
                });
            }
        }

        // ── Commander chamber: buried deep, off-center, random quadrant ──
        int quadrant = rng.Next(4);
        int cmdOffX, cmdOffZ;
        switch (quadrant)
        {
            case 0: // front-left
                cmdOffX = outerW / 4 + rng.Next(2);
                cmdOffZ = outerD / 4 + rng.Next(2);
                break;
            case 1: // front-right
                cmdOffX = outerW * 3 / 4 - rng.Next(2);
                cmdOffZ = outerD / 4 + rng.Next(2);
                break;
            case 2: // back-left
                cmdOffX = outerW / 4 + rng.Next(2);
                cmdOffZ = outerD * 3 / 4 - rng.Next(2);
                break;
            default: // back-right
                cmdOffX = outerW * 3 / 4 - rng.Next(2);
                cmdOffZ = outerD * 3 / 4 - rng.Next(2);
                break;
        }

        cmdOffX = Math.Clamp(cmdOffX, 3, outerW - 4);
        cmdOffZ = Math.Clamp(cmdOffZ, 3, outerD - 4);

        Vector3I cmdPos = outerStart + new Vector3I(cmdOffX, 1, cmdOffZ);
        plan.CommanderBuildUnit = cmdPos;

        // ── Commander vault: obsidian core ──
        int vaultSize = 3; // 3x3x3 hollow box around commander
        Vector3I vaultStart = cmdPos - new Vector3I(1, 0, 1);
        Vector3I vaultEnd = cmdPos + new Vector3I(1, vaultSize - 1, 1);

        // Obsidian layer (respect the MaxObsidianBlocks limit)
        VoxelMaterialType vaultMat = VoxelMaterialType.Obsidian;
        int obsidianCount = EstimateShellBlockCount(vaultSize, vaultSize, vaultSize);
        if (obsidianCount > GameConfig.MaxObsidianBlocks)
        {
            // Use reinforced steel for the vault shell, obsidian just for the floor
            vaultMat = VoxelMaterialType.ReinforcedSteel;
        }

        int vaultCost = EstimateHollowBoxCost(vaultSize, vaultSize, vaultSize, vaultMat);
        if (tracker.TrySpend(vaultCost))
        {
            plan.Actions.Add(new PlannedBuildAction
            {
                ToolMode = BuildToolMode.Box,
                Material = vaultMat,
                Start = vaultStart,
                End = vaultEnd,
                Hollow = true,
            });
        }

        // Additional reinforced steel wrapping around the vault
        Vector3I rsStart = vaultStart - Vector3I.One;
        Vector3I rsEnd = vaultEnd + Vector3I.One;
        rsStart = new Vector3I(
            Math.Max(rsStart.X, outerStart.X + 1),
            Math.Max(rsStart.Y, outerStart.Y),
            Math.Max(rsStart.Z, outerStart.Z + 1));
        rsEnd = new Vector3I(
            Math.Min(rsEnd.X, outerEnd.X - 1),
            Math.Min(rsEnd.Y, outerEnd.Y - 1),
            Math.Min(rsEnd.Z, outerEnd.Z - 1));

        int rsW = rsEnd.X - rsStart.X + 1;
        int rsH = rsEnd.Y - rsStart.Y + 1;
        int rsD = rsEnd.Z - rsStart.Z + 1;
        if (rsW >= 3 && rsH >= 3 && rsD >= 3)
        {
            VoxelMaterialType rsMat = VoxelMaterialType.ReinforcedSteel;
            int rsCost = EstimateHollowBoxCost(rsW, rsH, rsD, rsMat);
            if (tracker.TrySpend(rsCost))
            {
                plan.Actions.Add(new PlannedBuildAction
                {
                    ToolMode = BuildToolMode.Box,
                    Material = rsMat,
                    Start = rsStart,
                    End = rsEnd,
                    Hollow = true,
                });
            }
        }

        // ── Decoy rooms: reinforced rooms that look important but are empty ──
        int decoyCount = 1 + rng.Next(2); // 1-2 decoys
        for (int d = 0; d < decoyCount; d++)
        {
            // Place decoy in a different quadrant than the commander
            int decoyQuadrant = (quadrant + 1 + d) % 4;
            int decoyX, decoyZ;
            switch (decoyQuadrant)
            {
                case 0:
                    decoyX = outerW / 4;
                    decoyZ = outerD / 4;
                    break;
                case 1:
                    decoyX = outerW * 3 / 4;
                    decoyZ = outerD / 4;
                    break;
                case 2:
                    decoyX = outerW / 4;
                    decoyZ = outerD * 3 / 4;
                    break;
                default:
                    decoyX = outerW * 3 / 4;
                    decoyZ = outerD * 3 / 4;
                    break;
            }

            decoyX = Math.Clamp(decoyX, 3, outerW - 4);
            decoyZ = Math.Clamp(decoyZ, 3, outerD - 4);

            Vector3I decoyCenter = outerStart + new Vector3I(decoyX, 1, decoyZ);
            Vector3I decoyStart = decoyCenter - new Vector3I(1, 0, 1);
            Vector3I decoyEnd = decoyCenter + new Vector3I(1, 2, 1);

            // Use same material as the vault to look like the commander room
            VoxelMaterialType decoyMat = vaultMat;
            int decoyCost = EstimateHollowBoxCost(3, 3, 3, decoyMat);
            if (tracker.TrySpend(decoyCost))
            {
                plan.Actions.Add(new PlannedBuildAction
                {
                    ToolMode = BuildToolMode.Box,
                    Material = decoyMat,
                    Start = decoyStart,
                    End = decoyEnd,
                    Hollow = true,
                });
            }
        }

        // ── Internal dividing walls for structural complexity ──
        int wallCount = 2 + rng.Next(2);
        for (int w = 0; w < wallCount; w++)
        {
            bool xAligned = rng.Next(2) == 0;
            VoxelMaterialType wallMat = w % 2 == 0 ? VoxelMaterialType.Concrete : VoxelMaterialType.Metal;

            if (xAligned)
            {
                int wallZ = outerStart.Z + 3 + rng.Next(Math.Max(1, outerD - 6));
                wallZ = Math.Clamp(wallZ, outerStart.Z + 2, outerEnd.Z - 2);
                int wCost = EstimateWallCost(outerW, outerH, wallMat);
                if (tracker.TrySpend(wCost))
                {
                    plan.Actions.Add(new PlannedBuildAction
                    {
                        ToolMode = BuildToolMode.Wall,
                        Material = wallMat,
                        Start = new Vector3I(outerStart.X + 1, outerStart.Y, wallZ),
                        End = new Vector3I(outerEnd.X - 1, outerEnd.Y - 1, wallZ),
                        Hollow = false,
                    });
                }
            }
            else
            {
                int wallX = outerStart.X + 3 + rng.Next(Math.Max(1, outerW - 6));
                wallX = Math.Clamp(wallX, outerStart.X + 2, outerEnd.X - 2);
                int wCost = EstimateWallCost(outerD, outerH, wallMat);
                if (tracker.TrySpend(wCost))
                {
                    plan.Actions.Add(new PlannedBuildAction
                    {
                        ToolMode = BuildToolMode.Wall,
                        Material = wallMat,
                        Start = new Vector3I(wallX, outerStart.Y, outerStart.Z + 1),
                        End = new Vector3I(wallX, outerEnd.Y - 1, outerEnd.Z - 1),
                        Hollow = false,
                    });
                }
            }
        }

        // ── Metal armor plating on exterior faces ──
        // Front face reinforcement
        int armorCost = EstimateWallCost(outerW, outerH, VoxelMaterialType.Metal);
        if (tracker.TrySpend(armorCost))
        {
            plan.Actions.Add(new PlannedBuildAction
            {
                ToolMode = BuildToolMode.Wall,
                Material = VoxelMaterialType.Metal,
                Start = outerStart,
                End = new Vector3I(outerEnd.X, outerEnd.Y, outerStart.Z),
                Hollow = false,
            });
        }

        // ── 3-4 weapons in strategic positions ──
        int weaponCount = 3 + rng.Next(2);
        string[] hardWeapons = { "cannon", "mortar", "railgun", "drill", "missile" };
        int topY = outerEnd.Y + 1;

        List<Vector3I> wpPositions = new List<Vector3I>();

        // Place weapons at corners and strategic positions
        Vector3I[] preferredPositions =
        {
            new Vector3I(outerStart.X + 1, topY, outerStart.Z),                // front-left
            new Vector3I(outerEnd.X - 1, topY, outerStart.Z),                  // front-right
            new Vector3I(outerStart.X + outerW / 2, topY, outerStart.Z),       // front-center
            new Vector3I(outerStart.X + 1, topY, outerEnd.Z),                  // back-left
            new Vector3I(outerEnd.X - 1, topY, outerEnd.Z),                    // back-right
        };

        // Shuffle preferred positions for variety
        for (int i = preferredPositions.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (preferredPositions[i], preferredPositions[j]) = (preferredPositions[j], preferredPositions[i]);
        }

        for (int i = 0; i < Math.Min(weaponCount, preferredPositions.Length); i++)
        {
            Vector3I wPos = preferredPositions[i];
            if (wPos.DistanceTo(cmdPos) < GameConfig.MinWeaponCommanderGap)
            {
                continue;
            }

            // Strategic weapon selection
            string weaponId = SelectStrategicWeapon(wPos, outerStart, outerEnd, hardWeapons, rng);
            int wCost = GetWeaponCost(weaponId);
            if (tracker.TrySpend(wCost))
            {
                plan.WeaponPlacements.Add((wPos, weaponId));
                wpPositions.Add(wPos);
            }
        }

        // Ensure at least one weapon
        if (plan.WeaponPlacements.Count == 0)
        {
            Vector3I fallback = new Vector3I(outerStart.X + 1, topY, outerStart.Z);
            if (tracker.TrySpend(GetWeaponCost("cannon")))
            {
                plan.WeaponPlacements.Add((fallback, "cannon"));
            }
        }

        return plan;
    }

    // ─────────────────────────────────────────────────
    //  UTILITY METHODS
    // ─────────────────────────────────────────────────

    private static int EstimateHollowBoxCost(int w, int h, int d, VoxelMaterialType material)
    {
        int shellCount = EstimateShellBlockCount(w, h, d);
        return shellCount * VoxelMaterials.GetDefinition(material).Cost;
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

    private static int EstimateWallCost(int width, int height, VoxelMaterialType material)
    {
        return width * height * VoxelMaterials.GetDefinition(material).Cost;
    }

    private static int GetWeaponCost(string weaponId)
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

    private static Vector3I ClampToZone(Vector3I pos, BuildZone zone)
    {
        Vector3I min = zone.OriginBuildUnits;
        Vector3I max = zone.OriginBuildUnits + zone.SizeBuildUnits - Vector3I.One;
        return new Vector3I(
            Math.Clamp(pos.X, min.X, max.X),
            Math.Clamp(pos.Y, min.Y, max.Y),
            Math.Clamp(pos.Z, min.Z, max.Z));
    }

    private static Vector3I GenerateWeaponPosition(
        Vector3I mainStart, Vector3I mainEnd,
        Vector3I wingStart, Vector3I wingEnd,
        int topY, Random rng, int index,
        List<Vector3I> existingPositions, Vector3I cmdPos)
    {
        // Spread weapons across different parts of the fortress
        Vector3I candidate;
        int attempts = 0;
        do
        {
            if (index % 2 == 0)
            {
                // Place on main block
                int wx = mainStart.X + 1 + rng.Next(Math.Max(1, mainEnd.X - mainStart.X - 1));
                int wz = mainStart.Z + rng.Next(2); // near front
                candidate = new Vector3I(wx, topY, wz);
            }
            else
            {
                // Place on wing
                int wx = wingStart.X + 1 + rng.Next(Math.Max(1, wingEnd.X - wingStart.X - 1));
                int wz = wingStart.Z + rng.Next(Math.Max(1, wingEnd.Z - wingStart.Z));
                candidate = new Vector3I(wx, Math.Max(topY, wingEnd.Y + 1), wz);
            }

            attempts++;
        }
        while (attempts < 10 && (candidate.DistanceTo(cmdPos) < GameConfig.MinWeaponCommanderGap ||
               existingPositions.Exists(p => p.DistanceTo(candidate) < 2)));

        return candidate;
    }

    /// <summary>
    /// Selects the best weapon type for a given position relative to the fortress.
    /// Front-facing positions get offensive weapons, corners get area-denial.
    /// </summary>
    private static string SelectStrategicWeapon(Vector3I weaponPos, Vector3I fortStart, Vector3I fortEnd, string[] available, Random rng)
    {
        bool atFront = weaponPos.Z <= fortStart.Z + 1;
        bool atCorner = (weaponPos.X <= fortStart.X + 1 || weaponPos.X >= fortEnd.X - 1);

        if (atFront && atCorner)
        {
            // Corners: railgun for precision shots or mortar for area coverage
            return rng.Next(2) == 0 ? "railgun" : "mortar";
        }

        if (atFront)
        {
            // Front center: cannon or drill for penetration
            return rng.Next(3) switch
            {
                0 => "cannon",
                1 => "drill",
                _ => "missile",
            };
        }

        // Back positions: mortar for high-arc shots over obstacles
        return rng.Next(2) == 0 ? "mortar" : "cannon";
    }

    // ─────────────────────────────────────────────────
    //  BUDGET TRACKER
    // ─────────────────────────────────────────────────

    private sealed class BudgetTracker
    {
        private int _remaining;

        public BudgetTracker(int budget)
        {
            _remaining = budget;
        }

        public bool CanSpend(int amount) => _remaining >= amount;

        public bool TrySpend(int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            if (_remaining < amount)
            {
                return false;
            }

            _remaining -= amount;
            return true;
        }
    }
}
