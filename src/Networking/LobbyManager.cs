using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Core;

namespace VoxelSiege.Networking;

public sealed class LobbyMember
{
    public long PeerId { get; set; }
    public PlayerSlot Slot { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool Ready { get; set; }
}

public partial class LobbyManager : Node
{
    private readonly Dictionary<long, LobbyMember> _membersByPeer = new Dictionary<long, LobbyMember>();

    public string LobbyName { get; private set; } = "Voxel Siege Lobby";
    public MatchSettings Settings { get; } = new MatchSettings();
    public IReadOnlyDictionary<long, LobbyMember> Members => _membersByPeer;

    /// <summary>
    /// Fired whenever the member list changes (player joins, leaves, or updates ready state).
    /// </summary>
    public event Action? MembersChanged;

    public void ConfigureLobby(string lobbyName, MatchSettings settings)
    {
        LobbyName = lobbyName;
        Settings.Visibility = settings.Visibility;
        Settings.BuildTimeSeconds = settings.BuildTimeSeconds;
        Settings.StartingBudget = settings.StartingBudget;
        Settings.ArenaSize = settings.ArenaSize;
        Settings.TurnTimeSeconds = settings.TurnTimeSeconds;
        Settings.WeaponTierCap = settings.WeaponTierCap;
        Settings.FriendlyFire = settings.FriendlyFire;
        Settings.FogMode = settings.FogMode;
    }

    public void AddOrUpdateMember(long peerId, PlayerSlot slot, string displayName, bool ready)
    {
        _membersByPeer[peerId] = new LobbyMember
        {
            PeerId = peerId,
            Slot = slot,
            DisplayName = displayName,
            Ready = ready,
        };
        MembersChanged?.Invoke();
    }

    public void RemoveMember(long peerId)
    {
        if (_membersByPeer.Remove(peerId))
        {
            MembersChanged?.Invoke();
        }
    }

    /// <summary>
    /// Removes all members and resets the lobby name.
    /// </summary>
    public void Clear()
    {
        _membersByPeer.Clear();
        LobbyName = "Voxel Siege Lobby";
        MembersChanged?.Invoke();
    }

    /// <summary>
    /// Assigns the next available PlayerSlot for a new peer.
    /// Returns null if the lobby is full.
    /// </summary>
    public PlayerSlot? GetNextAvailableSlot()
    {
        PlayerSlot[] allSlots = { PlayerSlot.Player1, PlayerSlot.Player2, PlayerSlot.Player3, PlayerSlot.Player4 };
        HashSet<PlayerSlot> usedSlots = new HashSet<PlayerSlot>();
        foreach (LobbyMember member in _membersByPeer.Values)
        {
            usedSlots.Add(member.Slot);
        }
        foreach (PlayerSlot slot in allSlots)
        {
            if (!usedSlots.Contains(slot))
            {
                return slot;
            }
        }
        return null;
    }

    /// <summary>
    /// Toggles the ready state of the member with the given peer ID.
    /// Returns the new ready state, or null if the peer is not found.
    /// </summary>
    public bool? ToggleReady(long peerId)
    {
        if (_membersByPeer.TryGetValue(peerId, out LobbyMember? member))
        {
            member.Ready = !member.Ready;
            MembersChanged?.Invoke();
            return member.Ready;
        }
        return null;
    }

    /// <summary>
    /// Sets the ready state of the member with the given peer ID.
    /// </summary>
    public void SetReady(long peerId, bool ready)
    {
        if (_membersByPeer.TryGetValue(peerId, out LobbyMember? member))
        {
            member.Ready = ready;
            MembersChanged?.Invoke();
        }
    }

    public bool AreAllPlayersReady()
    {
        if (_membersByPeer.Count < GameConfig.MinPlayers)
        {
            return false;
        }

        foreach (LobbyMember member in _membersByPeer.Values)
        {
            if (!member.Ready)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds the lobby state payload for network broadcast.
    /// </summary>
    public LobbyStatePayload BuildStatePayload()
    {
        var players = new List<LobbyPlayerInfo>();
        foreach (LobbyMember member in _membersByPeer.Values)
        {
            players.Add(new LobbyPlayerInfo(
                member.PeerId,
                (int)member.Slot,
                member.DisplayName,
                member.Ready));
        }
        return new LobbyStatePayload(players.ToArray());
    }

    /// <summary>
    /// Applies a lobby state payload received from the host (client-side).
    /// Replaces all current members with the payload data.
    /// </summary>
    public void ApplyStatePayload(LobbyStatePayload payload)
    {
        _membersByPeer.Clear();
        foreach (LobbyPlayerInfo info in payload.Players)
        {
            _membersByPeer[info.PeerId] = new LobbyMember
            {
                PeerId = info.PeerId,
                Slot = (PlayerSlot)info.SlotIndex,
                DisplayName = info.DisplayName,
                Ready = info.IsReady,
            };
        }
        MembersChanged?.Invoke();
    }
}
