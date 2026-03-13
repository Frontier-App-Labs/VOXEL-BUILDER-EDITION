using Godot;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VoxelSiege.Networking.Steam;

/// <summary>
/// Manages Steam client lifecycle, lobby creation/joining, and friend invites.
/// Replaces the old reflection-based SteamPlatformNode with real Facepunch.Steamworks.
/// </summary>
public partial class SteamManager : Node
{
    [Export]
    public uint SteamAppId = 4522730;

    public string PlayerName => _initialized ? SteamClient.Name : "Player";
    public SteamId PlayerSteamId => _initialized ? SteamClient.SteamId : default;
    public bool IsInitialized => _initialized;

    public Lobby CurrentLobby { get; private set; }
    public bool InLobby { get; private set; }

    public event Action<Lobby>? LobbyCreated;
    public event Action<Lobby>? LobbyEntered;
    public event Action<Friend>? PlayerJoined;
    public event Action<Friend>? PlayerLeft;
    public event Action? LobbyJoinFailed;

    private bool _initialized;
    private static readonly char[] CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    public override void _Ready()
    {
        InitSteam();
    }

    public override void _Process(double delta)
    {
        if (_initialized)
        {
            SteamClient.RunCallbacks();
        }
    }

    public override void _ExitTree()
    {
        if (InLobby)
        {
            CurrentLobby.Leave();
            InLobby = false;
        }
        if (_initialized)
        {
            SteamClient.Shutdown();
            _initialized = false;
        }
    }

