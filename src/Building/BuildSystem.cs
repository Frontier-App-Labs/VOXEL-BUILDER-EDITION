using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using VoxelSiege.Army;
using VoxelSiege.Core;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Building;

internal sealed class BuildAction
{
    public required PlayerSlot Player { get; init; }
    public required List<Vector3I> Positions { get; init; }
    public required List<VoxelValue> Before { get; init; }
    public required List<VoxelValue> After { get; init; }
    public required int BudgetDelta { get; init; }
}

public partial class BuildSystem : Node
{
    private readonly BuildValidator _validator = new BuildValidator();
    private readonly SymmetryTool _symmetryTool = new SymmetryTool();
    private readonly Stack<BuildAction> _undoStack = new Stack<BuildAction>();
    private readonly Stack<BuildAction> _redoStack = new Stack<BuildAction>();

    [Export]
    public NodePath? GameManagerPath { get; set; }

    [Export]
    public NodePath? VoxelWorldPath { get; set; }

    public BuildToolMode CurrentToolMode { get; set; } = BuildToolMode.Single;
    public VoxelMaterialType CurrentMaterial { get; set; } = VoxelMaterialType.Stone;
    public BuildSymmetryMode SymmetryMode
    {
        get => _symmetryTool.Mode;
        set => _symmetryTool.Mode = value;
    }

    public bool HollowBoxMode { get; set; }

    /// <summary>
    /// When in HalfBlock mode, stores the exact microvoxel position
    /// the cursor is targeting. Set by GameManager each frame.
    /// </summary>
    public Vector3I HalfBlockMicrovoxel { get; set; }

