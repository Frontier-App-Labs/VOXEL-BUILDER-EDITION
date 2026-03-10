using Godot;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VoxelSiege.Networking.Steam;

public class SteamSocketManager : SocketManager
{
    private readonly Dictionary<Connection, Queue<SteamNetworkingMessage>> _connectionMessages = new();

    public event Action<(Connection, ConnectionInfo)>? OnConnectionEstablished;
    public event Action<(Connection, ConnectionInfo)>? OnConnectionLost;

    public override void OnConnectionChanged(Connection connection, ConnectionInfo info)
    {
        base.OnConnectionChanged(connection, info);
    }

    public override void OnConnected(Connection connection, ConnectionInfo info)
    {
        base.OnConnected(connection, info);
        _connectionMessages.Add(connection, new Queue<SteamNetworkingMessage>());
        OnConnectionEstablished?.Invoke((connection, info));
    }

    public override void OnConnecting(Connection connection, ConnectionInfo info)
    {
        base.OnConnecting(connection, info);
    }

    public override void OnDisconnected(Connection connection, ConnectionInfo info)
    {
        base.OnDisconnected(connection, info);
        _connectionMessages.Remove(connection);
        OnConnectionLost?.Invoke((connection, info));
    }

    public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size,
        long recvTime, long messageNum, int channel)
    {
        base.OnMessage(connection, identity, data, size, messageNum, recvTime, channel);

        byte[] managedArray = new byte[size];
        Marshal.Copy(data, managedArray, 0, size);

        if (_connectionMessages.ContainsKey(connection))
        {
            _connectionMessages[connection].Enqueue(
                new SteamNetworkingMessage(managedArray, identity.SteamId,
                    MultiplayerPeer.TransferModeEnum.Reliable, recvTime));
        }
    }

    public System.Collections.Generic.IEnumerable<SteamNetworkingMessage> ReceiveMessagesOnConnection(
        Connection connection)
    {
        if (!_connectionMessages.TryGetValue(connection, out Queue<SteamNetworkingMessage>? queue))
            yield break;

        int messageCount = queue.Count;
        for (int i = 0; i < messageCount; i++)
        {
            yield return queue.Dequeue();
        }
    }
}
