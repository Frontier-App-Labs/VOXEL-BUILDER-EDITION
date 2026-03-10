using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using VoxelSiege.Core;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Networking;

public sealed class BuildPhaseSnapshot
{
    public PlayerSlot Player { get; set; }
    public List<Vector3I> Positions { get; set; } = new List<Vector3I>();
    public List<ushort> Data { get; set; } = new List<ushort>();
}

public partial class SyncManager : Node
{
    private NetworkManager? _netManager;

    /// <summary>Fired when a remote weapon fire event is received.</summary>
    public event Action<WeaponFireSyncPayload>? WeaponFireReceived;

    /// <summary>Fired when a remote commander damage event is received.</summary>
    public event Action<CommanderDamageSyncPayload>? CommanderDamageReceived;

    /// <summary>Fired when a remote commander death event is received.</summary>
    public event Action<CommanderDeathSyncPayload>? CommanderDeathReceived;

    /// <summary>Fired when a turn advance event is received.</summary>
    public event Action<TurnAdvanceSyncPayload>? TurnAdvanceReceived;

    /// <summary>Fired when a phase change event is received.</summary>
    public event Action<PhaseChangeSyncPayload>? PhaseChangeReceived;

    /// <summary>Fired when a weapon destroyed event is received.</summary>
    public event Action<WeaponDestroyedSyncPayload>? WeaponDestroyedReceived;

    public override void _Ready()
    {
        _netManager = GetNodeOrNull<NetworkManager>("../NetworkManager");
    }

    // ─────────────────────────────────────────────────
    //  VOXEL SYNC (existing)
    // ─────────────────────────────────────────────────

    public byte[] SerializeBuildSnapshot(PlayerSlot player, IEnumerable<(Vector3I Position, VoxelValue Voxel)> voxels)
    {
        BuildPhaseSnapshot snapshot = new BuildPhaseSnapshot { Player = player };
        foreach ((Vector3I position, VoxelValue voxel) in voxels)
        {
            snapshot.Positions.Add(position);
            snapshot.Data.Add(voxel.Data);
        }

        return JsonSerializer.SerializeToUtf8Bytes(snapshot);
    }

    public BuildPhaseSnapshot? DeserializeBuildSnapshot(byte[] data)
    {
        return JsonSerializer.Deserialize<BuildPhaseSnapshot>(data);
    }

    public byte[] SerializeVoxelDelta(IEnumerable<(Vector3I Position, VoxelValue Voxel)> deltas)
    {
        VoxelDeltaPayload payload = new VoxelDeltaPayload();
        foreach ((Vector3I position, VoxelValue voxel) in deltas)
        {
            payload.Positions.Add(position);
            payload.Data.Add(voxel.Data);
        }

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    public void ApplyVoxelDelta(VoxelWorld world, byte[] data, PlayerSlot? instigator = null)
    {
        VoxelDeltaPayload? payload = JsonSerializer.Deserialize<VoxelDeltaPayload>(data);
        if (payload == null)
        {
            return;
        }

        for (int index = 0; index < payload.Positions.Count && index < payload.Data.Count; index++)
        {
            world.SetVoxel(payload.Positions[index], new VoxelValue(payload.Data[index]), instigator);
        }
    }

    // ─────────────────────────────────────────────────
    //  COMBAT STATE SYNC (new)
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Broadcasts a weapon fire event to all peers.
    /// </summary>
    public void SendWeaponFire(PlayerSlot owner, int weaponIndex, float yaw, float pitch, float power)
    {
        var payload = new WeaponFireSyncPayload((int)owner, weaponIndex, yaw, pitch, power);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        Rpc(nameof(ReceiveWeaponFire), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveWeaponFire(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<WeaponFireSyncPayload>(data);
        WeaponFireReceived?.Invoke(payload);
    }

    /// <summary>
    /// Broadcasts a commander damage event to all peers.
    /// </summary>
    public void SendCommanderDamage(PlayerSlot victim, int damage, int remainingHealth, Vector3 position, bool isCritical)
    {
        var payload = new CommanderDamageSyncPayload(
            (int)victim, damage, remainingHealth,
            position.X, position.Y, position.Z, isCritical);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        Rpc(nameof(ReceiveCommanderDamage), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveCommanderDamage(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<CommanderDamageSyncPayload>(data);
        CommanderDamageReceived?.Invoke(payload);
    }

    /// <summary>
    /// Broadcasts a commander death event to all peers.
    /// </summary>
    public void SendCommanderDeath(PlayerSlot victim, PlayerSlot? killer, Vector3 position)
    {
        var payload = new CommanderDeathSyncPayload(
            (int)victim,
            killer.HasValue ? (int)killer.Value : -1,
            position.X, position.Y, position.Z);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        Rpc(nameof(ReceiveCommanderDeath), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveCommanderDeath(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<CommanderDeathSyncPayload>(data);
        CommanderDeathReceived?.Invoke(payload);
    }

    /// <summary>
    /// Broadcasts a turn advance event to all peers.
    /// </summary>
    public void SendTurnAdvance(PlayerSlot currentPlayer, int roundNumber, float turnTime)
    {
        var payload = new TurnAdvanceSyncPayload((int)currentPlayer, roundNumber, turnTime);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        Rpc(nameof(ReceiveTurnAdvance), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveTurnAdvance(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<TurnAdvanceSyncPayload>(data);
        TurnAdvanceReceived?.Invoke(payload);
    }

    /// <summary>
    /// Broadcasts a phase change event to all peers.
    /// </summary>
    public void SendPhaseChange(GamePhase previousPhase, GamePhase currentPhase)
    {
        var payload = new PhaseChangeSyncPayload((int)previousPhase, (int)currentPhase);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        Rpc(nameof(ReceivePhaseChange), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceivePhaseChange(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<PhaseChangeSyncPayload>(data);
        PhaseChangeReceived?.Invoke(payload);
    }

    /// <summary>
    /// Broadcasts a weapon destroyed event to all peers.
    /// </summary>
    public void SendWeaponDestroyed(PlayerSlot owner, string weaponId, Vector3 position)
    {
        var payload = new WeaponDestroyedSyncPayload(
            (int)owner, weaponId,
            position.X, position.Y, position.Z);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        Rpc(nameof(ReceiveWeaponDestroyed), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveWeaponDestroyed(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<WeaponDestroyedSyncPayload>(data);
        WeaponDestroyedReceived?.Invoke(payload);
    }
}
