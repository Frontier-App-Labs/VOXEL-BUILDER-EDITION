using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using VoxelSiege.Building;
using VoxelSiege.Combat;
using VoxelSiege.Core;
using VoxelSiege.Networking;
using VoxelSiege.Voxel;
using CommanderActor = VoxelSiege.Commander.Commander;

namespace VoxelSiege.AI;

/// <summary>
/// Main bot brain that orchestrates build and combat phases.
/// Attached to a player slot, the bot uses BotBuildPlanner to generate
/// fortress designs during the build phase and BotCombatPlanner to
/// select targets and aim during combat.
/// </summary>
public partial class BotController : Node
{
    private readonly BotBuildPlanner _buildPlanner = new BotBuildPlanner();
    private readonly BotCombatPlanner _combatPlanner = new BotCombatPlanner();
    private Random _rng = new Random();

    [Export]
    public BotDifficulty Difficulty { get; set; } = BotDifficulty.Medium;

    /// <summary>
    /// The player slot this bot controls.
    /// </summary>
    [Export]
    public PlayerSlot PlayerSlot { get; set; } = PlayerSlot.Player2;

    // ─────────────────────────────────────────────────
    //  BUILD PHASE
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Returns a list of PlannedBuildAction for the GameManager to execute.
    /// This is the simple API used by the existing GameManager integration.
    /// </summary>
    public IEnumerable<PlannedBuildAction> PlanBuild(BuildZone zone)
    {
        int budget = GetBudgetForDifficulty(Difficulty);
        BotBuildPlan plan = _buildPlanner.CreateFullPlan(Difficulty, zone, budget);
        return plan.Actions;
    }

    /// <summary>
    /// Returns a full build plan including commander and weapon positions.
    /// Use this when the caller needs to know where the bot wants its
    /// commander and weapons placed.
    /// </summary>
    public BotBuildPlan PlanFullBuild(BuildZone zone, int budget)
    {
        return _buildPlanner.CreateFullPlan(Difficulty, zone, budget);
    }

    /// <summary>
    /// Executes the build phase for this bot: generates a fortress design,
    /// places voxels in the world, and returns placement info for commander
    /// and weapons.
    /// </summary>
    /// <param name="world">The voxel world to place blocks in.</param>
    /// <param name="zone">The build zone assigned to this bot.</param>
    /// <param name="player">The player data for budget tracking.</param>
    /// <returns>The complete build plan with commander and weapon positions.</returns>
    public BotBuildPlan RunBuildPhase(VoxelWorld world, BuildZone zone, PlayerData player)
    {
        _rng = new Random(System.Environment.TickCount ^ PlayerSlot.GetHashCode());
        int budget = GetBudgetForDifficulty(Difficulty);
        BotBuildPlan plan = _buildPlanner.CreateFullPlan(Difficulty, zone, budget);

        // Execute all build actions by stamping voxels directly into the world
        foreach (PlannedBuildAction action in plan.Actions)
        {
            ExecuteBuildAction(world, action, player);
        }

        return plan;
    }

    // ─────────────────────────────────────────────────
    //  COMBAT PHASE
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Simple shot planning using only the weapon and target commander.
    /// This is the API used by the existing GameManager integration.
    /// </summary>
    public AimUpdatePayload PlanShot(WeaponBase weapon, CommanderActor target)
    {
        return _combatPlanner.CreateAim(weapon, target, Difficulty);
    }

