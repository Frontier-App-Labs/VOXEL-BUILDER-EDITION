using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Voxel;
using VoxelSiege.Core;
using VoxelSiege.Building;
using VoxelSiege.Combat;
using VoxelSiege.Utility;
using VoxelValue = VoxelSiege.Voxel.Voxel;
using CommanderActor = VoxelSiege.Commander.Commander;

namespace VoxelSiege.Army;

/// <summary>Target type for troop attacks, in priority order.</summary>
public enum TroopTargetKind { EnemyTroop, Commander, Weapon, Voxel }

/// <summary>
/// Describes what a troop wants to attack this tick.
/// Only the field matching <see cref="Kind"/> is populated.
/// </summary>
public readonly struct TroopAttackTarget
{
    public readonly TroopTargetKind Kind;
    public readonly Vector3 WorldPosition;
    public readonly Vector3I VoxelPos;
    public readonly TroopEntity? EnemyTroop;
    public readonly CommanderActor? EnemyCommander;
    public readonly WeaponBase? EnemyWeapon;

    public TroopAttackTarget(TroopTargetKind kind, Vector3 worldPos,
        Vector3I voxelPos = default, TroopEntity? enemyTroop = null,
        CommanderActor? enemyCommander = null, WeaponBase? enemyWeapon = null)
    {
        Kind = kind;
        WorldPosition = worldPos;
        VoxelPos = voxelPos;
        EnemyTroop = enemyTroop;
        EnemyCommander = enemyCommander;
        EnemyWeapon = enemyWeapon;
    }
}

/// <summary>
/// Per-troop behavior logic. Called once per round by ArmyManager.TickTroops().
/// Stateless — all state lives in TroopEntity.
///
/// Simplified lifecycle:
///   1. Idle — standing at placed position, waiting for move orders
///   2. Moving — pathfinding toward MoveTarget, walks MoveStepsPerTick per tick
///   3. Attacking — within range of targets, attacks with priority:
///      Enemy Troops > Commander > Weapons > Voxels
/// </summary>
public static class TroopAI
{
    /// <summary>
    /// Execute one tick of troop behavior (called on turn change).
    /// Movement is now continuous in TroopEntity._Process — this only handles attack checks.
    /// </summary>
    public static void ExecuteTick(
        TroopEntity troop,
        VoxelWorld world,
        TroopPathfinder pathfinder,
        DoorRegistry doorRegistry,
        Dictionary<PlayerSlot, BuildZone> buildZones,
        Node sceneRoot,
        bool canAttack = true,
        HashSet<PlayerSlot>? alivePlayers = null,
        Dictionary<PlayerSlot, List<TroopEntity>>? allTroops = null,
        Dictionary<PlayerSlot, CommanderActor>? commanders = null,
        Dictionary<PlayerSlot, List<WeaponBase>>? weapons = null)
    {
        if (troop.AIState == TroopAIState.Dead || troop.CurrentHP <= 0) return;

        // Check for targets to attack (only on owner's turn)
        if (canAttack)
        {
            TroopAttackTarget? target = FindBestTarget(
                troop, world, buildZones, allTroops, commanders, weapons, alivePlayers);
            if (target.HasValue)
            {
                troop.SetAIState(TroopAIState.Attacking);
                ExecuteAttack(troop, world, target.Value);
                troop.PauseForAttack(0.5f);
                return;
            }

            // No standard targets — try clearing nearby debris (rubble piled around objectives)
            if (troop.AttackNearbyDebris())
            {
                troop.PauseForAttack(0.5f);
                return;
            }
        }
    }

    /// <summary>
    /// Dispatches an attack based on target kind.
    /// </summary>
    internal static void ExecuteAttack(TroopEntity troop, VoxelWorld world, TroopAttackTarget target)
    {
        switch (target.Kind)
        {
            case TroopTargetKind.EnemyTroop:
                if (target.EnemyTroop != null && GodotObject.IsInstanceValid(target.EnemyTroop))
                    troop.AttackTroop(target.EnemyTroop);
                break;
            case TroopTargetKind.Commander:
                if (target.EnemyCommander != null && GodotObject.IsInstanceValid(target.EnemyCommander))
                    troop.AttackCommander(target.EnemyCommander);
                break;
            case TroopTargetKind.Weapon:
                if (target.EnemyWeapon != null && GodotObject.IsInstanceValid(target.EnemyWeapon))
                    troop.AttackWeapon(target.EnemyWeapon);
                break;
            case TroopTargetKind.Voxel:
                troop.AttackVoxel(world, target.VoxelPos);
                break;
        }
    }

