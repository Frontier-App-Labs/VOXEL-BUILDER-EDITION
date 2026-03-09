using Godot;
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
    }

    public void RemoveMember(long peerId)
    {
        _membersByPeer.Remove(peerId);
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
}
