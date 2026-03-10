using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using VoxelSiege.Core;

namespace VoxelSiege.Networking;

public enum NetMessageType
{
    PlayerJoin,
    PlayerLeave,
    PlayerReady,
    MatchSettings,
    PlaceVoxel,
    RemoveVoxel,
    PlaceWeapon,
    PlaceCommander,
    BuildPhaseSync,
    TurnStart,
    WeaponSelect,
    AimUpdate,
    FireWeapon,
    ProjectileSpawn,
    ProjectileUpdate,
    ProjectileImpact,
    VoxelDestroyed,
    CommanderDamaged,
    CommanderKilled,
    WeaponDisabled,
    TurnEnd,
    ChatMessage,
    Ping,
    StateSync,
    GameOver,
}

public readonly record struct NetMessage(NetMessageType Type, long SenderPeerId, string PayloadJson, long TimestampTicks)
{
    public static NetMessage Create<TPayload>(NetMessageType type, long senderPeerId, TPayload payload)
    {
        return new NetMessage(type, senderPeerId, JsonSerializer.Serialize(payload), DateTime.UtcNow.Ticks);
    }

    public TPayload? Deserialize<TPayload>()
    {
        return JsonSerializer.Deserialize<TPayload>(PayloadJson);
    }
}

public sealed class VoxelDeltaPayload
{
    public List<Vector3I> Positions { get; set; } = new List<Vector3I>();
    public List<ushort> Data { get; set; } = new List<ushort>();
}

public readonly record struct AimUpdatePayload(string WeaponId, float YawRadians, float PitchRadians, float PowerPercent);
public readonly record struct FireWeaponPayload(string WeaponId, float YawRadians, float PitchRadians, float PowerPercent);
public readonly record struct CommanderPayload(PlayerSlot Player, Vector3 WorldPosition, int Health);
public readonly record struct PlayerReadyPayload(PlayerSlot Player, bool IsReady);
public readonly record struct ChatMessagePayload(PlayerSlot Player, string Text);

// Lobby payloads
public readonly record struct PlayerJoinPayload(string DisplayName, long PeerId);
public readonly record struct PlayerLeavePayload(long PeerId);
public readonly record struct LobbyStatePayload(LobbyPlayerInfo[] Players);
public readonly record struct LobbyPlayerInfo(long PeerId, int SlotIndex, string DisplayName, bool IsReady);
public readonly record struct MatchStartPayload(float BuildTimeSeconds, int StartingBudget, int ArenaSize, float TurnTimeSeconds);

// Combat sync payloads
public readonly record struct WeaponFireSyncPayload(int PlayerSlotIndex, int WeaponIndex, float YawRadians, float PitchRadians, float PowerPercent);
public readonly record struct CommanderDamageSyncPayload(int VictimSlotIndex, int Damage, int RemainingHealth, float PosX, float PosY, float PosZ, bool IsCriticalHit);
public readonly record struct CommanderDeathSyncPayload(int VictimSlotIndex, int KillerSlotIndex, float PosX, float PosY, float PosZ);
public readonly record struct TurnAdvanceSyncPayload(int CurrentPlayerSlotIndex, int RoundNumber, float TurnTimeSeconds);
public readonly record struct PhaseChangeSyncPayload(int PreviousPhase, int CurrentPhase);
public readonly record struct WeaponDestroyedSyncPayload(int OwnerSlotIndex, string WeaponId, float PosX, float PosY, float PosZ);