    private void InitSteam()
    {
        try
        {
            SteamClient.Init(SteamAppId, asyncCallbacks: true);
            _initialized = true;
            GD.Print($"[SteamManager] Steam initialized! Player: {SteamClient.Name} (ID: {SteamClient.SteamId})");

            // Pre-initialize the relay network so ConnectRelay doesn't timeout
            // on first use (relay discovery takes several seconds)
            SteamNetworkingUtils.InitRelayNetworkAccess();
            GD.Print("[SteamManager] Relay network access initialized.");

            // Hook lobby events
            SteamMatchmaking.OnLobbyCreated += OnSteamLobbyCreated;
            SteamMatchmaking.OnLobbyEntered += OnSteamLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined += OnSteamLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberDisconnected += OnSteamLobbyMemberLeft;
            SteamMatchmaking.OnLobbyMemberLeave += OnSteamLobbyMemberLeft;
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SteamManager] Steam init failed: {ex.Message}");
            GD.PrintErr("[SteamManager] Make sure Steam client is running and steam_appid.txt exists.");
            _initialized = false;
        }
    }

    /// <summary>
    /// Creates a Steam lobby and generates a 7-char game code stored as lobby data.
    /// Returns the game code, or null on failure.
    /// </summary>
    public async Task<string?> CreateLobbyAsync(int maxPlayers = 4)
    {
        if (!_initialized) return null;

        Lobby? result = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);
        if (!result.HasValue)
        {
            GD.PrintErr("[SteamManager] Failed to create Steam lobby.");
            return null;
        }

        CurrentLobby = result.Value;
        InLobby = true;

        // Configure lobby
        CurrentLobby.SetPublic();
        CurrentLobby.SetJoinable(true);

        // Generate and store a 7-char game code
        string gameCode = GenerateGameCode();
        CurrentLobby.SetData("game_code", gameCode);
        CurrentLobby.SetData("game_name", "VoxelSiege");
        CurrentLobby.SetData("host_name", SteamClient.Name);

        // Tell Valve's relay infrastructure that this Steam ID is hosting a game server.
        // Without this, ConnectRelay() calls from clients have nowhere to route to.
        CurrentLobby.SetGameServer(SteamClient.SteamId);
        GD.Print($"[SteamManager] SetGameServer → {SteamClient.SteamId}");

        GD.Print($"[SteamManager] Lobby created! ID: {CurrentLobby.Id}, Code: {gameCode}");
        return gameCode;
    }

    /// <summary>
    /// Searches for a lobby matching the given game code and joins it.
    /// </summary>
    public async Task<bool> JoinLobbyByCodeAsync(string code)
    {
        if (!_initialized) return false;

        code = code.ToUpperInvariant().Trim();
        GD.Print($"[SteamManager] Searching for lobby with code: {code}");

        // Search for VoxelSiege lobbies
        Lobby[] lobbies = await SteamMatchmaking.LobbyList
            .WithMaxResults(50)
            .WithKeyValue("game_name", "VoxelSiege")
            .RequestAsync() ?? Array.Empty<Lobby>();

        GD.Print($"[SteamManager] Found {lobbies.Length} VoxelSiege lobbies");

        foreach (Lobby lobby in lobbies)
        {
            string? lobbyCode = lobby.GetData("game_code");
            string? gameName = lobby.GetData("game_name");
            GD.Print($"[SteamManager] Lobby {lobby.Id}: code='{lobbyCode}', game='{gameName}', owner={lobby.Owner.Id}, members={lobby.MemberCount}");
            if (string.Equals(lobbyCode, code, StringComparison.OrdinalIgnoreCase))
            {
                GD.Print($"[SteamManager] Found matching lobby! Joining {lobby.Id} (owner={lobby.Owner.Id})...");
                RoomEnter joinResult = await lobby.Join();
                GD.Print($"[SteamManager] Lobby join result: {joinResult}");
                if (joinResult == RoomEnter.Success)
                {
                    CurrentLobby = lobby;
                    InLobby = true;
                    GD.Print($"[SteamManager] Joined lobby {lobby.Id} successfully! Owner={CurrentLobby.Owner.Id}");
                    return true;
                }
                else
                {
                    GD.PrintErr($"[SteamManager] Failed to join lobby: {joinResult}");
                    LobbyJoinFailed?.Invoke();
                    return false;
                }
            }
        }

        GD.PrintErr($"[SteamManager] No lobby found with code '{code}'");
        LobbyJoinFailed?.Invoke();
        return false;
    }

    /// <summary>
    /// Gets the Steam ID of the lobby owner (the host).
    /// </summary>
    public SteamId GetLobbyHostId()
    {
        SteamId hostId = InLobby ? CurrentLobby.Owner.Id : default;
        GD.Print($"[SteamManager] GetLobbyHostId: Owner={hostId}, InLobby={InLobby}");
        return InLobby ? CurrentLobby.Owner.Id : default;
    }

    public void LeaveLobby()
    {
        if (InLobby)
        {
            CurrentLobby.Leave();
            InLobby = false;
            GD.Print("[SteamManager] Left lobby.");
        }
    }

    public void OpenInviteOverlay()
    {
        if (InLobby)
        {
            SteamFriends.OpenGameInviteOverlay(CurrentLobby.Id);
        }
    }

    public bool SetRichPresence(string key, string value)
    {
        if (!_initialized) return false;
        return SteamFriends.SetRichPresence(key, value);
    }

    private static string GenerateGameCode()
    {
        Random rng = new Random();
        char[] code = new char[7];
        for (int i = 0; i < 7; i++)
        {
            code[i] = CodeChars[rng.Next(CodeChars.Length)];
        }
        return new string(code);
    }

    private void OnSteamLobbyCreated(Result result, Lobby lobby)
    {
        if (result == Result.OK)
        {
            GD.Print($"[SteamManager] OnLobbyCreated callback: {lobby.Id}");
            LobbyCreated?.Invoke(lobby);
        }
    }

    private void OnSteamLobbyEntered(Lobby lobby)
    {
        GD.Print($"[SteamManager] OnLobbyEntered: {lobby.Id}, members: {lobby.MemberCount}");

        // Ensure game server is set on the lobby so relay connections route correctly
        if (lobby.Owner.Id == SteamClient.SteamId)
        {
            lobby.SetGameServer(SteamClient.SteamId);
            GD.Print($"[SteamManager] Re-asserted SetGameServer → {SteamClient.SteamId}");
        }

        LobbyEntered?.Invoke(lobby);
    }

    private void OnSteamLobbyMemberJoined(Lobby lobby, Friend friend)
    {
        GD.Print($"[SteamManager] Player joined: {friend.Name} ({friend.Id})");
        PlayerJoined?.Invoke(friend);
    }

    private void OnSteamLobbyMemberLeft(Lobby lobby, Friend friend)
    {
        GD.Print($"[SteamManager] Player left: {friend.Name} ({friend.Id})");
        PlayerLeft?.Invoke(friend);
    }

    private async void OnGameLobbyJoinRequested(Lobby lobby, SteamId friendId)
    {
        GD.Print($"[SteamManager] Join requested via Steam overlay for lobby {lobby.Id}");
        RoomEnter joinResult = await lobby.Join();
        if (joinResult == RoomEnter.Success)
        {
            CurrentLobby = lobby;
            InLobby = true;
            GD.Print("[SteamManager] Joined via invite!");
        }
        else
        {
            GD.PrintErr($"[SteamManager] Invite join failed: {joinResult}");
            LobbyJoinFailed?.Invoke();
        }
    }
}
