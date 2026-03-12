using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Combat;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using CommanderActor = VoxelSiege.Commander.Commander;

namespace VoxelSiege.Army;

public partial class ArmyManager : Node
{
    private readonly Dictionary<PlayerSlot, List<(TroopType Type, Vector3I? PlacedPosition)>> _purchasedTroops = new();
    private readonly Dictionary<PlayerSlot, List<TroopEntity>> _deployedTroops = new();
    private readonly DoorRegistry _doorRegistry = new();
    private readonly TroopPathfinder _pathfinder = new();
    private Dictionary<PlayerSlot, BuildZone> _buildZones = new();
    private VoxelWorld? _voxelWorld;

    public DoorRegistry Doors => _doorRegistry;

    public void Initialize(VoxelWorld voxelWorld)
    {
        _voxelWorld = voxelWorld;
    }

    /// <summary>
    /// Provides the army manager with build zone info so troops can navigate through doors.
    /// Must be called after build zones are set up (e.g. in GameManager after SetupBuildZones).
    /// </summary>
    public void SetBuildZones(Dictionary<PlayerSlot, BuildZone> buildZones)
    {
        _buildZones = buildZones;
    }

    // === Build Phase ===
    public bool TryBuyTroop(PlayerSlot player, TroopType type, PlayerData playerData)
    {
        var stats = TroopDefinitions.Get(type);
        if (!_purchasedTroops.ContainsKey(player))
            _purchasedTroops[player] = new();

        if (_purchasedTroops[player].Count >= GameConfig.MaxTroopsPerPlayer)
            return false;

        if (!playerData.TrySpend(stats.Cost))
            return false;

        _purchasedTroops[player].Add((type, null));
        return true;
    }

    /// <summary>
    /// Buys a troop AND records its placement position (for manual placement in build phase).
    /// </summary>
    public bool TryBuyAndPlaceTroop(PlayerSlot player, TroopType type, PlayerData playerData, Vector3I position)
    {
        var stats = TroopDefinitions.Get(type);
        if (!_purchasedTroops.ContainsKey(player))
            _purchasedTroops[player] = new();

        if (_purchasedTroops[player].Count >= GameConfig.MaxTroopsPerPlayer)
            return false;

        if (!playerData.TrySpend(stats.Cost))
            return false;

        _purchasedTroops[player].Add((type, position));
        return true;
    }

