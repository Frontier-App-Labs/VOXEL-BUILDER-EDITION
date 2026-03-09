using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;

namespace VoxelSiege.Army;

public partial class ArmyManager : Node
{
    private readonly Dictionary<PlayerSlot, List<TroopType>> _purchasedTroops = new();
    private readonly Dictionary<PlayerSlot, List<TroopEntity>> _deployedTroops = new();
    private readonly DoorRegistry _doorRegistry = new();
    private readonly TroopPathfinder _pathfinder = new();
    private VoxelWorld? _voxelWorld;

    public DoorRegistry Doors => _doorRegistry;

    public void Initialize(VoxelWorld voxelWorld)
    {
        _voxelWorld = voxelWorld;
    }

    // === Build Phase ===
    public bool TryBuyTroop(PlayerSlot player, TroopType type, PlayerData playerData)
    {
        var stats = TroopDefinitions.Get(type);
        if (!_purchasedTroops.ContainsKey(player))
            _purchasedTroops[player] = new List<TroopType>();

        if (_purchasedTroops[player].Count >= GameConfig.MaxTroopsPerPlayer)
            return false;

        if (!playerData.TrySpend(stats.Cost))
            return false;

        _purchasedTroops[player].Add(type);
        return true;
    }

    public bool TrySellTroop(PlayerSlot player, TroopType type, PlayerData playerData)
    {
        if (!_purchasedTroops.TryGetValue(player, out var list)) return false;
        int idx = list.IndexOf(type);
        if (idx < 0) return false;

        list.RemoveAt(idx);
        playerData.Refund(TroopDefinitions.Get(type).Cost);
        return true;
    }

    public int TroopCount(PlayerSlot player, TroopType type)
    {
        if (!_purchasedTroops.TryGetValue(player, out var list)) return 0;
        int count = 0;
        foreach (var t in list) if (t == type) count++;
        return count;
    }

    public IReadOnlyList<TroopType> GetPurchasedTroops(PlayerSlot player)
    {
        return _purchasedTroops.TryGetValue(player, out var list)
            ? list
            : System.Array.Empty<TroopType>();
    }

    // === Combat Phase ===
    public bool DeployTroops(PlayerSlot player, PlayerSlot targetEnemy, Node sceneRoot)
    {
        if (_voxelWorld == null) return false;
        if (!_purchasedTroops.TryGetValue(player, out var troops) || troops.Count == 0)
            return false;

        // Find door positions to spawn troops at
        var doors = _doorRegistry.GetDoors(player);

        Color teamColor = GameConfig.PlayerColors[(int)player];

        if (!_deployedTroops.ContainsKey(player))
            _deployedTroops[player] = new List<TroopEntity>();

        for (int i = 0; i < troops.Count; i++)
        {
            Vector3I spawnPos;
            if (doors.Count > 0)
            {
                // Spread troops across available doors
                var door = doors[i % doors.Count];
                spawnPos = door.BaseMicrovoxel;
            }
            else
            {
                // Fallback: spawn near build zone origin
                Vector3I zoneOrigin = GetBuildZoneOriginMicrovoxels(player);
                spawnPos = zoneOrigin + new Vector3I(0, 1, 0); // above ground
            }

            TroopEntity entity = new TroopEntity();
            entity.Name = $"{player}_{troops[i]}_{i}";
            sceneRoot.AddChild(entity);
            entity.Initialize(troops[i], player, spawnPos, teamColor);
            entity.TargetEnemy = targetEnemy;
            entity.SetAIState(TroopAIState.Marching);
            _deployedTroops[player].Add(entity);
        }

        troops.Clear(); // purchased troops are now deployed
        GD.Print($"[Army] {player} deployed {_deployedTroops[player].Count} troops against {targetEnemy}");
        return true;
    }

    // === Tick (called each turn change) ===
    public void TickTroops(Node sceneRoot)
    {
        if (_voxelWorld == null) return;

        foreach (var (player, troops) in _deployedTroops)
        {
            // Remove dead troops
            troops.RemoveAll(t => !IsInstanceValid(t) || t.CurrentHP <= 0);

            foreach (var troop in troops)
            {
                TroopAI.ExecuteTick(troop, _voxelWorld, _pathfinder, _doorRegistry, sceneRoot);
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
