using Godot;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VoxelSiege.Networking.Steam;

public class SteamConnectionManager : ConnectionManager
{
    public event Action<ConnectionInfo>? OnConnectionEstablished;
    public event Action<ConnectionInfo>? OnConnectionLost;

    private readonly Queue<SteamNetworkingMessage> _pendingMessages = new();

    public override void OnConnectionChanged(ConnectionInfo info)
    {
        base.OnConnectionChanged(info);
    }

    public override void OnConnected(ConnectionInfo info)
    {
        base.OnConnected(info);
        OnConnectionEstablished?.Invoke(info);
    }

    public override void OnConnecting(ConnectionInfo info)
    {
        base.OnConnecting(info);
    }

    public override void OnDisconnected(ConnectionInfo info)
    {
        base.OnDisconnected(info);
        OnConnectionLost?.Invoke(info);
    }

    public override void OnMessage(IntPtr data, int size, long recvTime, long messageNum, int channel)
    {
        base.OnMessage(data, size, messageNum, recvTime, channel);

        byte[] managedArray = new byte[size];
        Marshal.Copy(data, managedArray, 0, size);

        _pendingMessages.Enqueue(
            new SteamNetworkingMessage(managedArray, ConnectionInfo.Identity.SteamId,
                MultiplayerPeer.TransferModeEnum.Reliable, recvTime));
    }

    public System.Collections.Generic.IEnumerable<SteamNetworkingMessage> GetPendingMessages()
    {
        int maxMessageCount = _pendingMessages.Count;
        for (int i = 0; i < maxMessageCount; i++)
        {
            yield return _pendingMessages.Dequeue();
        }
    }
}
