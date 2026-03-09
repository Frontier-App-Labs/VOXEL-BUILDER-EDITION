using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Voxel;
using VoxelSiege.Core;
using VoxelSiege.Building;
using VoxelSiege.Utility;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Army;

/// <summary>
/// Per-troop behavior logic. Called once per round by ArmyManager.TickTroops().
/// Stateless — all state lives in TroopEntity.
///
/// Troop lifecycle:
///   1. Spawned inside own base at HomeMicrovoxel (ExitingBase)
///   2. Pathfind from HomeMicrovoxel through own door to door exterior (ExitingBase)
///   3. Pathfind across open terrain to enemy base door or enemy commander (Marching)
///   4. Attack enemy commander when in range (Attacking)
///   5. After attacking, pathfind back to own door exterior (Returning)
///   6. Pathfind from own door through base back to HomeMicrovoxel (EnteringBase)
///   7. Idle at HomeMicrovoxel (Idle)
/// </summary>
public static class TroopAI
{
    /// <summary>
    /// Execute one tick of troop behavior using the door-based pathfinding state machine.
    /// </summary>
    public static void ExecuteTick(
        TroopEntity troop,
        VoxelWorld world,
        TroopPathfinder pathfinder,
        DoorRegistry doorRegistry,
        Dictionary<PlayerSlot, BuildZone> buildZones,
        Node sceneRoot)
    {
        if (troop.AIState == TroopAIState.Dead || troop.CurrentHP <= 0) return;

        switch (troop.AIState)
        {
            case TroopAIState.Idle:
                HandleIdle(troop, doorRegistry, buildZones);
                break;

            case TroopAIState.ExitingBase:
                HandleExitingBase(troop, world, pathfinder, doorRegistry, buildZones, sceneRoot);
                break;

            case TroopAIState.Marching:
                HandleMarching(troop, world, pathfinder, doorRegistry, buildZones, sceneRoot);
                break;

            case TroopAIState.Breaching:
                HandleBreaching(troop, world);
                break;

            case TroopAIState.Attacking:
                HandleAttacking(troop, world, pathfinder, doorRegistry, buildZones, sceneRoot);
                break;

            case TroopAIState.Returning:
                HandleReturning(troop, world, pathfinder, doorRegistry, buildZones, sceneRoot);
                break;

            case TroopAIState.EnteringBase:
                HandleEnteringBase(troop, world, pathfinder, doorRegistry, buildZones);
                break;
        }
    }

    /// <summary>
    /// Idle troops that haven't attacked yet start by exiting the base.
    /// </summary>
    private static void HandleIdle(
        TroopEntity troop,
        DoorRegistry doorRegistry,
        Dictionary<PlayerSlot, BuildZone> buildZones)
    {
        if (troop.HasAttacked)
            return; // Already completed cycle, stay idle at home

        // Start the exit sequence
        troop.SetAIState(TroopAIState.ExitingBase);
        troop.CurrentPath = null;
        troop.PathIndex = 0;
    }

