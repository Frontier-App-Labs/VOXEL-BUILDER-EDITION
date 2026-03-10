using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using VoxelSiege.Core;

namespace VoxelSiege.Networking;

public partial class NetworkManager : Node
{
    private readonly List<NetMessage> _pendingOutbound = new List<NetMessage>();
    private MultiplayerPeer? _activePeer;
    private Upnp? _upnp;
    private bool _upnpMapped;

    [Export]
    public int Port { get; set; } = 24565;

    public bool IsHost { get; private set; }
    public bool IsOnline => _activePeer != null;

    /// <summary>
    /// The external (public) IP discovered via UPnP, or the LAN IP as fallback.
    /// Set after Host() completes UPnP discovery.
    /// </summary>
    public string ExternalIp { get; private set; } = string.Empty;

    /// <summary>
    /// The local peer ID assigned by ENet (1 for host, unique positive int for clients).
    /// Returns 0 if not connected.
    /// </summary>
    public long LocalPeerId => _activePeer != null ? Multiplayer.GetUniqueId() : 0;

    /// <summary>
    /// Display name of the local player. Set before hosting or joining.
    /// </summary>
    public string LocalDisplayName { get; set; } = "Player";

    public event Action<long>? PeerConnected;
    public event Action<long>? PeerDisconnected;
    public event Action? ConnectedToServer;
    public event Action? ConnectionFailed;
    public event Action<NetMessage>? MessageReceived;

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Shutdown();
    }

    public Error Host(int maxPlayers = GameConfig.MaxPlayers)
    {
        ENetMultiplayerPeer peer = new ENetMultiplayerPeer();
        Error error = peer.CreateServer(Port, maxPlayers);
        if (error != Error.Ok)
        {
            return error;
        }

        Multiplayer.MultiplayerPeer = peer;
        _activePeer = peer;
        IsHost = true;
        GD.Print($"[NetworkManager] Hosting on port {Port} as peer {Multiplayer.GetUniqueId()}");

        // Use UPnP to auto-forward the port and discover external IP.
        // This runs in a background thread so it doesn't block the UI.
        SetupUpnpAsync();

        return Error.Ok;
    }

    /// <summary>
    /// Uses UPnP to discover the gateway, forward the game port (UDP),
    /// and retrieve the external (public) IP address. Runs on a worker
    /// thread because UPnP discovery can take 2-5 seconds.
    /// </summary>
    private void SetupUpnpAsync()
    {
        _upnp = new Upnp();
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                int discoverResult = _upnp.Discover();
                if (discoverResult != (int)Upnp.UpnpResult.Success)
                {
                    GD.Print($"[NetworkManager] UPnP discover failed (code {discoverResult}). Players may need the LAN IP.");
                    // Fall back to LAN IP
                    CallDeferred(nameof(SetExternalIpDeferred), GetLanAddress());
                    return;
                }

                // Query the external IP from the gateway
                string externalIp = _upnp.QueryExternalAddress();
                if (string.IsNullOrEmpty(externalIp))
                {
                    GD.Print("[NetworkManager] UPnP: could not query external IP. Using LAN IP.");
                    CallDeferred(nameof(SetExternalIpDeferred), GetLanAddress());
                    return;
                }

                // Add port mapping (UDP only — ENet uses UDP)
                int mapResult = _upnp.AddPortMapping(Port, Port, "VoxelSiege", "UDP");
                if (mapResult == (int)Upnp.UpnpResult.Success)
                {
                    _upnpMapped = true;
                    GD.Print($"[NetworkManager] UPnP: port {Port}/UDP forwarded. External IP: {externalIp}");
                }
                else
                {
                    GD.Print($"[NetworkManager] UPnP: port mapping failed (code {mapResult}). External IP: {externalIp}");
                }

                CallDeferred(nameof(SetExternalIpDeferred), externalIp);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[NetworkManager] UPnP exception: {ex.Message}");
                CallDeferred(nameof(SetExternalIpDeferred), GetLanAddress());
            }
        });
    }

    private void SetExternalIpDeferred(string ip)
    {
        ExternalIp = ip;
        GD.Print($"[NetworkManager] ExternalIp set to: {ip}");
        ExternalIpDiscovered?.Invoke(ip);
    }

    /// <summary>
    /// Fired when UPnP discovery completes and the external IP is available.
    /// The MainMenu subscribes to update the displayed game code.
    /// </summary>
    public event Action<string>? ExternalIpDiscovered;

    /// <summary>
    /// Returns the best local LAN IPv4 address (prefers 192.168.x.x, 10.x.x.x).
    /// </summary>
    private static string GetLanAddress()
    {
        string[] addresses = IP.GetLocalAddresses();
        string? best = null;
        foreach (string addr in addresses)
        {
            if (addr.Contains(':')) continue;
            if (addr == "127.0.0.1") continue;
            if (addr.StartsWith("192.168.") || addr.StartsWith("10."))
                return addr;
            best ??= addr;
        }
        return best ?? "127.0.0.1";
    }

    /// <summary>
    /// Join a host. The address can be a plain IP ("192.168.1.100") or IP:port ("192.168.1.100:24565").
    /// </summary>
    public Error Join(string address)
    {
        int port = Port;
        string host = address;

        // Support IP:port format
        int colonIndex = address.LastIndexOf(':');
        if (colonIndex > 0)
        {
            string portStr = address.Substring(colonIndex + 1);
            if (int.TryParse(portStr, out int parsedPort) && parsedPort > 0 && parsedPort <= 65535)
            {
                host = address.Substring(0, colonIndex);
                port = parsedPort;
            }
        }

        ENetMultiplayerPeer peer = new ENetMultiplayerPeer();
        Error error = peer.CreateClient(host, port);
        if (error != Error.Ok)
        {
            return error;
        }

        Multiplayer.MultiplayerPeer = peer;
        _activePeer = peer;
        IsHost = false;
        GD.Print($"[NetworkManager] Joining {host}:{port}");
        return Error.Ok;
    }

    public void Shutdown()
    {
        // Remove UPnP port mapping if we created one
        if (_upnpMapped && _upnp != null)
        {
            try
            {
                _upnp.DeletePortMapping(Port, "UDP");
                GD.Print($"[NetworkManager] UPnP: removed port mapping for {Port}/UDP");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[NetworkManager] UPnP cleanup error: {ex.Message}");
            }
            _upnpMapped = false;
        }
        _upnp = null;
        ExternalIp = string.Empty;

        if (_activePeer != null)
        {
            Multiplayer.MultiplayerPeer = null;
            _activePeer.Close();
            _activePeer = null;
        }

        IsHost = false;
        _pendingOutbound.Clear();
        GD.Print("[NetworkManager] Shutdown complete.");
    }

    public void QueueMessage(NetMessage message)
    {
        _pendingOutbound.Add(message);
    }

    /// <summary>
    /// Sends a message immediately (bypasses the queue).
    /// </summary>
    public void SendMessage(NetMessage message)
    {
        if (_activePeer == null) return;
        byte[] bytes = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(message));
        Rpc(nameof(ReceiveMessage), bytes);
    }

    public override void _Process(double delta)
    {
        if (_activePeer == null || _pendingOutbound.Count == 0)
        {
            return;
        }

        foreach (NetMessage message in _pendingOutbound)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(message));
            Rpc(nameof(ReceiveMessage), bytes);
        }

        _pendingOutbound.Clear();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveMessage(byte[] payload)
    {
        NetMessage? message = System.Text.Json.JsonSerializer.Deserialize<NetMessage>(Encoding.UTF8.GetString(payload));
        if (message.HasValue)
        {
            MessageReceived?.Invoke(message.Value);
        }
    }

    /// <summary>
    /// RPC used by clients to announce their display name to the host upon connection.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void AnnouncePlayer(string displayName)
    {
        long senderId = Multiplayer.GetRemoteSenderId();
        GD.Print($"[NetworkManager] Player announced: '{displayName}' (peer {senderId})");
        PlayerAnnounced?.Invoke(senderId, displayName);
    }

    /// <summary>
    /// Fired when a remote peer announces their display name.
    /// Parameters: (peerId, displayName).
    /// </summary>
    public event Action<long, string>? PlayerAnnounced;

    /// <summary>
    /// RPC used by the host to broadcast the full lobby state to all peers.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void BroadcastLobbyState(byte[] payload)
    {
        LobbyStatePayload? state = System.Text.Json.JsonSerializer.Deserialize<LobbyStatePayload>(
            Encoding.UTF8.GetString(payload));
        if (state.HasValue)
        {
            LobbyStateReceived?.Invoke(state.Value);
        }
    }

    /// <summary>
    /// Fired when a lobby state broadcast is received from the host.
    /// </summary>
    public event Action<LobbyStatePayload>? LobbyStateReceived;

    /// <summary>
    /// RPC used to toggle ready state. Clients send to host, host can broadcast.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void SetPlayerReady(bool ready)
    {
        long senderId = Multiplayer.GetRemoteSenderId();
        PlayerReadyChanged?.Invoke(senderId, ready);
    }

    /// <summary>
    /// Fired when a peer changes their ready state.
    /// Parameters: (peerId, isReady).
    /// </summary>
    public event Action<long, bool>? PlayerReadyChanged;

    /// <summary>
    /// RPC used by the host to tell all clients to start the match.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void StartMatch(byte[] settingsPayload)
    {
        MatchStartPayload? settings = System.Text.Json.JsonSerializer.Deserialize<MatchStartPayload>(
            Encoding.UTF8.GetString(settingsPayload));
        if (settings.HasValue)
        {
            MatchStartReceived?.Invoke(settings.Value);
        }
    }

    /// <summary>
    /// Fired when the host tells us to start the match.
    /// </summary>
    public event Action<MatchStartPayload>? MatchStartReceived;

    private void OnPeerConnected(long peerId)
    {
        GD.Print($"[NetworkManager] Peer connected: {peerId}");
        PeerConnected?.Invoke(peerId);
    }

    private void OnPeerDisconnected(long peerId)
    {
        GD.Print($"[NetworkManager] Peer disconnected: {peerId}");
        PeerDisconnected?.Invoke(peerId);
    }

    private void OnConnectedToServer()
    {
        GD.Print($"[NetworkManager] Connected to server as peer {Multiplayer.GetUniqueId()}");
        // Announce our name to the host
        RpcId(1, nameof(AnnouncePlayer), LocalDisplayName);
        ConnectedToServer?.Invoke();
    }

    private void OnConnectionFailed()
    {
        GD.PrintErr("[NetworkManager] Connection to server failed.");
        Shutdown();
        ConnectionFailed?.Invoke();
    }
}
