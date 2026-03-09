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

    [Export]
    public int Port { get; set; } = 24565;

    public bool IsHost { get; private set; }
    public bool IsOnline => _activePeer != null;

    public event Action<long>? PeerConnected;
    public event Action<long>? PeerDisconnected;
    public event Action<NetMessage>? MessageReceived;

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
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
        return Error.Ok;
    }

    public Error Join(string address)
    {
        ENetMultiplayerPeer peer = new ENetMultiplayerPeer();
        Error error = peer.CreateClient(address, Port);
        if (error != Error.Ok)
        {
            return error;
        }

        Multiplayer.MultiplayerPeer = peer;
        _activePeer = peer;
        IsHost = false;
        return Error.Ok;
    }

    public void Shutdown()
    {
        if (_activePeer != null)
        {
            Multiplayer.MultiplayerPeer = null;
            _activePeer.Close();
            _activePeer = null;
        }

        IsHost = false;
        _pendingOutbound.Clear();
    }

    public void QueueMessage(NetMessage message)
    {
        _pendingOutbound.Add(message);
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

    private void OnPeerConnected(long peerId)
    {
        PeerConnected?.Invoke(peerId);
    }

    private void OnPeerDisconnected(long peerId)
    {
        PeerDisconnected?.Invoke(peerId);
    }
}