    /// <summary>
    /// Pathfind from inside the base through own door to the door exterior.
    /// </summary>
    private static void HandleExitingBase(
        TroopEntity troop,
        VoxelWorld world,
        TroopPathfinder pathfinder,
        DoorRegistry doorRegistry,
        Dictionary<PlayerSlot, BuildZone> buildZones,
        Node sceneRoot)
    {
        // Find our nearest door
        DoorPlacement? ownDoor = doorRegistry.FindNearestDoor(troop.OwnerSlot, troop.CurrentMicrovoxel);
        if (ownDoor == null)
        {
            // No door placed — can't exit base; go straight to marching from current position
            GD.Print($"[TroopAI] {troop.Name}: No door found, marching from current position");
            troop.SetAIState(TroopAIState.Marching);
            troop.CurrentPath = null;
            troop.PathIndex = 0;
            return;
        }

        // Calculate the exterior position just outside the door
        Vector3I doorExterior = GetDoorExterior(ownDoor, troop.OwnerSlot, buildZones, doorRegistry);

        // If we're already outside, switch to marching
        if (troop.CurrentMicrovoxel == doorExterior || troop.CurrentMicrovoxel == ownDoor.BaseMicrovoxel)
        {
            troop.SetAIState(TroopAIState.Marching);
            troop.CurrentPath = null;
            troop.PathIndex = 0;
            return;
        }

        // Pathfind from current position to door exterior (through own door)
        if (troop.CurrentPath == null || troop.PathIndex >= troop.CurrentPath.Count)
        {
            Func<Vector3I, bool> doorCheck = pos => doorRegistry.IsDoorVoxelAnyPlayer(pos);
            troop.CurrentPath = pathfinder.FindPath(world, troop.CurrentMicrovoxel, doorExterior, doorCheck);
            troop.PathIndex = 0;

            if (troop.CurrentPath == null)
            {
                // Can't find path to door from inside — try the door base position itself
                troop.CurrentPath = pathfinder.FindPath(world, troop.CurrentMicrovoxel, ownDoor.BaseMicrovoxel, doorCheck);
                troop.PathIndex = 0;

                if (troop.CurrentPath == null)
                {
                    // Still no path — skip to marching from current position
                    GD.Print($"[TroopAI] {troop.Name}: Can't pathfind to door, marching from current pos");
                    troop.SetAIState(TroopAIState.Marching);
                    troop.CurrentPath = null;
                    troop.PathIndex = 0;
                    return;
                }
            }
        }

        // Move along the exit path
        MoveAlongPath(troop);

        // Check if we've arrived outside
        if (troop.PathIndex >= troop.CurrentPath.Count)
        {
            troop.SetAIState(TroopAIState.Marching);
            troop.CurrentPath = null;
            troop.PathIndex = 0;
        }
    }

    /// <summary>
    /// March across open terrain toward the enemy base/commander.
    /// </summary>
    private static void HandleMarching(
        TroopEntity troop,
        VoxelWorld world,
        TroopPathfinder pathfinder,
        DoorRegistry doorRegistry,
        Dictionary<PlayerSlot, BuildZone> buildZones,
        Node sceneRoot)
    {
        // Find target commander
        Commander.Commander? targetCmd = FindTargetCommander(troop.TargetEnemy, sceneRoot);
        if (targetCmd == null || targetCmd.IsDead)
        {
            // Target dead — return home
            BeginReturn(troop, doorRegistry, buildZones);
            return;
        }

        // Check if in attack range
        Vector3I cmdMicrovoxel = MathHelpers.WorldToMicrovoxel(targetCmd.GlobalPosition);
        float dist = MicrovoxelDistance(troop.CurrentMicrovoxel, cmdMicrovoxel);
        TroopStats stats = TroopDefinitions.Get(troop.Type);

        if (dist <= stats.AttackRange && stats.AttackDamage > 0)
        {
            // Attack!
            troop.SetAIState(TroopAIState.Attacking);
            targetCmd.ApplyDamage(stats.AttackDamage, troop.OwnerSlot, troop.GlobalPosition);
            troop.RecordDamageDealt(stats.AttackDamage);
            troop.HasAttacked = true;
            return;
        }

        // Pathfind toward enemy commander
        if (troop.CurrentPath == null || troop.PathIndex >= troop.CurrentPath.Count)
        {
            // Allow walking through any player's doors during marching
            Func<Vector3I, bool> doorCheck = pos => doorRegistry.IsDoorVoxelAnyPlayer(pos);

            // Try to pathfind to the enemy commander's position
            troop.CurrentPath = pathfinder.FindPath(world, troop.CurrentMicrovoxel, cmdMicrovoxel, doorCheck);
            troop.PathIndex = 0;

            if (troop.CurrentPath == null)
            {
                // No path found — try to find an enemy door to pathfind to instead
                DoorPlacement? enemyDoor = doorRegistry.FindNearestDoor(troop.TargetEnemy, troop.CurrentMicrovoxel);
                if (enemyDoor != null)
                {
                    Vector3I enemyDoorExterior = GetDoorExterior(enemyDoor, troop.TargetEnemy, buildZones, doorRegistry);
                    troop.CurrentPath = pathfinder.FindPath(world, troop.CurrentMicrovoxel, enemyDoorExterior, doorCheck);
                    troop.PathIndex = 0;
                }

                if (troop.CurrentPath == null)
                {
                    // Still no path — Demolisher tries to breach, others wait
                    if (stats.CanDamageWalls)
                    {
                        troop.SetAIState(TroopAIState.Breaching);
                    }
                    return;
                }
            }
        }

        // Move along the march path
        MoveAlongPath(troop);
    }

