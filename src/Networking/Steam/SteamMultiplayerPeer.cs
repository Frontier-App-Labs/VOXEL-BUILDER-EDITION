using Godot;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VoxelSiege.Networking.Steam;

/// <summary>
/// Drop-in replacement for ENetMultiplayerPeer that uses Steam Networking Sockets.
/// All traffic goes through Valve's relay servers — no port forwarding needed.
/// Based on https://github.com/Pieeer1/SteamMultiplayerPeer
/// </summary>
public partial class SteamMultiplayerPeer : MultiplayerPeerExtension
{
    public enum Mode
    {
        None,
        Server,
        Client
    }

    private const int MaxPacketSize = 524288;

    private SteamConnectionManager? _steamConnectionManager;
    private SteamSocketManager? _steamSocketManager;

    private readonly Dictionary<int, SteamConnection> _peerIdToConnection = new();
    private readonly Dictionary<ulong, SteamConnection> _connectionsBySteamId = new();

    private int _targetPeer = -1;
    private int _uniqueId = 0;
    private Mode _mode;
    private bool IsActive => _mode != Mode.None;
    private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;
    private TransferModeEnum _transferMode = TransferModeEnum.Reliable;

    private readonly Queue<SteamPacketPeer?> _incomingPackets = new();

    private SteamId _steamId;
    private int _transferChannel = 0;
    private bool _refuseNewConnections = false;

    public Error CreateHost(SteamId playerId)
    {
        _steamId = playerId;
        if (IsActive)
        {
            return Error.AlreadyInUse;
        }

        GD.Print($"[SteamMultiplayerPeer] Relay network status: {SteamNetworkingUtils.Status}");
        _steamSocketManager = SteamNetworkingSockets.CreateRelaySocket<SteamSocketManager>();
        GD.Print("[SteamMultiplayerPeer] Relay socket created.");
        _steamConnectionManager = SteamNetworkingSockets.ConnectRelay<SteamConnectionManager>(playerId);
        GD.Print("[SteamMultiplayerPeer] Self-connection relay created.");

        _steamSocketManager.OnConnectionEstablished += (c) =>
        {
            if (c.Item2.Identity.SteamId != _steamId)
            {
                AddConnection(c.Item2.Identity.SteamId, c.Item1);
                GD.Print($"[SteamMultiplayerPeer] New connection from Steam ID {c.Item2.Identity.SteamId}");
            }
        };

        _steamSocketManager.OnConnectionLost += (c) =>
        {
            if (c.Item2.Identity.SteamId != _steamId)
            {
                if (_connectionsBySteamId.TryGetValue(c.Item2.Identity.SteamId, out SteamConnection? conn))
                {
                    GD.Print($"[SteamMultiplayerPeer] Connection lost: Steam ID {c.Item2.Identity.SteamId}, peer {conn.PeerId}");
                    EmitSignal(SignalName.PeerDisconnected, conn.PeerId);
                    _peerIdToConnection.Remove(conn.PeerId);
                    _connectionsBySteamId.Remove(c.Item2.Identity.SteamId);
                }
            }
        };

        _uniqueId = 1;
        _mode = Mode.Server;
        _connectionStatus = ConnectionStatus.Connected;
        GD.Print($"[SteamMultiplayerPeer] Hosting as Steam ID {playerId}");
        return Error.Ok;
    }

    public Error CreateClient(SteamId playerId, SteamId hostId)
    {
        _steamId = playerId;
        if (IsActive)
        {
            return Error.AlreadyInUse;
        }

        _uniqueId = (int)GenerateUniqueId();

        GD.Print($"[SteamMultiplayerPeer] Client relay status: {SteamNetworkingUtils.Status}");
        _steamConnectionManager = SteamNetworkingSockets.ConnectRelay<SteamConnectionManager>(hostId);
        GD.Print($"[SteamMultiplayerPeer] Connecting to host relay {hostId}...");

        _steamConnectionManager.OnConnectionEstablished += (connection) =>
        {
            if (connection.Identity.SteamId != _steamId)
            {
                AddConnection(connection.Identity.SteamId, _steamConnectionManager.Connection);
                _connectionStatus = ConnectionStatus.Connected;
                _connectionsBySteamId[connection.Identity.SteamId].SendPeer(_uniqueId);
                GD.Print($"[SteamMultiplayerPeer] Connected to host Steam ID {connection.Identity.SteamId}");
            }
        };

        _steamConnectionManager.OnConnectionLost += (connection) =>
        {
            if (connection.Identity.SteamId != _steamId)
            {
                if (_connectionsBySteamId.TryGetValue(connection.Identity.SteamId, out SteamConnection? conn))
                {
                    EmitSignal(SignalName.PeerDisconnected, conn.PeerId);
                    _peerIdToConnection.Remove(conn.PeerId);
                    _connectionsBySteamId.Remove(connection.Identity.SteamId);
                }
            }
        };

        _mode = Mode.Client;
        _connectionStatus = ConnectionStatus.Connecting;
        GD.Print($"[SteamMultiplayerPeer] Connecting to host Steam ID {hostId}");
        return Error.Ok;
    }

