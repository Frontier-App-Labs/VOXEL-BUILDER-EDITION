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