    /// <summary>
    /// Demolisher breaches adjacent walls when no path exists.
    /// </summary>
    private static void HandleBreaching(TroopEntity troop, VoxelWorld world)
    {
        TroopStats stats = TroopDefinitions.Get(troop.Type);
        if (!stats.CanDamageWalls)
        {
            troop.SetAIState(TroopAIState.Marching);
            troop.CurrentPath = null;
            troop.PathIndex = 0;
            return;
        }

        // Demolish an adjacent wall voxel
        bool demolished = TryDemolishAdjacentWall(troop, world);
        if (!demolished)
        {
            // Nothing adjacent to demolish — go back to marching to re-pathfind
            troop.SetAIState(TroopAIState.Marching);
            troop.CurrentPath = null;
            troop.PathIndex = 0;
        }
    }

    /// <summary>
    /// Attack the enemy commander. After attacking, begin return journey.
    /// </summary>
    private static void HandleAttacking(
        TroopEntity troop,
        VoxelWorld world,
        TroopPathfinder pathfinder,
        DoorRegistry doorRegistry,
        Dictionary<PlayerSlot, BuildZone> buildZones,
        Node sceneRoot)
    {
        Commander.Commander? targetCmd = FindTargetCommander(troop.TargetEnemy, sceneRoot);
        TroopStats stats = TroopDefinitions.Get(troop.Type);

        if (targetCmd == null || targetCmd.IsDead)
        {
            // Target dead — return home
            BeginReturn(troop, doorRegistry, buildZones);
            return;
        }

        Vector3I cmdMicrovoxel = MathHelpers.WorldToMicrovoxel(targetCmd.GlobalPosition);
        float dist = MicrovoxelDistance(troop.CurrentMicrovoxel, cmdMicrovoxel);

        if (dist <= stats.AttackRange && stats.AttackDamage > 0)
        {
            // Continue attacking
            targetCmd.ApplyDamage(stats.AttackDamage, troop.OwnerSlot, troop.GlobalPosition);
            troop.RecordDamageDealt(stats.AttackDamage);
            // After one tick of attacking, begin return
            BeginReturn(troop, doorRegistry, buildZones);
        }
        else
        {
            // Out of range — close in or return
            if (troop.HasAttacked)
            {
                BeginReturn(troop, doorRegistry, buildZones);
            }
            else
            {
                // Get closer
                troop.SetAIState(TroopAIState.Marching);
                troop.CurrentPath = null;
                troop.PathIndex = 0;
            }
        }
    }

    /// <summary>
    /// Return across open terrain to own base door exterior.
    /// </summary>
    private static void HandleReturning(
        TroopEntity troop,
        VoxelWorld world,
        TroopPathfinder pathfinder,
        DoorRegistry doorRegistry,
        Dictionary<PlayerSlot, BuildZone> buildZones,
        Node sceneRoot)
    {
        DoorPlacement? ownDoor = doorRegistry.FindNearestDoor(troop.OwnerSlot, troop.CurrentMicrovoxel);
        if (ownDoor == null)
        {
            // No door — just idle in place
            troop.SetAIState(TroopAIState.Idle);
            return;
        }

        Vector3I doorExterior = GetDoorExterior(ownDoor, troop.OwnerSlot, buildZones, doorRegistry);

        // Check if we've arrived at the door exterior
        if (troop.CurrentMicrovoxel == doorExterior || troop.CurrentMicrovoxel == ownDoor.BaseMicrovoxel)
        {
            // Start entering the base
            troop.SetAIState(TroopAIState.EnteringBase);
            troop.CurrentPath = null;
            troop.PathIndex = 0;
            return;
        }

        // Pathfind back to the door exterior
        if (troop.CurrentPath == null || troop.PathIndex >= troop.CurrentPath.Count)
        {
            Func<Vector3I, bool> doorCheck = pos => doorRegistry.IsDoorVoxelAnyPlayer(pos);
            troop.CurrentPath = pathfinder.FindPath(world, troop.CurrentMicrovoxel, doorExterior, doorCheck);
            troop.PathIndex = 0;

            if (troop.CurrentPath == null)
            {
                // Try the door base position directly
                troop.CurrentPath = pathfinder.FindPath(world, troop.CurrentMicrovoxel, ownDoor.BaseMicrovoxel, doorCheck);
                troop.PathIndex = 0;

                if (troop.CurrentPath == null)
                {
                    // Can't find way back — idle where we are
                    troop.SetAIState(TroopAIState.Idle);
                    return;
                }
            }
        }

        MoveAlongPath(troop);

        // Check arrival again after moving
        if (troop.PathIndex >= troop.CurrentPath.Count)
        {
            if (troop.CurrentMicrovoxel == doorExterior || troop.CurrentMicrovoxel == ownDoor.BaseMicrovoxel)
            {
                troop.SetAIState(TroopAIState.EnteringBase);
                troop.CurrentPath = null;
                troop.PathIndex = 0;
            }
        }
    }

