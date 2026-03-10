using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Building;

public readonly record struct BuildZone(Vector3I OriginBuildUnits, Vector3I SizeBuildUnits)
{
    public Vector3I OriginMicrovoxels => MathHelpers.BuildToMicrovoxel(OriginBuildUnits);
    public Vector3I SizeMicrovoxels => SizeBuildUnits * GameConfig.MicrovoxelsPerBuildUnit;
    public Vector3I MaxMicrovoxelsInclusive => OriginMicrovoxels + SizeMicrovoxels - Vector3I.One;

    public bool ContainsBuildUnit(Vector3I buildUnitPosition)
    {
        return buildUnitPosition.X >= OriginBuildUnits.X
            && buildUnitPosition.Y >= OriginBuildUnits.Y
            && buildUnitPosition.Z >= OriginBuildUnits.Z
            && buildUnitPosition.X < OriginBuildUnits.X + SizeBuildUnits.X
            && buildUnitPosition.Y < OriginBuildUnits.Y + SizeBuildUnits.Y
            && buildUnitPosition.Z < OriginBuildUnits.Z + SizeBuildUnits.Z;
    }

    public bool ContainsMicrovoxel(Vector3I microvoxelPosition)
    {
        return microvoxelPosition.X >= OriginMicrovoxels.X
            && microvoxelPosition.Y >= OriginMicrovoxels.Y
            && microvoxelPosition.Z >= OriginMicrovoxels.Z
            && microvoxelPosition.X <= MaxMicrovoxelsInclusive.X
            && microvoxelPosition.Y <= MaxMicrovoxelsInclusive.Y
            && microvoxelPosition.Z <= MaxMicrovoxelsInclusive.Z;
    }
}

public sealed class BuildStamp
{
    public HashSet<Vector3I> BuildUnits { get; } = new HashSet<Vector3I>();
    public HashSet<Vector3I> Microvoxels { get; } = new HashSet<Vector3I>();
    public bool IsEraseOperation { get; init; }

    public bool IsEmpty => BuildUnits.Count == 0 || Microvoxels.Count == 0;
}

public readonly record struct BuildValidationResult(bool Success, string Reason, int BudgetDelta)
{
    public static BuildValidationResult Passed(int budgetDelta) => new BuildValidationResult(true, string.Empty, budgetDelta);
    public static BuildValidationResult Failed(string reason) => new BuildValidationResult(false, reason, 0);
}

public sealed class BuildValidator
{
    public BuildValidationResult ValidateStamp(VoxelWorld world, PlayerData player, BuildZone zone, BuildStamp stamp, VoxelMaterialType material)
    {
        if (stamp.IsEmpty)
        {
            return BuildValidationResult.Failed("No build cells were selected.");
        }

        foreach (Vector3I buildUnit in stamp.BuildUnits)
        {
            if (!zone.ContainsBuildUnit(buildUnit))
            {
                return BuildValidationResult.Failed("Placement is outside the build zone.");
            }
        }

        foreach (Vector3I microvoxel in stamp.Microvoxels)
        {
            if (!zone.ContainsMicrovoxel(microvoxel))
            {
                return BuildValidationResult.Failed("Microvoxel stamp exceeded the build zone.");
            }
        }

        if (!stamp.IsEraseOperation)
        {
            int microPerUnit = GameConfig.MicrovoxelsPerBuildUnit * GameConfig.MicrovoxelsPerBuildUnit * GameConfig.MicrovoxelsPerBuildUnit;
            int materialCost = VoxelMaterials.GetDefinition(material).Cost;
            int placementCost = (int)System.MathF.Ceiling((stamp.Microvoxels.Count * materialCost) / (float)microPerUnit);
            if (!player.CanSpend(placementCost))
            {
                return BuildValidationResult.Failed("Not enough budget.");
            }

            if (material == VoxelMaterialType.ArmorPlate && !IsExteriorPlacement(world, stamp))
            {
                return BuildValidationResult.Failed("Armor plate must be placed on the exterior.");
            }

            if (!TouchesExistingSupport(world, zone, stamp))
            {
                return BuildValidationResult.Failed("Build pieces must attach to the ground or your current structure.");
            }

            return BuildValidationResult.Passed(-placementCost);
        }

        if (!WouldRemainConnectedAfterErase(world, zone, stamp))
        {
            return BuildValidationResult.Failed("Removing these pieces would leave unsupported floating sections.");
        }

        return BuildValidationResult.Passed(0);
    }

    public bool ValidateCommanderPlacement(VoxelWorld world, BuildZone zone, Vector3I commanderBuildUnitsBase, IReadOnlyCollection<Vector3I> weaponBuildPositions)
    {
        Vector3I microBase = MathHelpers.BuildToMicrovoxel(commanderBuildUnitsBase);
        Vector3I size = new Vector3I(GameConfig.MicrovoxelsPerBuildUnit, GameConfig.MicrovoxelsPerBuildUnit * 2, GameConfig.MicrovoxelsPerBuildUnit);
        Vector3I max = microBase + size - Vector3I.One;
        if (!zone.ContainsBuildUnit(commanderBuildUnitsBase) || !zone.ContainsBuildUnit(commanderBuildUnitsBase + Vector3I.Up))
        {
            return false;
        }

        foreach (Vector3I weaponPosition in weaponBuildPositions)
        {
            if (weaponPosition.DistanceTo(commanderBuildUnitsBase) < GameConfig.MinWeaponCommanderGap)
            {
                return false;
            }
        }

        Vector3I[] directions =
        {
            Vector3I.Left,
            Vector3I.Right,
            Vector3I.Down,
            Vector3I.Up,
            Vector3I.Back,
            Vector3I.Forward,
        };
        foreach (Vector3I direction in directions)
        {
            bool blocked = false;
            foreach (Vector3I surfaceVoxel in EnumerateCommanderSurface(microBase, max, direction))
            {
                if (world.GetVoxel(surfaceVoxel + direction).IsSolid)
                {
                    blocked = true;
                    break;
                }
            }

            if (!blocked)
            {
                return false;
            }
        }

        return true;
    }