    /// <summary>
    /// Finds the best target for a troop, checking in priority order:
    /// 1. Enemy troops (highest — defend against incoming attackers)
    /// 2. Enemy commander (high value — win condition)
    /// 3. Enemy weapons (medium — reduce enemy firepower)
    /// 4. Enemy voxels (low — dig toward high-value targets)
    /// </summary>
    internal static TroopAttackTarget? FindBestTarget(
        TroopEntity troop,
        VoxelWorld world,
        Dictionary<PlayerSlot, BuildZone> buildZones,
        Dictionary<PlayerSlot, List<TroopEntity>>? allTroops = null,
        Dictionary<PlayerSlot, CommanderActor>? commanders = null,
        Dictionary<PlayerSlot, List<WeaponBase>>? weapons = null,
        HashSet<PlayerSlot>? alivePlayers = null)
    {
        TroopStats stats = TroopDefinitions.Get(troop.Type);
        float rangeMeters = stats.AttackRange * GameConfig.MicrovoxelMeters;
        Vector3 troopPos = troop.GlobalPosition + Vector3.Up * 0.2f; // eye height

        // Priority 1: Enemy troops within attack range + LOS
        if (allTroops != null)
        {
            TroopEntity? closestTroop = null;
            float closestDist = float.MaxValue;
            foreach (var (player, troops) in allTroops)
            {
                if (player == troop.OwnerSlot) continue;
                foreach (var enemy in troops)
                {
                    if (!GodotObject.IsInstanceValid(enemy) || enemy.CurrentHP <= 0) continue;
                    float dist = troopPos.DistanceTo(enemy.GlobalPosition);
                    if (dist <= rangeMeters && dist < closestDist && HasLineOfSight(world, troopPos, enemy.GlobalPosition, dist))
                    {
                        closestDist = dist;
                        closestTroop = enemy;
                    }
                }
            }
            if (closestTroop != null)
            {
                return new TroopAttackTarget(TroopTargetKind.EnemyTroop,
                    closestTroop.GlobalPosition, enemyTroop: closestTroop);
            }
        }

        // Priority 2: Enemy commander within attack range + LOS
        if (commanders != null)
        {
            CommanderActor? closestCmd = null;
            float closestDist = float.MaxValue;
            foreach (var (player, cmd) in commanders)
            {
                if (player == troop.OwnerSlot) continue;
                if (alivePlayers != null && !alivePlayers.Contains(player)) continue;
                if (!GodotObject.IsInstanceValid(cmd) || cmd.IsDead) continue;
                float dist = troopPos.DistanceTo(cmd.GlobalPosition);
                if (dist <= rangeMeters && dist < closestDist && HasLineOfSight(world, troopPos, cmd.GlobalPosition, dist))
                {
                    closestDist = dist;
                    closestCmd = cmd;
                }
            }
            if (closestCmd != null)
            {
                return new TroopAttackTarget(TroopTargetKind.Commander,
                    closestCmd.GlobalPosition, enemyCommander: closestCmd);
            }
        }

        // Priority 3: Enemy weapons within attack range + LOS
        if (weapons != null)
        {
            WeaponBase? closestWpn = null;
            float closestDist = float.MaxValue;
            foreach (var (player, wpnList) in weapons)
            {
                if (player == troop.OwnerSlot) continue;
                if (alivePlayers != null && !alivePlayers.Contains(player)) continue;
                foreach (var wpn in wpnList)
                {
                    if (wpn == null || !GodotObject.IsInstanceValid(wpn) || wpn.IsDestroyed) continue;
                    float dist = troopPos.DistanceTo(wpn.GlobalPosition);
                    if (dist <= rangeMeters && dist < closestDist && HasLineOfSight(world, troopPos, wpn.GlobalPosition, dist))
                    {
                        closestDist = dist;
                        closestWpn = wpn;
                    }
                }
            }
            if (closestWpn != null)
            {
                return new TroopAttackTarget(TroopTargetKind.Weapon,
                    closestWpn.GlobalPosition, enemyWeapon: closestWpn);
            }
        }

        // Priority 4: Enemy voxels (dig toward high-value targets)
        // Build HVT list from passed commanders/weapons for voxel scoring
        List<Vector3>? hvts = null;
        if (commanders != null || weapons != null)
        {
            hvts = new List<Vector3>();
            if (commanders != null)
            {
                foreach (var (player, cmd) in commanders)
                {
                    if (player != troop.OwnerSlot && GodotObject.IsInstanceValid(cmd) && !cmd.IsDead)
                        hvts.Add(cmd.GlobalPosition);
                }
            }
            if (weapons != null)
            {
                foreach (var (player, wpnList) in weapons)
                {
                    if (player != troop.OwnerSlot)
                    {
                        foreach (var wpn in wpnList)
                        {
                            if (wpn != null && GodotObject.IsInstanceValid(wpn) && !wpn.IsDestroyed)
                                hvts.Add(wpn.GlobalPosition);
                        }
                    }
                }
            }
        }

        Vector3I? voxelTarget = FindNearestEnemyVoxel(
            troop, world, buildZones, highValueTargets: hvts, alivePlayers: alivePlayers);
        if (voxelTarget.HasValue)
        {
            float mvM = GameConfig.MicrovoxelMeters;
            Vector3 voxelWorld = new Vector3(
                voxelTarget.Value.X * mvM + mvM * 0.5f,
                voxelTarget.Value.Y * mvM + mvM * 0.5f,
                voxelTarget.Value.Z * mvM + mvM * 0.5f);
            return new TroopAttackTarget(TroopTargetKind.Voxel,
                voxelWorld, voxelPos: voxelTarget.Value);
        }

        return null;
    }

