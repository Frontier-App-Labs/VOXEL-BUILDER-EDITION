using Godot;
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

public sealed class PlannedBuildAction
{
    public BuildToolMode ToolMode { get; set; }
    public VoxelMaterialType Material { get; set; }
    public Vector3I Start { get; set; }
    public Vector3I End { get; set; }
    public bool Hollow { get; set; }
}

public sealed class BotBuildPlanner
{
    public List<PlannedBuildAction> CreatePlan(BotDifficulty difficulty, BuildZone zone)
    {
        List<PlannedBuildAction> plan = new List<PlannedBuildAction>();
        Vector3I shellStart = zone.OriginBuildUnits + new Vector3I(1, 0, 1);
        Vector3I shellEnd = shellStart + new Vector3I(7, difficulty == BotDifficulty.Easy ? 3 : 4, 7);
        plan.Add(new PlannedBuildAction { ToolMode = BuildToolMode.Box, Material = VoxelMaterialType.Stone, Start = shellStart, End = shellEnd, Hollow = true });
        plan.Add(new PlannedBuildAction { ToolMode = BuildToolMode.Box, Material = difficulty == BotDifficulty.Hard ? VoxelMaterialType.ReinforcedSteel : VoxelMaterialType.Brick, Start = shellStart + new Vector3I(2, 1, 2), End = shellStart + new Vector3I(4, 2, 4), Hollow = true });
        if (difficulty != BotDifficulty.Easy)
        {
            plan.Add(new PlannedBuildAction { ToolMode = BuildToolMode.Wall, Material = VoxelMaterialType.Concrete, Start = shellStart + new Vector3I(0, 0, 4), End = shellStart + new Vector3I(7, 3, 4), Hollow = false });
        }

        return plan;
    }
}