    public bool ValidateWeaponPlacement(VoxelWorld world, BuildZone zone, Vector3I weaponBuildUnit)
    {
        // Weapons must be placed inside the player's build zone, just like blocks.
        if (!zone.ContainsBuildUnit(weaponBuildUnit))
        {
            return false;
        }

        Vector3I microBase = MathHelpers.BuildToMicrovoxel(weaponBuildUnit);
        for (int z = 0; z < GameConfig.MicrovoxelsPerBuildUnit; z++)
        {
            for (int y = 0; y < GameConfig.MicrovoxelsPerBuildUnit; y++)
            {
                for (int x = 0; x < GameConfig.MicrovoxelsPerBuildUnit; x++)
                {
                    Vector3I pos = microBase + new Vector3I(x, y, z);
                    if (HasExteriorNeighbor(world, pos))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool TouchesExistingSupport(VoxelWorld world, BuildZone zone, BuildStamp stamp)
    {
        foreach (Vector3I microvoxel in stamp.Microvoxels)
        {
            if (microvoxel.Y == zone.OriginMicrovoxels.Y)
            {
                return true;
            }

            foreach (Vector3I neighbor in EnumerateNeighbors(microvoxel))
            {
                if (!zone.ContainsMicrovoxel(neighbor))
                {
                    return true;
                }

                if (!stamp.Microvoxels.Contains(neighbor) && world.GetVoxel(neighbor).IsSolid)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsExteriorPlacement(VoxelWorld world, BuildStamp stamp)
    {
        foreach (Vector3I microvoxel in stamp.Microvoxels)
        {
            if (HasExteriorNeighbor(world, microvoxel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasExteriorNeighbor(VoxelWorld world, Vector3I microvoxel)
    {
        foreach (Vector3I neighbor in EnumerateNeighbors(microvoxel))
        {
            if (world.GetVoxel(neighbor).IsAir)
            {
                return true;
            }
        }

        return false;
    }

    private static bool WouldRemainConnectedAfterErase(VoxelWorld world, BuildZone zone, BuildStamp stamp)
    {
        Queue<Vector3I> frontier = new Queue<Vector3I>();
        HashSet<Vector3I> connected = new HashSet<Vector3I>();
        int solidCount = 0;

        for (int z = zone.OriginMicrovoxels.Z; z <= zone.MaxMicrovoxelsInclusive.Z; z++)
        {
            for (int y = zone.OriginMicrovoxels.Y; y <= zone.MaxMicrovoxelsInclusive.Y; y++)
            {
                for (int x = zone.OriginMicrovoxels.X; x <= zone.MaxMicrovoxelsInclusive.X; x++)
                {
                    Vector3I position = new Vector3I(x, y, z);
                    if (stamp.Microvoxels.Contains(position))
                    {
                        continue;
                    }

                    if (world.GetVoxel(position).IsAir)
                    {
                        continue;
                    }

                    solidCount++;
                    if (y == zone.OriginMicrovoxels.Y)
                    {
                        frontier.Enqueue(position);
                        connected.Add(position);
                    }
                }
            }
        }

        while (frontier.Count > 0)
        {
            Vector3I current = frontier.Dequeue();
            foreach (Vector3I neighbor in EnumerateNeighbors(current))
            {
                if (!zone.ContainsMicrovoxel(neighbor) || stamp.Microvoxels.Contains(neighbor) || connected.Contains(neighbor) || world.GetVoxel(neighbor).IsAir)
                {
                    continue;
                }

                connected.Add(neighbor);
                frontier.Enqueue(neighbor);
            }
        }

        return connected.Count == solidCount;
    }

    private static IEnumerable<Vector3I> EnumerateCommanderSurface(Vector3I min, Vector3I max, Vector3I direction)
    {
        for (int z = min.Z; z <= max.Z; z++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    Vector3I position = new Vector3I(x, y, z);
                    if ((direction == Vector3I.Left && x == min.X)
                        || (direction == Vector3I.Right && x == max.X)
                        || (direction == Vector3I.Down && y == min.Y)
                        || (direction == Vector3I.Up && y == max.Y)
                        || (direction == Vector3I.Back && z == min.Z)
                        || (direction == Vector3I.Forward && z == max.Z))
                    {
                        yield return position;
                    }
                }
            }
        }
    }

    private static IEnumerable<Vector3I> EnumerateNeighbors(Vector3I origin)
    {
        yield return origin + Vector3I.Left;
        yield return origin + Vector3I.Right;
        yield return origin + Vector3I.Down;
        yield return origin + Vector3I.Up;
        yield return origin + Vector3I.Back;
        yield return origin + Vector3I.Forward;
    }
}