    /// <summary>
    /// Finds the best solid enemy voxel within attack range.
    /// Priority: blocks near commanders/weapons (high-value targets) > structural supports > zone center.
    /// Skips voxels in <paramref name="excludeTargets"/> so multiple troops spread their attacks.
    /// </summary>
    internal static Vector3I? FindNearestEnemyVoxel(
        TroopEntity troop,
        VoxelWorld world,
        Dictionary<PlayerSlot, BuildZone> buildZones,
        HashSet<Vector3I>? excludeTargets = null,
        List<Vector3>? highValueTargets = null,
        HashSet<PlayerSlot>? alivePlayers = null)
    {
        TroopStats stats = TroopDefinitions.Get(troop.Type);
        int range = (int)Mathf.Ceil(stats.AttackRange);
        Vector3I pos = troop.CurrentMicrovoxel;
        float mvMeters = GameConfig.MicrovoxelMeters;

        Vector3I? bestTarget = null;
        float bestScore = float.MaxValue;

        foreach (var (player, zone) in buildZones)
        {
            if (player == troop.OwnerSlot) continue; // Don't attack own base
            // Skip dead players — don't waste attacks on bases with no commander
            if (alivePlayers != null && !alivePlayers.Contains(player)) continue;

            for (int dx = -range; dx <= range; dx++)
            {
                for (int dy = -range; dy <= range; dy++)
                {
                    for (int dz = -range; dz <= range; dz++)
                    {
                        Vector3I candidate = pos + new Vector3I(dx, dy, dz);
                        float dist = new Vector3(dx, dy, dz).Length();
                        if (dist > stats.AttackRange) continue;

                        // Skip blocks already claimed by another troop
                        if (excludeTargets != null && excludeTargets.Contains(candidate)) continue;

                        // Must be inside enemy build zone
                        if (!zone.ContainsMicrovoxel(candidate)) continue;

                        VoxelValue voxel = world.GetVoxel(candidate);
                        if (!voxel.IsSolid || voxel.Material == VoxelMaterialType.Foundation) continue;

                        // Base score: distance from troop
                        float score = dist;

                        // LOS penalty: voxels behind walls get a large penalty so troops
                        // attack the wall in front of them first instead of targeting
                        // interior blocks they can't physically reach
                        Vector3 troopEye = troop.GlobalPosition + Vector3.Up * 0.2f;
                        Vector3 candidateWorld = new Vector3(
                            candidate.X * mvMeters + mvMeters * 0.5f,
                            candidate.Y * mvMeters + mvMeters * 0.5f,
                            candidate.Z * mvMeters + mvMeters * 0.5f);
                        Vector3 losDir = (candidateWorld - troopEye).Normalized();
                        float losDist = troopEye.DistanceTo(candidateWorld) - mvMeters * 1.5f;
                        if (losDist > 0f && world.RaycastVoxel(troopEye, losDir, losDist, out _, out _))
                            score += 20f; // heavy penalty — prefer visible blocks

                        // Priority bonus: blocks near high-value targets (commanders, weapons)
                        // get a large score reduction so troops dig toward them
                        if (highValueTargets != null && highValueTargets.Count > 0)
                        {
                            float nearestHVT = float.MaxValue;
                            foreach (Vector3 hvt in highValueTargets)
                            {
                                float hvtDist = candidateWorld.DistanceTo(hvt);
                                if (hvtDist < nearestHVT) nearestHVT = hvtDist;
                            }
                            // Strong bonus for blocks within 5m of a high-value target
                            if (nearestHVT < 5f)
                                score -= (5f - nearestHVT) * 3f;
                        }

                        // Structural bonus: prefer blocks that are supporting other blocks
                        // (have solid blocks above them — knocking these out causes collapse)
                        VoxelValue above = world.GetVoxel(candidate + Vector3I.Up);
                        if (above.IsSolid && above.Material != VoxelMaterialType.Foundation)
                            score -= 1.5f;

                        // Weaker blocks are easier to destroy — slight preference
                        score -= (float)voxel.HitPoints * 0.05f;

                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestTarget = candidate;
                        }
                    }
                }
            }
        }