    /// <summary>
    /// Enter the base through own door and pathfind back to home position.
    /// </summary>
    private static void HandleEnteringBase(
        TroopEntity troop,
        VoxelWorld world,
        TroopPathfinder pathfinder,
        DoorRegistry doorRegistry,
        Dictionary<PlayerSlot, BuildZone> buildZones)
    {
        // Check if we've arrived home
        if (troop.CurrentMicrovoxel == troop.HomeMicrovoxel)
        {
            troop.SetAIState(TroopAIState.Idle);
            troop.CurrentPath = null;
            troop.PathIndex = 0;
            return;
        }

        // Pathfind from current position (door area) to home inside the base
        if (troop.CurrentPath == null || troop.PathIndex >= troop.CurrentPath.Count)
        {
            Func<Vector3I, bool> doorCheck = pos => doorRegistry.IsDoorVoxelAnyPlayer(pos);
            troop.CurrentPath = pathfinder.FindPath(world, troop.CurrentMicrovoxel, troop.HomeMicrovoxel, doorCheck);
            troop.PathIndex = 0;

            if (troop.CurrentPath == null)
            {
                // Can't find path home — just idle here
                troop.SetAIState(TroopAIState.Idle);
                return;
            }
        }

        MoveAlongPath(troop);

        // Check arrival
        if (troop.PathIndex >= troop.CurrentPath.Count)
        {
            troop.SetAIState(TroopAIState.Idle);
            troop.CurrentPath = null;
            troop.PathIndex = 0;
        }
    }

    /// <summary>
    /// Begin the return journey home.
    /// </summary>
    private static void BeginReturn(
        TroopEntity troop,
        DoorRegistry doorRegistry,
        Dictionary<PlayerSlot, BuildZone> buildZones)
    {
        troop.HasAttacked = true;
        troop.SetAIState(TroopAIState.Returning);
        troop.CurrentPath = null;
        troop.PathIndex = 0;
    }

    /// <summary>
    /// Move the troop along its current path according to its movement speed.
    /// </summary>
    private static void MoveAlongPath(TroopEntity troop)
    {
        if (troop.CurrentPath == null) return;

        TroopStats stats = TroopDefinitions.Get(troop.Type);
        int stepsToTake = stats.MoveStepsPerTick;

        for (int i = 0; i < stepsToTake && troop.PathIndex < troop.CurrentPath.Count; i++)
        {
            Vector3I next = troop.CurrentPath[troop.PathIndex];
            troop.StartMoveTo(next);
            troop.PathIndex++;
        }
    }

    /// <summary>
    /// Gets the exterior position outside a door, accounting for the build zone edges.
    /// </summary>
    private static Vector3I GetDoorExterior(
        DoorPlacement door,
        PlayerSlot owner,
        Dictionary<PlayerSlot, BuildZone> buildZones,
        DoorRegistry doorRegistry)
    {
        if (buildZones.TryGetValue(owner, out BuildZone zone))
        {
            return doorRegistry.GetDoorExteriorPosition(door, zone.OriginMicrovoxels, zone.MaxMicrovoxelsInclusive);
        }
        // Fallback: step one voxel in -X
        return door.BaseMicrovoxel + new Vector3I(-1, 0, 0);
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

    private static float MicrovoxelDistance(Vector3I a, Vector3I b)
    {
        Vector3 af = new Vector3(a.X, a.Y, a.Z);
        Vector3 bf = new Vector3(b.X, b.Y, b.Z);
        return af.DistanceTo(bf);
    }

    private static bool TryDemolishAdjacentWall(TroopEntity troop, VoxelWorld world)
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
                return true;
            }
        }

        return false;
    }
}