    /// <summary>
    /// Runs a full combat turn for this bot: selects a target, chooses
    /// the best weapon, calculates aim, and returns the firing parameters.
    /// </summary>
    /// <param name="world">The voxel world for structure analysis.</param>
    /// <param name="players">All player data for target selection.</param>
    /// <param name="aimingSystem">The aiming system to configure.</param>
    /// <param name="weapons">This bot's available weapons.</param>
    /// <param name="turnManager">The turn manager for round tracking.</param>
    /// <returns>
    /// The weapon and aim payload to use, or null if no valid action is available.
    /// </returns>
    public (WeaponBase Weapon, AimUpdatePayload Aim)? RunCombatTurn(
        VoxelWorld world,
        IReadOnlyDictionary<PlayerSlot, PlayerData> players,
        AimingSystem aimingSystem,
        List<WeaponBase> weapons,
        TurnManager turnManager)
    {
        if (weapons.Count == 0)
        {
            return null;
        }

        _rng = new Random(System.Environment.TickCount ^ turnManager.RoundNumber);

        // Find enemy targets
        List<(PlayerSlot Slot, PlayerData Data)> enemies = new List<(PlayerSlot, PlayerData)>();
        foreach ((PlayerSlot slot, PlayerData data) in players)
        {
            if (slot != PlayerSlot && data.IsAlive)
            {
                enemies.Add((slot, data));
            }
        }

        if (enemies.Count == 0)
        {
            return null;
        }

        // Select target based on difficulty
        (PlayerSlot targetSlot, PlayerData targetData) = SelectTarget(enemies);

        // Get the enemy's build zone for aiming
        BuildZone enemyZone = targetData.AssignedBuildZone;

        // Select the best weapon
        int weaponIndex = _combatPlanner.SelectWeapon(weapons, turnManager.RoundNumber, enemyZone, Difficulty, _rng);
        if (weaponIndex < 0)
        {
            return null;
        }

        WeaponBase weapon = weapons[weaponIndex];

        // Calculate aim
        AimUpdatePayload aim = _combatPlanner.CreateAimExtended(
            weapon, enemyZone, targetSlot, Difficulty, _rng, world);

        // Apply aim to the aiming system
        aimingSystem.YawRadians = aim.YawRadians;
        aimingSystem.PitchRadians = aim.PitchRadians;
        aimingSystem.PowerPercent = aim.PowerPercent;

        return (weapon, aim);
    }

    /// <summary>
    /// Records the result of a shot for learning in subsequent turns.
    /// </summary>
    public void RecordShotResult(PlayerSlot enemySlot, Vector3I hitPosition)
    {
        _combatPlanner.RecordHit(enemySlot, hitPosition);
    }

    /// <summary>
    /// Resets the bot's combat history for a new match.
    /// </summary>
    public void ResetForMatch()
    {
        _combatPlanner.ResetHistory();
    }

    // ─────────────────────────────────────────────────
    //  TARGET SELECTION
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Records damage dealt to this bot by an attacker, building a threat
    /// profile used for weighted target selection.
    /// </summary>
    public void RecordDamageReceived(PlayerSlot attackerSlot, int damage)
    {
        _combatPlanner.RecordDamageReceived(attackerSlot, damage);
    }

    /// <summary>
    /// Returns a dictionary of threat scores (damage received from each enemy).
    /// Used by GameManager for the static target-selection path.
    /// </summary>
    public Dictionary<PlayerSlot, int> GetThreatScores(
        List<(PlayerSlot Slot, PlayerData Data)> enemies)
    {
        Dictionary<PlayerSlot, int> scores = new Dictionary<PlayerSlot, int>();
        foreach (var e in enemies)
        {
            scores[e.Slot] = _combatPlanner.GetThreatFrom(e.Slot);
        }
        return scores;
    }

    private (PlayerSlot Slot, PlayerData Data) SelectTarget(
        List<(PlayerSlot Slot, PlayerData Data)> enemies)
    {
        if (enemies.Count == 1)
        {
            return enemies[0];
        }

        // Build threat scores from the combat planner's damage-received history
        Dictionary<PlayerSlot, int> threatScores = new Dictionary<PlayerSlot, int>();
        foreach (var e in enemies)
        {
            threatScores[e.Slot] = _combatPlanner.GetThreatFrom(e.Slot);
        }

        // Use the shared weighted random selection system
        return BotCombatPlanner.SelectTargetStatic(
            enemies, Difficulty, _rng, threatScores, botZoneCenter: null);
    }

    // ─────────────────────────────────────────────────
    //  BUDGET HELPERS
    // ─────────────────────────────────────────────────

    private static int GetBudgetForDifficulty(BotDifficulty difficulty)
    {
        return difficulty switch
        {
            BotDifficulty.Easy => GameConfig.BotBudgetEasy,
            BotDifficulty.Medium => GameConfig.BotBudgetMedium,
            BotDifficulty.Hard => GameConfig.BotBudgetHard,
            _ => GameConfig.BotBudgetMedium,
        };
    }

