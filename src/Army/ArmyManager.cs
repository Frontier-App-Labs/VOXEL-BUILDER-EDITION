using Godot;
using System.Collections.Generic;
using VoxelSiege.Building;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;

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
    public bool DeployTroops(PlayerSlot player, PlayerSlot targetEnemy, Node sceneRoot)
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
                // Use the manually placed position
                spawnPos = placed.Value;
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
            entity.TargetEnemy = targetEnemy;
            // Start idle — TroopAI will transition to ExitingBase on first tick
            entity.SetAIState(TroopAIState.Idle);
            _deployedTroops[player].Add(entity);
        }

        troops.Clear(); // purchased troops are now deployed
        GD.Print($"[Army] {player} deployed {_deployedTroops[player].Count} troops inside base against {targetEnemy}");
        return true;
    }

    // === Tick (called on turn change — only ticks the current player's troops) ===
    public void TickTroops(PlayerSlot currentPlayer, Node sceneRoot)
    {
        if (_voxelWorld == null) return;

        if (!_deployedTroops.TryGetValue(currentPlayer, out var troops)) return;

        // Remove dead troops
        troops.RemoveAll(t => !IsInstanceValid(t) || t.CurrentHP <= 0);

        foreach (var troop in troops)
        {
            // Decrement lifespan — skip AI tick if the troop just expired
            if (troop.TickLifespan())
                continue;

            TroopAI.ExecuteTick(troop, _voxelWorld, _pathfinder, _doorRegistry, _buildZones, sceneRoot);
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
                    troop.ApplyDamage(damage, instigator);
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
