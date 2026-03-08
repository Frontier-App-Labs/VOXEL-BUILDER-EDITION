using Godot;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Commander;
using VoxelSiege.Combat;
using VoxelSiege.Core;
using VoxelSiege.Networking;
using CommanderActor = VoxelSiege.Commander.Commander;

namespace VoxelSiege.AI;

public partial class BotController : Node
{
    private readonly BotBuildPlanner _buildPlanner = new BotBuildPlanner();
    private readonly BotCombatPlanner _combatPlanner = new BotCombatPlanner();

    [Export]
    public BotDifficulty Difficulty { get; set; } = BotDifficulty.Medium;

    public IEnumerable<PlannedBuildAction> PlanBuild(BuildZone zone)
    {
        return _buildPlanner.CreatePlan(Difficulty, zone);
    }

    public AimUpdatePayload PlanShot(WeaponBase weapon, CommanderActor target)
    {
        return _combatPlanner.CreateAim(weapon, target, Difficulty);
    }
}