    // ─────────────────────────────────────────────────
    //  BUILD EXECUTION
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Stamps a build action into the voxel world by expanding build units
    /// to microvoxels and setting them directly.
    /// </summary>
    private void ExecuteBuildAction(VoxelWorld world, PlannedBuildAction action, PlayerData player)
    {
        Vector3I min = new Vector3I(
            Math.Min(action.Start.X, action.End.X),
            Math.Min(action.Start.Y, action.End.Y),
            Math.Min(action.Start.Z, action.End.Z));
        Vector3I max = new Vector3I(
            Math.Max(action.Start.X, action.End.X),
            Math.Max(action.Start.Y, action.End.Y),
            Math.Max(action.Start.Z, action.End.Z));

        int w = max.X - min.X + 1;
        int h = max.Y - min.Y + 1;
        int d = max.Z - min.Z + 1;

        switch (action.ToolMode)
        {
            case BuildToolMode.Box:
                StampBox(world, min, max, action.Material, action.Hollow, player);
                break;

            case BuildToolMode.Wall:
                StampWall(world, min, max, action.Material, player);
                break;

            case BuildToolMode.Floor:
                StampFloor(world, min, max, action.Material, player);
                break;

            case BuildToolMode.Single:
                StampBuildUnit(world, action.Start, action.Material, player);
                break;

            default:
                // For Line, Ramp, etc. -- fall back to box
                StampBox(world, min, max, action.Material, action.Hollow, player);
                break;
        }
    }

    private static void StampBox(VoxelWorld world, Vector3I min, Vector3I max,
        VoxelMaterialType material, bool hollow, PlayerData player)
    {
        for (int z = min.Z; z <= max.Z; z++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    // For single-layer hollow (min.Y == max.Y), only X/Z
                    // edges are shell — otherwise y == min.Y is always true
                    // and every cell would be "shell", filling the interior.
                    bool isShell;
                    if (min.Y == max.Y)
                    {
                        isShell = x == min.X || x == max.X ||
                                  z == min.Z || z == max.Z;
                    }
                    else
                    {
                        isShell = x == min.X || x == max.X ||
                                  y == min.Y || y == max.Y ||
                                  z == min.Z || z == max.Z;
                    }

                    if (!hollow || isShell)
                    {
                        StampBuildUnit(world, new Vector3I(x, y, z), material, player);
                    }
                }
            }
        }
    }

    private static void StampWall(VoxelWorld world, Vector3I min, Vector3I max,
        VoxelMaterialType material, PlayerData player)
    {
        for (int z = min.Z; z <= max.Z; z++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    StampBuildUnit(world, new Vector3I(x, y, z), material, player);
                }
            }
        }
    }

    private static void StampFloor(VoxelWorld world, Vector3I min, Vector3I max,
        VoxelMaterialType material, PlayerData player)
    {
        for (int z = min.Z; z <= max.Z; z++)
        {
            for (int x = min.X; x <= max.X; x++)
            {
                StampBuildUnit(world, new Vector3I(x, min.Y, z), material, player);
            }
        }
    }

    /// <summary>
    /// Stamps a single build unit into the world by expanding it to microvoxels.
    /// Deducts cost from the player budget.
    /// </summary>
    private static void StampBuildUnit(VoxelWorld world, Vector3I buildUnitPosition,
        VoxelMaterialType material, PlayerData player)
    {
        // Note: Budget was already tracked by BotBuildPlanner.BudgetTracker during
        // plan generation. Do NOT deduct again here or the bot runs out at ~50% build.

        Vector3I microBase = buildUnitPosition * GameConfig.MicrovoxelsPerBuildUnit;
        for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit; z++)
        {
            for (int y = 0; y < GameConfig.MicrovoxelsPerBuildUnit; y++)
            {
                for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit; x++)
                {
                    world.SetVoxel(
                        microBase + new Vector3I(x, y, z),
                        Voxel.Voxel.Create(material),
                        player.Slot);
                }
            }
        }

        player.Stats.VoxelsPlaced++;
    }
}
