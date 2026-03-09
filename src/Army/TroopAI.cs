using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Voxel;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Army;

/// <summary>
/// Per-troop behavior logic. Called once per round by ArmyManager.TickTroops().
/// Stateless — all state lives in TroopEntity.
/// </summary>
public static class TroopAI
{
    /// <summary>
    /// Execute one tick of troop behavior: pathfind, move, attack.
    /// </summary>
    public static void ExecuteTick(
        TroopEntity troop,
        VoxelWorld world,
        TroopPathfinder pathfinder,
        DoorRegistry doorRegistry,
        Node sceneRoot)
    {
        if (troop.AIState == TroopAIState.Dead || troop.CurrentHP <= 0) return;

        // 1. Find target commander
        Commander.Commander? targetCmd = FindTargetCommander(troop.TargetEnemy, sceneRoot);
        if (targetCmd == null || targetCmd.IsDead)
        {
            troop.SetAIState(TroopAIState.Idle);
            return;
        }

        // 2. Check if already in attack range
        Vector3I cmdMicrovoxel = MathHelpers.WorldToMicrovoxel(targetCmd.GlobalPosition);
        Vector3 troopFloat = new Vector3(troop.CurrentMicrovoxel.X, troop.CurrentMicrovoxel.Y, troop.CurrentMicrovoxel.Z);
        Vector3 cmdFloat = new Vector3(cmdMicrovoxel.X, cmdMicrovoxel.Y, cmdMicrovoxel.Z);
        float dist = troopFloat.DistanceTo(cmdFloat);
        TroopStats stats = TroopDefinitions.Get(troop.Type);

        if (dist <= stats.AttackRange && stats.AttackDamage > 0)
        {
            // Attack the commander
            troop.SetAIState(TroopAIState.Attacking);
            targetCmd.ApplyDamage(stats.AttackDamage, troop.OwnerSlot, troop.GlobalPosition);
            return;
        }

        // 3. Demolisher special: if adjacent to wall and no path through, damage the wall
        if (stats.CanDamageWalls && troop.CurrentPath == null)
        {
            TryDemolishAdjacentWall(troop, world);
        }

        // 4. Pathfind to commander if no current path or path is stale
        if (troop.CurrentPath == null || troop.PathIndex >= troop.CurrentPath.Count)
        {
            Func<Vector3I, bool> doorCheck = pos => doorRegistry.IsDoorVoxel(pos, troop.OwnerSlot);
            troop.CurrentPath = pathfinder.FindPath(world, troop.CurrentMicrovoxel, cmdMicrovoxel, doorCheck);
            troop.PathIndex = 0;

            if (troop.CurrentPath == null)
            {
                // No path — try to find breach point on enemy wall
                troop.SetAIState(TroopAIState.Retreating);
                return;
            }
        }

        // 5. Move along path
        troop.SetAIState(TroopAIState.Marching);
        int stepsToTake = stats.MoveStepsPerTick;
        for (int i = 0; i < stepsToTake && troop.PathIndex < troop.CurrentPath.Count; i++)
        {
            Vector3I next = troop.CurrentPath[troop.PathIndex];
            troop.StartMoveTo(next);
            troop.PathIndex++;
        }
    }

    private static Commander.Commander? FindTargetCommander(PlayerSlot target, Node sceneRoot)
    {
        foreach (Node node in sceneRoot.GetTree().GetNodesInGroup("Commanders"))
        {
            if (node is Commander.Commander cmd && cmd.OwnerSlot == target)
                return cmd;
        }
        return null;
    }

    private static void TryDemolishAdjacentWall(TroopEntity troop, VoxelWorld world)
    {
        // Check 4 horizontal neighbors for solid voxels to demolish
        Vector3I[] dirs = { Vector3I.Right, Vector3I.Left, new(0, 0, 1), new(0, 0, -1) };
        foreach (var dir in dirs)
        {
            Vector3I wallPos = troop.CurrentMicrovoxel + dir;
            VoxelValue voxel = world.GetVoxel(wallPos);
            if (voxel.IsSolid && voxel.Material != VoxelMaterialType.Foundation)
            {
                // Deal 1 HP damage to this voxel
                int newHP = System.Math.Max(0, voxel.HitPoints - 1);
                if (newHP <= 0)
                    world.SetVoxel(wallPos, VoxelValue.Air);
                else
                    world.SetVoxel(wallPos, voxel.WithHitPoints(newHP).WithDamaged(true));

                troop.SetAIState(TroopAIState.Breaching);
                return; // only demolish one block per tick
            }
        }
    }
}