    public override void _Close()
    {
        if (!IsActive || _connectionStatus != ConnectionStatus.Connected) return;

        foreach (Connection connection in _steamSocketManager?.Connected ?? Enumerable.Empty<Connection>())
        {
            connection.Close();
        }
        if (_IsServer())
        {
            _steamConnectionManager?.Close();
        }

        _steamSocketManager?.Close();
        _peerIdToConnection.Clear();
        _connectionsBySteamId.Clear();
        _mode = Mode.None;
        _uniqueId = 0;
        _connectionStatus = ConnectionStatus.Disconnected;
        GD.Print("[SteamMultiplayerPeer] Closed.");
    }

    public override void _DisconnectPeer(int pPeer, bool pForce)
    {
        SteamConnection? connection = GetConnectionFromPeer(pPeer);
        if (connection == null) return;

        connection.Connection.Close();
        connection.Connection.Flush();
        _connectionsBySteamId.Remove(connection.SteamId);
        _peerIdToConnection.Remove(pPeer);

        if (_mode == Mode.Client || _mode == Mode.Server)
        {
            GetConnectionFromPeer(0)?.Connection.Flush();
        }
        if (pForce && _mode == Mode.Client)
        {
            _connectionsBySteamId.Clear();
            _Close();
        }
    }

    public override int _GetAvailablePacketCount() => _incomingPackets.Count;

    public override ConnectionStatus _GetConnectionStatus() => _connectionStatus;

    public override int _GetMaxPacketSize() => MaxPacketSize;

    public override int _GetPacketChannel() => 0;

    public override TransferModeEnum _GetPacketMode() =>
        _incomingPackets.FirstOrDefault()?.TransferMode ?? TransferModeEnum.Reliable;

    public override int _GetPacketPeer()
    {
        SteamPacketPeer? front = _incomingPackets.FirstOrDefault();
        if (front == null) return 0;
        if (_connectionsBySteamId.TryGetValue(front.Value.SenderSteamId, out SteamConnection? conn))
            return conn.PeerId;
        return 0;
    }

    public override byte[] _GetPacketScript()
    {
        if (_incomingPackets.TryDequeue(out SteamPacketPeer? packet))
        {
            return packet?.Data ?? Array.Empty<byte>();
        }
        return Array.Empty<byte>();
    }

    public override int _GetTransferChannel() => _transferChannel;

    public override TransferModeEnum _GetTransferMode() => _transferMode;

    public override int _GetUniqueId() => _uniqueId;

    public override bool _IsRefusingNewConnections() => _refuseNewConnections;

    public override bool _IsServer() => _uniqueId == 1;

    public override bool _IsServerRelaySupported() => _mode == Mode.Server || _mode == Mode.Client;

    private float _pollLogTimer;

    public override void _Poll()
    {
        _steamSocketManager?.Receive();

        // IMPORTANT: Always call Receive() on the connection manager, even before
        // Connected is true. With relay connections, OnConnectionChanged (which sets
        // Connected=true) fires DURING Receive(). If we only call Receive() when
        // already Connected, the client can never transition to Connected state.
        if (_steamConnectionManager != null)
        {
            _steamConnectionManager.Receive();

            if (!_steamConnectionManager.Connected)
            {
                // Log periodically while waiting for connection (every 2s)
                _pollLogTimer += 0.016f; // approximate frame time
                if (_pollLogTimer > 2f)
                {
                    _pollLogTimer = 0f;
                    GD.Print($"[SteamMultiplayerPeer] _Poll: still waiting for connection... mode={_mode}, status={_connectionStatus}, connMgr.Connected={_steamConnectionManager.Connected}, peers={_connectionsBySteamId.Count}");
                }
            }
        }

        System.Collections.Generic.IEnumerable<SteamNetworkingMessage> steamNetworkingMessages =
            _steamConnectionManager?.GetPendingMessages() ?? Enumerable.Empty<SteamNetworkingMessage>();

        foreach (SteamConnection connection in _connectionsBySteamId.Values.ToList())
        {
            System.Collections.Generic.IEnumerable<SteamNetworkingMessage> messagesByConnection =
                steamNetworkingMessages.Union(
                    _steamSocketManager?.ReceiveMessagesOnConnection(connection.Connection)
                    ?? Enumerable.Empty<SteamNetworkingMessage>());

            foreach (SteamNetworkingMessage message in messagesByConnection)
            {
                if (GetPeerIdFromSteamId(message.Sender) != -1)
                {
                    ProcessMessage(message);
                }
                else
                {
                    SteamConnection.SetupPeerPayload? receive =
                        message.Data.ToStruct<SteamConnection.SetupPeerPayload>();
                    if (receive.HasValue)
                    {
                        ProcessPing(receive.Value, message.Sender);
                    }
                }
            }
        }
    }

