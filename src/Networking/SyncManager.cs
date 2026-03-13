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

    /// <summary>Fired when a remote player's build snapshot is received.</summary>
    public event Action<BuildCompleteSyncPayload>? BuildCompleteReceived;

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

    /// <summary>Fired when a remote player skips their turn.</summary>
    public event Action<SkipTurnSyncPayload>? SkipTurnReceived;

    /// <summary>Fired when a remote player uses a powerup.</summary>
    public event Action<PowerupUsedSyncPayload>? PowerupUsedReceived;

    /// <summary>Fired when a remote EMP result is received (which weapons were disabled).</summary>
    public event Action<EmpResultSyncPayload>? EmpResultReceived;

    /// <summary>Fired when a remote airstrike result is received (impact positions).</summary>
    public event Action<AirstrikeResultSyncPayload>? AirstrikeResultReceived;

    /// <summary>Fired when a remote player moves their troops.</summary>
    public event Action<TroopMoveSyncPayload>? TroopMoveReceived;

    /// <summary>Fired when the host broadcasts the turn order for combat.</summary>
    public event Action<TurnOrderSyncPayload>? TurnOrderReceived;

    /// <summary>Fired when a game over event is received.</summary>
    public event Action<GameOverSyncPayload>? GameOverReceived;

    /// <summary>Fired when a peer disconnect mid-game is received.</summary>
    public event Action<DisconnectSyncPayload>? DisconnectReceived;

    /// <summary>Fired when the host broadcasts authoritative voxel damage changes.</summary>
    public event Action<byte[]>? VoxelDamageReceived;

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
    /// Broadcasts the local player's completed build to all peers.
    /// </summary>
    public void SendBuildComplete(PlayerSlot player, string blueprintJson)
    {
        var payload = new BuildCompleteSyncPayload((int)player, blueprintJson);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        GD.Print($"[Sync] Sending build snapshot for {player} ({data.Length} bytes)");
        Rpc(nameof(ReceiveBuildComplete), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveBuildComplete(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<BuildCompleteSyncPayload>(data);
        GD.Print($"[Sync] Received build snapshot for slot {payload.PlayerSlotIndex}");
        BuildCompleteReceived?.Invoke(payload);
    }

    /// <summary>
    /// Broadcasts a weapon fire event (with launch velocity) to all peers.
    /// </summary>
    public void SendWeaponFire(PlayerSlot owner, int weaponIndex, Vector3 launchVelocity, Vector3 weaponPos)
    {
        var payload = new WeaponFireSyncPayload((int)owner, weaponIndex,
            launchVelocity.X, launchVelocity.Y, launchVelocity.Z,
            weaponPos.X, weaponPos.Y, weaponPos.Z);
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

    // ─────────────────────────────────────────────────
    //  SKIP TURN SYNC
    // ─────────────────────────────────────────────────

    public void SendSkipTurn(PlayerSlot player)
    {
        var payload = new SkipTurnSyncPayload((int)player);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        GD.Print($"[Sync] Sending skip turn for {player}");
        Rpc(nameof(ReceiveSkipTurn), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveSkipTurn(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<SkipTurnSyncPayload>(data);
        GD.Print($"[Sync] Received skip turn for slot {payload.PlayerSlotIndex}");
        SkipTurnReceived?.Invoke(payload);
    }

    // ─────────────────────────────────────────────────
    //  POWERUP USED SYNC
    // ─────────────────────────────────────────────────

    public void SendPowerupUsed(PlayerSlot player, int powerupType, PlayerSlot targetEnemy = default, Vector3 targetPos = default)
    {
        var payload = new PowerupUsedSyncPayload(
            (int)player, powerupType, (int)targetEnemy,
            targetPos.X, targetPos.Y, targetPos.Z);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        GD.Print($"[Sync] Sending powerup {powerupType} used by {player}");
        Rpc(nameof(ReceivePowerupUsed), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceivePowerupUsed(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<PowerupUsedSyncPayload>(data);
        GD.Print($"[Sync] Received powerup {payload.PowerupTypeId} from slot {payload.PlayerSlotIndex}");
        PowerupUsedReceived?.Invoke(payload);
    }

    // ─────────────────────────────────────────────────
    //  EMP RESULT SYNC
    // ─────────────────────────────────────────────────

    public void SendEmpResult(PlayerSlot activator, int[] disabledOwnerSlots, int[] disabledWeaponIndices)
    {
        var payload = new EmpResultSyncPayload((int)activator, disabledOwnerSlots, disabledWeaponIndices);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        GD.Print($"[Sync] Sending EMP result: {disabledWeaponIndices.Length} weapons disabled");
        Rpc(nameof(ReceiveEmpResult), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEmpResult(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<EmpResultSyncPayload>(data);
        GD.Print($"[Sync] Received EMP result from slot {payload.ActivatorSlotIndex}");
        EmpResultReceived?.Invoke(payload);
    }

    // ─────────────────────────────────────────────────
    //  AIRSTRIKE RESULT SYNC
    // ─────────────────────────────────────────────────

    public void SendAirstrikeResult(PlayerSlot player, PlayerSlot targetEnemy, Vector3[] impacts, int planeCount)
    {
        float[] xs = new float[impacts.Length];
        float[] ys = new float[impacts.Length];
        float[] zs = new float[impacts.Length];
        for (int i = 0; i < impacts.Length; i++)
        {
            xs[i] = impacts[i].X;
            ys[i] = impacts[i].Y;
            zs[i] = impacts[i].Z;
        }
        var payload = new AirstrikeResultSyncPayload((int)player, (int)targetEnemy, xs, ys, zs, planeCount);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        GD.Print($"[Sync] Sending airstrike result: {impacts.Length} impacts");
        Rpc(nameof(ReceiveAirstrikeResult), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveAirstrikeResult(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<AirstrikeResultSyncPayload>(data);
        GD.Print($"[Sync] Received airstrike result from slot {payload.PlayerSlotIndex}");
        AirstrikeResultReceived?.Invoke(payload);
    }

    // ─────────────────────────────────────────────────
    //  TROOP MOVE SYNC
    // ─────────────────────────────────────────────────

    public void SendTroopMove(PlayerSlot player, Vector3I target)
    {
        var payload = new TroopMoveSyncPayload((int)player, target.X, target.Y, target.Z);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        GD.Print($"[Sync] Sending troop move for {player} to {target}");
        Rpc(nameof(ReceiveTroopMove), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveTroopMove(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<TroopMoveSyncPayload>(data);
        TroopMoveReceived?.Invoke(payload);
    }

    // ─────────────────────────────────────────────────
    //  TURN ORDER SYNC (host → clients)
    // ─────────────────────────────────────────────────

    public void SendTurnOrder(IReadOnlyList<PlayerSlot> order)
    {
        int[] slots = new int[order.Count];
        for (int i = 0; i < order.Count; i++)
            slots[i] = (int)order[i];
        var payload = new TurnOrderSyncPayload(slots);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        GD.Print($"[Sync] Sending turn order: {string.Join(", ", order)}");
        Rpc(nameof(ReceiveTurnOrder), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveTurnOrder(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<TurnOrderSyncPayload>(data);
        GD.Print($"[Sync] Received turn order: {string.Join(", ", payload.SlotOrder)}");
        TurnOrderReceived?.Invoke(payload);
    }

    // ─────────────────────────────────────────────────
    //  GAME OVER SYNC
    // ─────────────────────────────────────────────────

    public void SendGameOver(PlayerSlot? winner)
    {
        var payload = new GameOverSyncPayload(winner.HasValue ? (int)winner.Value : -1);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        GD.Print($"[Sync] Sending game over: winner={winner}");
        Rpc(nameof(ReceiveGameOver), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveGameOver(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<GameOverSyncPayload>(data);
        GD.Print($"[Sync] Received game over: winner slot {payload.WinnerSlotIndex}");
        GameOverReceived?.Invoke(payload);
    }

    // ─────────────────────────────────────────────────
    //  VOXEL DAMAGE SYNC (host → clients)
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Host broadcasts authoritative voxel damage (explosion + structural collapse) to clients.
    /// Uses the same VoxelDeltaPayload format as build phase sync.
    /// </summary>
    public void SendVoxelDamage(byte[] data)
    {
        GD.Print($"[Sync] Sending voxel damage ({data.Length} bytes)");
        Rpc(nameof(ReceiveVoxelDamage), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveVoxelDamage(byte[] data)
    {
        GD.Print($"[Sync] Received voxel damage ({data.Length} bytes)");
        VoxelDamageReceived?.Invoke(data);
    }

    // ─────────────────────────────────────────────────
    //  DISCONNECT SYNC
    // ─────────────────────────────────────────────────

    public void SendDisconnect(PlayerSlot disconnected)
    {
        var payload = new DisconnectSyncPayload((int)disconnected);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        GD.Print($"[Sync] Sending disconnect for {disconnected}");
        Rpc(nameof(ReceiveDisconnect), data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveDisconnect(byte[] data)
    {
        var payload = JsonSerializer.Deserialize<DisconnectSyncPayload>(data);
        GD.Print($"[Sync] Received disconnect for slot {payload.DisconnectedSlotIndex}");
        DisconnectReceived?.Invoke(payload);
    }
}