    public bool TrySellTroop(PlayerSlot player, TroopType type, PlayerData playerData)
    {
        if (!_purchasedTroops.TryGetValue(player, out var list)) return false;
        // Find last troop of this type (most recently placed)
        int idx = -1;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Type == type) { idx = i; break; }
        }
        if (idx < 0) return false;

        list.RemoveAt(idx);
        playerData.Refund(TroopDefinitions.Get(type).Cost);
        return true;
    }

    public int TroopCount(PlayerSlot player, TroopType type)
    {
        if (!_purchasedTroops.TryGetValue(player, out var list)) return 0;
        int count = 0;
        foreach (var t in list) if (t.Type == type) count++;
        return count;
    }

    public IReadOnlyList<(TroopType Type, Vector3I? PlacedPosition)> GetPurchasedTroops(PlayerSlot player)
    {
        return _purchasedTroops.TryGetValue(player, out var list)
            ? list
            : System.Array.Empty<(TroopType, Vector3I?)>();
    }

    // === Combat Phase ===
    /// <summary>
    /// Deploys troops. Uses manually placed positions when available,
    /// otherwise falls back to auto-finding interior positions.
    /// </summary>
    public bool DeployTroops(PlayerSlot player, Node sceneRoot)
    {
        if (_voxelWorld == null) return false;
        if (!_purchasedTroops.TryGetValue(player, out var troops) || troops.Count == 0)
            return false;

        Color teamColor = GameConfig.PlayerColors[(int)player];

        if (!_deployedTroops.ContainsKey(player))
            _deployedTroops[player] = new List<TroopEntity>();

        // Count troops that need auto-placement (no manual position)
        int autoCount = 0;
        foreach (var t in troops)
            if (!t.PlacedPosition.HasValue) autoCount++;

        List<Vector3I> autoPositions = autoCount > 0
            ? FindInteriorSpawnPositions(player, autoCount)
            : new List<Vector3I>();
        int autoIdx = 0;

        for (int i = 0; i < troops.Count; i++)
        {
            Vector3I spawnPos;
            Vector3I? placed = troops[i].PlacedPosition;
            if (placed.HasValue)
            {
                // Use the manually placed position, but validate it's still walkable
                spawnPos = placed.Value;
                if (!_pathfinder.IsWalkable(_voxelWorld, spawnPos))
                {
                    // Position is now blocked — find nearest air position
                    Vector3I adjusted = FindNearbyWalkable(spawnPos, 6);
                    GD.Print($"[Army] {player} troop {i} placed pos {spawnPos} now blocked, adjusted to {adjusted}");
                    spawnPos = adjusted;
                }
            }
            else
            {
                // Fall back to auto-found interior position
                spawnPos = autoIdx < autoPositions.Count
                    ? autoPositions[autoIdx]
                    : autoPositions.Count > 0
                        ? autoPositions[autoIdx % autoPositions.Count]
                        : GetFallbackSpawnPos(player);
                autoIdx++;
            }

            TroopEntity entity = new TroopEntity();
            entity.Name = $"{player}_{troops[i].Type}_{i}";
            sceneRoot.AddChild(entity);
            entity.Initialize(troops[i].Type, player, spawnPos, teamColor);
            // Start idle at placed position — troops stay visible where player put them
            entity.SetAIState(TroopAIState.Idle);
            _deployedTroops[player].Add(entity);
            GD.Print($"[Army] {player} deployed {troops[i].Type} #{i} at microvoxel {spawnPos} (world: {entity.GlobalPosition})");
        }

        troops.Clear(); // purchased troops are now deployed
        GD.Print($"[Army] {player} deployed {_deployedTroops[player].Count} troops at placed positions");
        return true;
    }

    // === Continuous movement: pathfinding runs every frame for troops that need it ===
    public override void _Process(double delta)
    {
        if (_voxelWorld == null) return;

        foreach (var (player, troops) in _deployedTroops)
        {
            foreach (var troop in troops)
            {
                if (!IsInstanceValid(troop) || troop.CurrentHP <= 0 || troop.IsSurrendered) continue;

                // If troop has a MoveTarget but no current path, find one
                if (troop.MoveTarget.HasValue && troop.CurrentPath == null)
                {
                    Func<Vector3I, bool> doorCheck = pos =>
                        _doorRegistry.IsDoorVoxel(pos, troop.OwnerSlot);

                    troop.CurrentPath = _pathfinder.FindPath(
                        _voxelWorld, troop.CurrentMicrovoxel, troop.MoveTarget.Value, doorCheck);
                    troop.PathIndex = 0;

                    if (troop.CurrentPath == null)
                    {
                        // Can't reach spread target — try original click position as fallback
                        if (troop.MoveTargetFallback.HasValue)
                        {
                            troop.MoveTarget = troop.MoveTargetFallback.Value;
                            troop.MoveTargetFallback = null;
                            // Path will be computed next frame
                        }
                        else
                        {
                            troop.MoveTarget = null;
                        }
                    }
                }
            }
        }
    }

    // === Tick (called on turn change) ===
    /// <summary>
    /// Ticks ALL players' troops. Attack checks only — movement is continuous in _Process.
    /// If skipAttacksFor is set, that player's troops won't attack (handled by visual sequence).
    /// </summary>
    public void TickAllTroops(PlayerSlot currentPlayer, Node sceneRoot, PlayerSlot? skipAttacksFor = null,
        HashSet<PlayerSlot>? alivePlayers = null,
        Dictionary<PlayerSlot, CommanderActor>? commanders = null,
        Dictionary<PlayerSlot, List<WeaponBase>>? weapons = null)
    {
        if (_voxelWorld == null) return;

        foreach (var (player, troops) in _deployedTroops)
        {
            // Remove dead troops
            troops.RemoveAll(t => !IsInstanceValid(t) || t.CurrentHP <= 0);

            bool canAttack = player == currentPlayer && player != skipAttacksFor;
            foreach (var troop in troops)
            {
                TroopAI.ExecuteTick(troop, _voxelWorld, _pathfinder, _doorRegistry, _buildZones, sceneRoot,
                    canAttack, alivePlayers, _deployedTroops, commanders, weapons);
            }
        }
    }

    // === Damage from explosions ===
    public void DamageNearbyTroops(Vector3 worldPos, int baseDamage, float radiusMicrovoxels, PlayerSlot? instigator)
    {
        foreach (var (player, troops) in _deployedTroops)
        {
            foreach (var troop in troops)
            {
                if (!IsInstanceValid(troop) || troop.CurrentHP <= 0) continue;
                float dist = troop.GlobalPosition.DistanceTo(worldPos) / GameConfig.MicrovoxelMeters;
                if (dist >= radiusMicrovoxels) continue;
                float falloff = 1f - Mathf.Clamp(dist / radiusMicrovoxels, 0f, 1f);
                int damage = Mathf.CeilToInt(baseDamage * falloff);
                if (damage > 0)
                    troop.ApplyDamage(damage, instigator, worldPos);
            }
        }
    }

    public void ClearAll()
    {
        foreach (var (_, troops) in _deployedTroops)
            foreach (var t in troops)
                if (IsInstanceValid(t)) t.QueueFree();
        _deployedTroops.Clear();
        _purchasedTroops.Clear();
        _doorRegistry.Clear();
    }

    /// <summary>
    /// Finds walkable positions inside the player's base for troop spawning.
    /// Searches the build zone floor level for open air spaces with solid ground below.
    /// </summary>
    private List<Vector3I> FindInteriorSpawnPositions(PlayerSlot player, int needed)
    {
        var results = new List<Vector3I>();

        if (!_buildZones.TryGetValue(player, out BuildZone zone) || _voxelWorld == null)
        {
            results.Add(GetFallbackSpawnPos(player));
            return results;
        }

        Vector3I zoneMin = zone.OriginMicrovoxels;
        Vector3I zoneMax = zone.MaxMicrovoxelsInclusive;

        // Scan the interior of the zone (1 voxel inset from edges to avoid walls)
        // Start from the bottom Y and scan upward to find floor-level positions
        for (int y = zoneMin.Y; y <= zoneMax.Y && results.Count < needed; y++)
        {
            for (int x = zoneMin.X + 1; x < zoneMax.X && results.Count < needed; x++)
            {
                for (int z = zoneMin.Z + 1; z < zoneMax.Z && results.Count < needed; z++)
                {
                    Vector3I pos = new Vector3I(x, y, z);
                    Vector3I below = new Vector3I(x, y - 1, z);
                    Vector3I above = new Vector3I(x, y + 1, z);

                    // Need: solid ground below, air at pos, air above pos (2-tall clearance)
                    if (_voxelWorld.GetVoxel(below).IsSolid &&
                        _voxelWorld.GetVoxel(pos).IsAir &&
                        _voxelWorld.GetVoxel(above).IsAir)
                    {
                        results.Add(pos);
                    }
                }
            }
        }

        // If we didn't find enough positions inside, fall back to zone origin
        if (results.Count == 0)
        {
            results.Add(GetFallbackSpawnPos(player));
        }

        return results;
    }

    /// <summary>
    /// Scans outward from a position to find the nearest walkable microvoxel.
    /// Used when a placed troop position is now blocked by built blocks.
    /// </summary>
    private Vector3I FindNearbyWalkable(Vector3I center, int searchRadius)
    {
        if (_voxelWorld == null) return center;

        for (int r = 1; r <= searchRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dy = -2; dy <= 4; dy++)
                    {
                        Vector3I candidate = center + new Vector3I(dx, dy, dz);
                        if (_pathfinder.IsWalkable(_voxelWorld, candidate))
                            return candidate;
                    }
                }
            }
        }

        return center; // couldn't find one, use original
    }

    /// <summary>
    /// Sets a move target for all of a player's alive troops. Used by both human click-to-move
    /// and bot AI. Applies per-troop offset to prevent stacking.
    /// </summary>
    public void MoveTroopsToward(PlayerSlot player, Vector3I targetMicrovoxel)
    {
        if (!_deployedTroops.TryGetValue(player, out var troops)) return;

        int alive = 0;
        foreach (var t in troops)
            if (IsInstanceValid(t) && t.CurrentHP > 0) alive++;

        // Track claimed destinations so no two troops get the same target
        HashSet<Vector3I> claimed = new();
        int idx = 0;
        foreach (var troop in troops)
        {
            if (!IsInstanceValid(troop) || troop.CurrentHP <= 0) continue;

            // Offset each troop slightly to prevent stacking
            int spreadX = (idx % 3) - 1; // -1, 0, 1
            int spreadZ = (idx / 3) - 1;
            Vector3I candidate = targetMicrovoxel + new Vector3I(spreadX * 2, 0, spreadZ * 2);

            // If this position is already claimed, spiral outward to find an open one
            if (claimed.Contains(candidate))
            {
                bool found = false;
                for (int r = 1; r <= 4 && !found; r++)
                {
                    for (int dx = -r; dx <= r && !found; dx++)
                    {
                        for (int dz = -r; dz <= r && !found; dz++)
                        {
                            if (System.Math.Abs(dx) != r && System.Math.Abs(dz) != r) continue;
                            Vector3I alt = targetMicrovoxel + new Vector3I(dx * 2, 0, dz * 2);
                            if (!claimed.Contains(alt))
                            {
                                candidate = alt;
                                found = true;
                            }
                        }
                    }
                }
            }

            claimed.Add(candidate);
            troop.MoveTarget = candidate;
            troop.MoveTargetFallback = candidate != targetMicrovoxel ? targetMicrovoxel : null;
            troop.CurrentPath = null;
            troop.PathIndex = 0;
            idx++;
        }

        GD.Print($"[Army] {player}: {alive} troops moving toward {targetMicrovoxel}");
    }

    /// <summary>
    /// Bot AI: auto-move troops toward nearest enemy base.
    /// </summary>
    public void BotMoveTroops(PlayerSlot botPlayer)
    {
        if (!_deployedTroops.TryGetValue(botPlayer, out var troops)) return;

        HashSet<Vector3I> claimed = new();
        int idx = 0;
        int total = troops.Count;
        foreach (var troop in troops)
        {
            if (!IsInstanceValid(troop) || troop.CurrentHP <= 0) continue;

            Vector3I? target = TroopAI.ComputeBotMoveTarget(troop, _buildZones, idx, total);
            if (target.HasValue)
            {
                Vector3I candidate = target.Value;
                // Prevent overlap — find a unique position if claimed
                if (claimed.Contains(candidate))
                {
                    bool found = false;
                    for (int r = 1; r <= 4 && !found; r++)
                    {
                        for (int dx = -r; dx <= r && !found; dx++)
                        {
                            for (int dz = -r; dz <= r && !found; dz++)
                            {
                                if (System.Math.Abs(dx) != r && System.Math.Abs(dz) != r) continue;
                                Vector3I alt = candidate + new Vector3I(dx, 0, dz);
                                if (!claimed.Contains(alt))
                                {
                                    candidate = alt;
                                    found = true;
                                }
                            }
                        }
                    }
                }
                claimed.Add(candidate);
                troop.MoveTarget = candidate;
                troop.CurrentPath = null;
                troop.PathIndex = 0;
            }
            idx++;
        }
    }

    /// <summary>Returns the list of alive deployed troops for a player.</summary>
    public List<TroopEntity> GetDeployedTroops(PlayerSlot player)
    {
        if (!_deployedTroops.TryGetValue(player, out var troops))
            return new List<TroopEntity>();
        troops.RemoveAll(t => !IsInstanceValid(t) || t.CurrentHP <= 0);
        return troops;
    }

    /// <summary>Returns the full deployed troops dictionary (all players).</summary>
    public Dictionary<PlayerSlot, List<TroopEntity>> GetDeployedTroops() => _deployedTroops;

    /// <summary>Returns true if the player has at least one alive deployed troop.</summary>
    public bool HasAliveTroops(PlayerSlot player)
    {
        if (!_deployedTroops.TryGetValue(player, out var troops)) return false;
        foreach (var t in troops)
            if (IsInstanceValid(t) && t.CurrentHP > 0) return true;
        return false;
    }

    /// <summary>
    /// Makes all of a player's troops surrender (hands up, stop fighting).
    /// Called when their commander dies.
    /// </summary>
    public void SurrenderTroops(PlayerSlot player)
    {
        if (!_deployedTroops.TryGetValue(player, out var troops)) return;
        foreach (var troop in troops)
        {
            if (!IsInstanceValid(troop) || troop.CurrentHP <= 0) continue;
            troop.MoveTarget = null;
            troop.MoveTargetFallback = null;
            troop.CurrentPath = null;
            troop.Surrender();
        }
        GD.Print($"[Army] {player}: troops surrendered (commander killed)");
    }

    /// <summary>
    /// Returns a list of (troop, attack target) pairs for troops that can attack this turn.
    /// Used by the troop attack camera sequence. Checks all target types in priority order:
    /// Enemy Troops > Commander > Weapons > Voxels.
    /// </summary>
    public List<(TroopEntity Troop, TroopAttackTarget Target)> GetTroopsWithAttackTargets(
        PlayerSlot player,
        Dictionary<PlayerSlot, CommanderActor>? commanders = null,
        Dictionary<PlayerSlot, List<WeaponBase>>? weapons = null,
        HashSet<PlayerSlot>? alivePlayers = null)
    {
        var result = new List<(TroopEntity, TroopAttackTarget)>();
        if (_voxelWorld == null || !_deployedTroops.TryGetValue(player, out var troops)) return result;

        foreach (var troop in troops)
        {
            if (!IsInstanceValid(troop) || troop.CurrentHP <= 0 || troop.IsSurrendered) continue;
            TroopAttackTarget? target = TroopAI.FindBestTarget(
                troop, _voxelWorld, _buildZones,
                _deployedTroops, commanders, weapons, alivePlayers);
            if (target.HasValue)
            {
                result.Add((troop, target.Value));
            }
        }
        return result;
    }

    /// <summary>
    /// Returns the world-space center of a player's alive deployed troops.
    /// </summary>
    public Vector3 GetTroopClusterCenter(PlayerSlot player)
    {
        if (!_deployedTroops.TryGetValue(player, out var troops)) return Vector3.Zero;
        Vector3 sum = Vector3.Zero;
        int count = 0;
        foreach (var t in troops)
        {
            if (!IsInstanceValid(t) || t.CurrentHP <= 0) continue;
            sum += t.GlobalPosition;
            count++;
        }
        return count > 0 ? sum / count : Vector3.Zero;
    }

    /// <summary>
    /// Prints a diagnostic summary of all deployed troops (position, HP, AI state, visibility).
    /// Called by pressing a debug key during combat.
    /// </summary>
    public void PrintTroopDebug()
    {
        GD.Print("[Army] === TROOP DEBUG ===");
        foreach (var (player, troops) in _deployedTroops)
        {
            GD.Print($"  {player}: {troops.Count} deployed");
            foreach (var troop in troops)
            {
                if (!IsInstanceValid(troop)) { GD.Print("    (freed)"); continue; }
                GD.Print($"    {troop.Name} HP={troop.CurrentHP} State={troop.AIState} Pos={troop.GlobalPosition} Visible={troop.Visible}");
            }
        }
        int purchased = 0;
        foreach (var (player, list) in _purchasedTroops) purchased += list.Count;
        GD.Print($"  Purchased (undeployed): {purchased}");
        GD.Print("[Army] === END DEBUG ===");
    }

    private Vector3I GetFallbackSpawnPos(PlayerSlot player)
    {
        Vector3I zoneOrigin = GetBuildZoneOriginMicrovoxels(player);
        return zoneOrigin + new Vector3I(2, 1, 2); // slightly inset, above ground
    }

    private Vector3I GetBuildZoneOriginMicrovoxels(PlayerSlot player)
    {
        int idx = (int)player;
        if (idx < GameConfig.FourPlayerZoneOrigins.Length)
        {
            Vector3I bu = GameConfig.FourPlayerZoneOrigins[idx];
            return MathHelpers.BuildToMicrovoxel(bu);
        }
        return Vector3I.Zero;
    }
}