    private void ProcessPing(SteamConnection.SetupPeerPayload receive, ulong sender)
    {
        GD.Print($"[SteamMultiplayerPeer] ProcessPing: peerId={receive.PeerId} from steamId={sender}");
        if (!_connectionsBySteamId.TryGetValue(sender, out SteamConnection? connection))
        {
            GD.PrintErr($"[SteamMultiplayerPeer] ProcessPing: sender {sender} not found in connections!");
            return;
        }

        if (receive.PeerId != -1)
        {
            if (connection.PeerId == -1)
            {
                SetSteamIdPeer(sender, receive.PeerId);
            }
            if (_IsServer())
            {
                connection.SendPeer(_uniqueId);
            }

            EmitSignal(SignalName.PeerConnected, receive.PeerId);
        }
    }

    private void SetSteamIdPeer(ulong steamId, int peerId)
    {
        if (_connectionsBySteamId.TryGetValue(steamId, out SteamConnection? steamConnection)
            && steamConnection.PeerId == -1)
        {
            steamConnection.PeerId = peerId;
            _peerIdToConnection[peerId] = steamConnection;
        }
    }

    private void ProcessMessage(SteamNetworkingMessage message)
    {
        SteamPacketPeer packet = new SteamPacketPeer(message.Data, TransferModeEnum.Reliable);
        packet.SenderSteamId = message.Sender;
        _incomingPackets.Enqueue(packet);
    }

    public override Error _PutPacketScript(byte[] pBuffer)
    {
        if (!IsActive || _connectionStatus != ConnectionStatus.Connected) return Error.Unconfigured;

        if (_targetPeer != 0 && !_peerIdToConnection.ContainsKey(Mathf.Abs(_targetPeer)))
        {
            return Error.InvalidParameter;
        }

        if (_mode == Mode.Client && !_peerIdToConnection.ContainsKey(1))
        {
            return Error.Bug;
        }

        SteamPacketPeer packet = new SteamPacketPeer(pBuffer, _transferMode);

        if (_targetPeer == 0)
        {
            Error error = Error.Ok;
            foreach (SteamConnection connection in _connectionsBySteamId.Values)
            {
                Error packetSendingError = connection.Send(packet);
                if (packetSendingError != Error.Ok)
                {
                    return packetSendingError;
                }
            }
            return error;
        }
        else
        {
            return GetConnectionFromPeer(_targetPeer)?.Send(packet) ?? Error.Unavailable;
        }
    }

    public override void _SetRefuseNewConnections(bool pEnable) => _refuseNewConnections = pEnable;
    public override void _SetTargetPeer(int pPeer) => _targetPeer = pPeer;
    public override void _SetTransferChannel(int pChannel) => _transferChannel = pChannel;
    public override void _SetTransferMode(TransferModeEnum pMode) => _transferMode = pMode;

    private SteamConnection? GetConnectionFromPeer(int peerId)
    {
        _peerIdToConnection.TryGetValue(peerId, out SteamConnection? value);
        return value;
    }

    private int GetPeerIdFromSteamId(ulong steamId)
    {
        if (steamId == _steamId.Value)
            return _uniqueId;
        if (_connectionsBySteamId.TryGetValue(steamId, out SteamConnection? value))
            return value.PeerId;
        return -1;
    }

    private void AddConnection(SteamId steamId, Connection connection)
    {
        if (steamId == _steamId.Value)
            return;

        SteamConnection connectionData = new SteamConnection
        {
            Connection = connection,
            SteamIdRaw = steamId
        };
        _connectionsBySteamId[steamId] = connectionData;
    }
}