        return bestTarget;
    }

    /// <summary>
    /// Bot AI: compute a move target toward the nearest enemy base center.
    /// Returns null if no enemies remain.
    /// </summary>
    public static Vector3I? ComputeBotMoveTarget(
        TroopEntity troop,
        Dictionary<PlayerSlot, BuildZone> buildZones,
        int troopIndex,
        int totalTroops)
    {
        float bestDist = float.MaxValue;
        Vector3I bestCenter = troop.CurrentMicrovoxel;

        foreach (var (player, zone) in buildZones)
        {
            if (player == troop.OwnerSlot) continue;

            Vector3I center = new Vector3I(
                (zone.OriginMicrovoxels.X + zone.MaxMicrovoxelsInclusive.X) / 2,
                zone.OriginMicrovoxels.Y + 1, // ground level
                (zone.OriginMicrovoxels.Z + zone.MaxMicrovoxelsInclusive.Z) / 2);

            float dist = new Vector3(
                troop.CurrentMicrovoxel.X - center.X,
                0,
                troop.CurrentMicrovoxel.Z - center.Z).Length();

            if (dist < bestDist)
            {
                bestDist = dist;
                bestCenter = center;
            }
        }

        // Apply spread offset so troops don't all stack on the same point
        int spreadX = (troopIndex % 3) - 1; // -1, 0, 1
        int spreadZ = (troopIndex / 3) - 1;
        return bestCenter + new Vector3I(spreadX * 2, 0, spreadZ * 2);
    }

    /// <summary>
    /// Checks if there is a clear line of sight between two world positions.
    /// Uses voxel DDA raycast — returns false if any solid voxel blocks the path.
    /// </summary>
    private static bool HasLineOfSight(VoxelWorld world, Vector3 from, Vector3 to, float distance)
    {
        Vector3 dir = (to - from).Normalized();
        // Raycast the full distance minus a small margin so we don't hit the target's own voxel
        float checkDist = distance - GameConfig.MicrovoxelMeters * 1.5f;
        if (checkDist <= 0f) return true; // point-blank range, always visible
        return !world.RaycastVoxel(from, dir, checkDist, out _, out _);
    }
}