    /// <summary>
    /// Returns symmetry-mirrored microvoxels for ghost preview display.
    /// </summary>
    public List<Vector3I> GetSymmetryMirroredMicrovoxels(BuildZone zone, Vector3I buildUnit)
    {
        var result = new List<Vector3I>();
        if (SymmetryMode == BuildSymmetryMode.None) return result;

        foreach (Vector3I mirrored in _symmetryTool.MirrorBuildUnit(zone, buildUnit))
        {
            if (!zone.ContainsBuildUnit(mirrored)) continue;
            Vector3I microBase = mirrored * GameConfig.MicrovoxelsPerBuildUnit;
            for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit; z++)
                for (int y = 0; y < GameConfig.MicrovoxelsPerBuildUnit; y++)
                    for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit; x++)
                        result.Add(microBase + new Vector3I(x, y, z));
        }
        return result;
    }

    /// <summary>
    /// The currently active blueprint definition (when CurrentToolMode == Blueprint).
    /// Set by the UI/GameManager when the player selects a blueprint preset.
    /// </summary>
    public BlueprintDefinition? ActiveBlueprint { get; set; }

    /// <summary>
    /// Places a blueprint at the given build-unit origin using the player's selected material.
    /// The blueprint offsets are pre-rotated by the caller. Each offset in the blueprint
    /// becomes a full build unit (2x2x2 microvoxels) filled with the player's current material.
    /// </summary>
    public bool TryApplyBlueprint(PlayerSlot playerSlot, BuildZone zone, Vector3I origin, List<Vector3I> rotatedOffsets, out string failureReason)
    {
        failureReason = string.Empty;
        GameManager? gameManager = ResolveGameManager();
        VoxelWorld? world = ResolveWorld();
        if (gameManager == null || world == null)
        {
            failureReason = "Build system is not connected to the game world.";
            return false;
        }

        PlayerData? player = gameManager.GetPlayer(playerSlot);
        if (player == null)
        {
            failureReason = "Player data not found.";
            return false;
        }

        // Build stamp from blueprint offsets
        BuildStamp stamp = new BuildStamp { IsEraseOperation = false };
        foreach (Vector3I offset in rotatedOffsets)
        {
            Vector3I buildUnit = origin + offset;
            stamp.BuildUnits.Add(buildUnit);
            foreach (Vector3I micro in ExpandUniformBuildUnit(buildUnit))
            {
                stamp.Microvoxels.Add(micro);
            }
        }

        // Apply symmetry
        HashSet<Vector3I> symmetricBuildUnits = _symmetryTool.Apply(zone, stamp.BuildUnits);
        if (symmetricBuildUnits.Count > stamp.BuildUnits.Count)
        {
            // Symmetry added extra build units — expand their microvoxels too
            foreach (Vector3I buildUnit in symmetricBuildUnits)
            {
                if (stamp.BuildUnits.Add(buildUnit))
                {
                    foreach (Vector3I micro in ExpandUniformBuildUnit(buildUnit))
                    {
                        stamp.Microvoxels.Add(micro);
                    }
                }
            }
        }

        BuildValidationResult validation = _validator.ValidateStamp(world, player, zone, stamp, CurrentMaterial);
        if (!validation.Success)
        {
            failureReason = validation.Reason;
            return false;
        }

        BuildAction action = CreateBuildAction(playerSlot, world, stamp, validation.BudgetDelta);
        if (!ApplyBudgetDelta(player, action.BudgetDelta))
        {
            failureReason = "Budget application failed.";
            return false;
        }

        ApplyBuildAction(world, action, true);
        _undoStack.Push(action);
        _redoStack.Clear();
        EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(player.Slot, player.Budget, action.BudgetDelta));
        return true;
    }

    /// <summary>
    /// Generates microvoxel positions for a blueprint placed at the given build-unit origin
    /// with the given rotated offsets. Used by GameManager for ghost preview rendering.
    /// </summary>
    public static List<Vector3I> GenerateBlueprintMicrovoxels(Vector3I origin, List<Vector3I> rotatedOffsets)
    {
        List<Vector3I> microvoxels = new List<Vector3I>(rotatedOffsets.Count * GameConfig.MicrovoxelsPerBuildUnit * GameConfig.MicrovoxelsPerBuildUnit * GameConfig.MicrovoxelsPerBuildUnit);
        foreach (Vector3I offset in rotatedOffsets)
        {
            Vector3I buildUnit = origin + offset;
            foreach (Vector3I micro in ExpandUniformBuildUnit(buildUnit))
            {
                microvoxels.Add(micro);
            }
        }

        return microvoxels;
    }

    /// <summary>
    /// Checks whether all build units in a blueprint placement are within the build zone.
    /// Used by GameManager for ghost preview validation.
    /// </summary>
    public static bool ValidateBlueprintInZone(BuildZone zone, Vector3I origin, List<Vector3I> rotatedOffsets)
    {
        foreach (Vector3I offset in rotatedOffsets)
        {
            if (!zone.ContainsBuildUnit(origin + offset))
            {
                return false;
            }
        }

        return true;
    }

    public bool TryApply(PlayerSlot playerSlot, BuildZone zone, Vector3I startBuildUnit, Vector3I endBuildUnit, out string failureReason)
    {
        failureReason = string.Empty;
        GameManager? gameManager = ResolveGameManager();
        VoxelWorld? world = ResolveWorld();
        if (gameManager == null || world == null)
        {
            failureReason = "Build system is not connected to the game world.";
            return false;
        }

        PlayerData? player = gameManager.GetPlayer(playerSlot);
        if (player == null)
        {
            failureReason = "Player data not found.";
            return false;
        }

        BuildStamp stamp = CreateStamp(zone, startBuildUnit, endBuildUnit);
        BuildValidationResult validation = _validator.ValidateStamp(world, player, zone, stamp, CurrentMaterial);
        if (!validation.Success)
        {
            failureReason = validation.Reason;
            return false;
        }

        BuildAction action = CreateBuildAction(playerSlot, world, stamp, validation.BudgetDelta);
        if (stamp.IsEraseOperation)
        {
            action = action.WithBudgetDelta(CalculateRefund(world, stamp));
        }

        if (!ApplyBudgetDelta(player, action.BudgetDelta))
        {
            failureReason = "Budget application failed.";
            return false;
        }

        ApplyBuildAction(world, action, true);
        _undoStack.Push(action);
        _redoStack.Clear();
        EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(player.Slot, player.Budget, action.BudgetDelta));
        return true;
    }

    public void ClearHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    public bool UndoLast(PlayerSlot playerSlot)
    {
        if (_undoStack.Count == 0)
        {
            return false;
        }

        BuildAction action = _undoStack.Pop();
        if (action.Player != playerSlot)
        {
            _undoStack.Push(action);
            return false;
        }

        GameManager? gameManager = ResolveGameManager();
        VoxelWorld? world = ResolveWorld();
        PlayerData? player = gameManager?.GetPlayer(playerSlot);
        if (world == null || player == null)
        {
            return false;
        }

        ApplyBudgetDelta(player, -action.BudgetDelta);
        ApplyBuildAction(world, action, false);
        _redoStack.Push(action);
        EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(player.Slot, player.Budget, -action.BudgetDelta));
        return true;
    }

    public bool RedoLast(PlayerSlot playerSlot)
    {
        if (_redoStack.Count == 0)
        {
            return false;
        }

        BuildAction action = _redoStack.Pop();
        if (action.Player != playerSlot)
        {
            _redoStack.Push(action);
            return false;
        }

        GameManager? gameManager = ResolveGameManager();
        VoxelWorld? world = ResolveWorld();
        PlayerData? player = gameManager?.GetPlayer(playerSlot);
        if (world == null || player == null)
        {
            return false;
        }

        if (!ApplyBudgetDelta(player, action.BudgetDelta))
        {
            _redoStack.Push(action);
            return false;
        }

        ApplyBuildAction(world, action, true);
        _undoStack.Push(action);
        EventBus.Instance?.EmitBudgetChanged(new BudgetChangedEvent(player.Slot, player.Budget, action.BudgetDelta));
        return true;
    }

    private BuildStamp CreateStamp(BuildZone zone, Vector3I startBuildUnit, Vector3I endBuildUnit)
    {
        HashSet<Vector3I> buildUnits = _symmetryTool.Apply(zone, GenerateBuildUnitCells(CurrentToolMode, startBuildUnit, endBuildUnit, HollowBoxMode));
        HashSet<Vector3I> microvoxels = new HashSet<Vector3I>();

        if (CurrentToolMode == BuildToolMode.HalfBlock)
        {
            // HalfBlock: place exactly one microvoxel at the cursor position
            microvoxels.Add(HalfBlockMicrovoxel);
        }
        else
        {
            foreach (Vector3I buildUnit in buildUnits)
            {
                foreach (Vector3I micro in ExpandBuildUnit(buildUnit, CurrentToolMode, startBuildUnit, endBuildUnit))
                {
                    microvoxels.Add(micro);
                }
            }
        }

        BuildStamp stamp = new BuildStamp
        {
            IsEraseOperation = CurrentToolMode == BuildToolMode.Eraser,
        };
        stamp.BuildUnits.UnionWith(buildUnits);
        stamp.Microvoxels.UnionWith(microvoxels);
        return stamp;
    }

    private BuildAction CreateBuildAction(PlayerSlot playerSlot, VoxelWorld world, BuildStamp stamp, int budgetDelta)
    {
        List<Vector3I> positions = stamp.Microvoxels.OrderBy(static p => p.X).ThenBy(static p => p.Y).ThenBy(static p => p.Z).ToList();
        List<VoxelValue> before = new List<VoxelValue>(positions.Count);
        List<VoxelValue> after = new List<VoxelValue>(positions.Count);
        foreach (Vector3I position in positions)
        {
            VoxelValue current = world.GetVoxel(position);
            before.Add(current);
            after.Add(stamp.IsEraseOperation ? VoxelValue.Air : VoxelValue.Create(CurrentMaterial));
        }

        return new BuildAction
        {
            Player = playerSlot,
            Positions = positions,
            Before = before,
            After = after,
            BudgetDelta = budgetDelta,
        };
    }

    private static void ApplyBuildAction(VoxelWorld world, BuildAction action, bool useAfter)
    {
        // Use bulk changes to apply all voxel modifications in one pass.
        // This queues a single remesh per affected chunk instead of one per voxel,
        // preventing floating texture faces caused by async remeshes snapshotting
        // partially-modified chunk data.
        var changes = new List<(Vector3I Position, VoxelValue NewVoxel)>(action.Positions.Count);
        for (int index = 0; index < action.Positions.Count; index++)
        {
            changes.Add((action.Positions[index], useAfter ? action.After[index] : action.Before[index]));
        }
        world.ApplyBulkChanges(changes, action.Player);
    }

    private static bool ApplyBudgetDelta(PlayerData player, int budgetDelta)
    {
        if (budgetDelta < 0)
        {
            return player.TrySpend(-budgetDelta);
        }

        if (budgetDelta > 0)
        {
            player.Refund(budgetDelta);
        }

        return true;
    }

    private static int CalculateRefund(VoxelWorld world, BuildStamp stamp)
    {
        int microPerUnit = GameConfig.MicrovoxelsPerBuildUnit * GameConfig.MicrovoxelsPerBuildUnit * GameConfig.MicrovoxelsPerBuildUnit;
        int solidMicrovoxelCount = 0;
        int totalMaterialCost = 0;

        foreach (Vector3I microvoxel in stamp.Microvoxels)
        {
            VoxelValue voxel = world.GetVoxel(microvoxel);
            if (!voxel.IsAir)
            {
                solidMicrovoxelCount++;
                totalMaterialCost += VoxelMaterials.GetDefinition(voxel.Material).Cost;
            }
        }

        if (solidMicrovoxelCount == 0)
        {
            return 0;
        }

        // Refund proportional to microvoxels actually erased
        return (int)MathF.Ceiling(totalMaterialCost / (float)microPerUnit);
    }

    private static VoxelMaterialType SampleBuildUnitMaterial(VoxelWorld world, Vector3I buildUnit)
    {
        foreach (Vector3I micro in ExpandUniformBuildUnit(buildUnit))
        {
            VoxelValue voxel = world.GetVoxel(micro);
            if (!voxel.IsAir)
            {
                return voxel.Material;
            }
        }

        return VoxelMaterialType.Air;
    }

    internal static IEnumerable<Vector3I> GenerateBuildUnitCells(BuildToolMode toolMode, Vector3I start, Vector3I end, bool hollowBox)
    {
        Vector3I min = new Vector3I(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Min(start.Z, end.Z));
        Vector3I max = new Vector3I(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y), Math.Max(start.Z, end.Z));
        switch (toolMode)
        {
            case BuildToolMode.Single:
            case BuildToolMode.Eraser:
            case BuildToolMode.HalfBlock:
                yield return start;
                break;
            case BuildToolMode.Line:
                foreach (Vector3I point in TraceLine(start, end))
                {
                    yield return point;
                }

                break;
            case BuildToolMode.Wall:
                foreach (Vector3I point in GenerateWall(min, max))
                {
                    yield return point;
                }

                break;
            case BuildToolMode.Box:
                foreach (Vector3I point in GenerateBox(min, max, hollowBox))
                {
                    yield return point;
                }

                break;
            case BuildToolMode.Floor:
                for (int z = min.Z; z <= max.Z; z++)
                {
                    for (int x = min.X; x <= max.X; x++)
                    {
                        yield return new Vector3I(x, start.Y, z);
                    }
                }

                break;
            case BuildToolMode.Ramp:
                foreach (Vector3I point in GenerateRamp(start, end))
                {
                    yield return point;
                }

                break;
            default:
                foreach (Vector3I point in GenerateBox(min, max, false))
                {
                    yield return point;
                }

                break;
        }
    }

    private static IEnumerable<Vector3I> TraceLine(Vector3I start, Vector3I end)
    {
        int dx = end.X - start.X;
        int dy = end.Y - start.Y;
        int dz = end.Z - start.Z;
        int steps = Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz)));
        if (steps == 0)
        {
            yield return start;
            yield break;
        }

        for (int step = 0; step <= steps; step++)
        {
            float t = step / (float)steps;
            yield return new Vector3I(
                Mathf.RoundToInt(Mathf.Lerp(start.X, end.X, t)),
                Mathf.RoundToInt(Mathf.Lerp(start.Y, end.Y, t)),
                Mathf.RoundToInt(Mathf.Lerp(start.Z, end.Z, t)));
        }
    }

    private static IEnumerable<Vector3I> GenerateWall(Vector3I min, Vector3I max)
    {
        int sizeX = max.X - min.X;
        int sizeY = max.Y - min.Y;
        int sizeZ = max.Z - min.Z;
        if (sizeX <= sizeZ)
        {
            int x = min.X;
            for (int z = min.Z; z <= max.Z; z++)
            {
                for (int y = min.Y; y <= max.Y; y++)
                {
                    yield return new Vector3I(x, y, z);
                }
            }

            yield break;
        }

        int zFixed = min.Z;
        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                yield return new Vector3I(x, y, zFixed);
            }
        }
    }

    private static IEnumerable<Vector3I> GenerateBox(Vector3I min, Vector3I max, bool hollow)
    {
        for (int z = min.Z; z <= max.Z; z++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    bool isShell = x == min.X || x == max.X || y == min.Y || y == max.Y || z == min.Z || z == max.Z;
                    if (!hollow || isShell)
                    {
                        yield return new Vector3I(x, y, z);
                    }
                }
            }
        }
    }

    private static IEnumerable<Vector3I> GenerateRamp(Vector3I start, Vector3I end)
    {
        int horizontalSteps = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Z - start.Z));
        horizontalSteps = Math.Max(1, horizontalSteps);
        for (int step = 0; step <= horizontalSteps; step++)
        {
            float t = step / (float)horizontalSteps;
            int x = Mathf.RoundToInt(Mathf.Lerp(start.X, end.X, t));
            int z = Mathf.RoundToInt(Mathf.Lerp(start.Z, end.Z, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(start.Y, end.Y, t));
            yield return new Vector3I(x, y, z);
            if (y > start.Y)
            {
                for (int fillY = start.Y; fillY < y; fillY++)
                {
                    yield return new Vector3I(x, fillY, z);
                }
            }
        }
    }

    internal static IEnumerable<Vector3I> ExpandBuildUnit(Vector3I buildUnit, BuildToolMode toolMode, Vector3I start, Vector3I end)
    {
        switch (toolMode)
        {
            case BuildToolMode.Ramp:
                foreach (Vector3I micro in ExpandRampBuildUnit(buildUnit, start, end))
                {
                    yield return micro;
                }

                yield break;
            case BuildToolMode.Wall:
                foreach (Vector3I micro in ExpandWallBuildUnit(buildUnit, start, end))
                {
                    yield return micro;
                }

                yield break;
            case BuildToolMode.Floor:
                foreach (Vector3I micro in ExpandFloorBuildUnit(buildUnit))
                {
                    yield return micro;
                }

                yield break;
            case BuildToolMode.Door:
                foreach (Vector3I micro in ExpandDoorBuildUnit(buildUnit))
                {
                    yield return micro;
                }

                yield break;
            case BuildToolMode.HalfBlock:
                // HalfBlock yields just a single microvoxel (the base corner).
                // The actual microvoxel position is resolved in CreateStamp.
                yield return buildUnit * GameConfig.MicrovoxelsPerBuildUnit;
                yield break;
            default:
                foreach (Vector3I micro in ExpandUniformBuildUnit(buildUnit))
                {
                    yield return micro;
                }

                yield break;
        }
    }

    private static IEnumerable<Vector3I> ExpandUniformBuildUnit(Vector3I buildUnit)
    {
        Vector3I microBase = buildUnit * GameConfig.MicrovoxelsPerBuildUnit;
        for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit; z++)
        {
            for (int y = 0; y < GameConfig.MicrovoxelsPerBuildUnit; y++)
            {
                for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit; x++)
                {
                    yield return microBase + new Vector3I(x, y, z);
                }
            }
        }
    }

    /// <summary>
    /// Expands a build unit into a thin door-shaped column: 1 wide, 1 deep, DoorHeight tall.
    /// </summary>
    private static IEnumerable<Vector3I> ExpandDoorBuildUnit(Vector3I buildUnit)
    {
        Vector3I microBase = buildUnit * GameConfig.MicrovoxelsPerBuildUnit;
        for (int dy = 0; dy < DoorRegistry.DoorHeight; dy++)
        {
            yield return microBase + new Vector3I(0, dy, 0);
        }
    }

    private static IEnumerable<Vector3I> ExpandRampBuildUnit(Vector3I buildUnit, Vector3I start, Vector3I end)
    {
        Vector3I microBase = buildUnit * GameConfig.MicrovoxelsPerBuildUnit;
        int riseDirection = Math.Sign(end.Y - start.Y);
        if (riseDirection == 0)
        {
            riseDirection = 1;
        }

        for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit; z++)
        {
            for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit; x++)
            {
                int height = riseDirection > 0 ? Math.Max(1, x + 1) : Math.Max(1, GameConfig.MicrovoxelsPerBuildUnit - x);
                for (int y = 0; y < height; y++)
                {
                    yield return microBase + new Vector3I(x, y, z);
                }
            }
        }
    }

    /// <summary>
    /// Expands a wall build unit to 1 microvoxel thick, 2 build units (4 microvoxels) tall.
    /// Orientation is determined by the drag direction: if the drag spans more in X than Z,
    /// the wall runs along X (thin in Z). Otherwise it runs along Z (thin in X).
    /// For single-click placement (start == end), defaults to thin in Z.
    /// The non-thin axis always has full build-unit width (2 microvoxels).
    /// </summary>
    private static IEnumerable<Vector3I> ExpandWallBuildUnit(Vector3I buildUnit, Vector3I start, Vector3I end)
    {
        // Wall is 2 build units tall (4 microvoxels = 2m), 1 microvoxel thick
        const int WallHeight = GameConfig.MicrovoxelsPerBuildUnit * 2; // 4 microvoxels = 2 build units tall
        Vector3I microBase = buildUnit * GameConfig.MicrovoxelsPerBuildUnit;
        int spanX = Math.Abs(end.X - start.X);
        int spanZ = Math.Abs(end.Z - start.Z);

        // Determine thin axis: wall running along X is thin in Z, wall running along Z is thin in X.
        // When spans are equal or zero (single click), default to thin in Z.
        bool thinInZ = spanX >= spanZ;

        // Width on the non-thin axis is always full build unit width (2 microvoxels).
        // Thickness on the thin axis is always 1 microvoxel.
        int widthX = thinInZ ? GameConfig.MicrovoxelsPerBuildUnit : 1;
        int widthZ = thinInZ ? 1 : GameConfig.MicrovoxelsPerBuildUnit;

        for (int z = 0; z < widthZ; z++)
        {
            for (int y = 0; y < WallHeight; y++)
            {
                for (int x = 0; x < widthX; x++)
                {
                    yield return microBase + new Vector3I(x, y, z);
                }
            }
        }
    }

    /// <summary>
    /// Expands a floor build unit to only the bottom layer (y=0) of microvoxels,
    /// producing a 0.5m deep floor slab.
    /// </summary>
    private static IEnumerable<Vector3I> ExpandFloorBuildUnit(Vector3I buildUnit)
    {
        Vector3I microBase = buildUnit * GameConfig.MicrovoxelsPerBuildUnit;
        for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit; z++)
        {
            for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit; x++)
            {
                yield return microBase + new Vector3I(x, 0, z);
            }
        }
    }

    private GameManager? ResolveGameManager()
    {
        return GameManagerPath is null ? GetTree().Root.GetNodeOrNull<GameManager>("Main") : GetNodeOrNull<GameManager>(GameManagerPath);
    }

    private VoxelWorld? ResolveWorld()
    {
        return VoxelWorldPath is null ? GetTree().Root.GetNodeOrNull<VoxelWorld>("Main/GameWorld") : GetNodeOrNull<VoxelWorld>(VoxelWorldPath);
    }
}

internal static class BuildActionExtensions
{
    public static BuildAction WithBudgetDelta(this BuildAction action, int budgetDelta)
    {
        return new BuildAction
        {
            Player = action.Player,
            Positions = action.Positions,
            Before = action.Before,
            After = action.After,
            BudgetDelta = budgetDelta,
        };
    }
}
