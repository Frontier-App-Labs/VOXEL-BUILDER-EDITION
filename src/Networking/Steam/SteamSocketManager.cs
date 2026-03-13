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

    /// <summary>
    /// With relay connections, OnConnected/OnConnecting/OnDisconnected are never
    /// called (Facepunch.Steamworks issue #568). Only OnConnectionChanged fires.
    /// We handle all state transitions here, including accepting incoming connections.
    /// </summary>
    public override void OnConnectionChanged(Connection connection, ConnectionInfo info)
    {
        GD.Print($"[SteamSocketMgr] ConnectionChanged: state={info.State}, steamId={info.Identity.SteamId}, endReason={info.EndReason}");

        switch (info.State)
        {
            case ConnectionState.Connecting:
                GD.Print($"[SteamSocketMgr] INCOMING connection from {info.Identity.SteamId}, accepting...");
                connection.Accept();
                break;

            case ConnectionState.Connected:
                GD.Print($"[SteamSocketMgr] CONNECTED: {info.Identity.SteamId}");
                if (!_connectionMessages.ContainsKey(connection))
                {
                    _connectionMessages.Add(connection, new Queue<SteamNetworkingMessage>());
                }
                OnConnectionEstablished?.Invoke((connection, info));
                break;

            case ConnectionState.ClosedByPeer:
            case ConnectionState.ProblemDetectedLocally:
                GD.Print($"[SteamSocketMgr] DISCONNECTED: {info.Identity.SteamId}, state={info.State}, endReason={info.EndReason}");
                _connectionMessages.Remove(connection);
                OnConnectionLost?.Invoke((connection, info));
                break;

            default:
                GD.Print($"[SteamSocketMgr] Unhandled state: {info.State} for {info.Identity.SteamId}");
                break;
        }
    }

    // Keep these as no-ops — they won't fire with relay, but just in case:
    public override void OnConnected(Connection connection, ConnectionInfo info)
    {
        GD.Print($"[SteamSocketMgr] OnConnected callback (unexpected with relay): {info.Identity.SteamId}");
        base.OnConnected(connection, info);
    }

    public override void OnConnecting(Connection connection, ConnectionInfo info)
    {
        GD.Print($"[SteamSocketMgr] OnConnecting callback (unexpected with relay): {info.Identity.SteamId}");
        base.OnConnecting(connection, info);
    }

    public override void OnDisconnected(Connection connection, ConnectionInfo info)
    {
        GD.Print($"[SteamSocketMgr] OnDisconnected callback (unexpected with relay): {info.Identity.SteamId}");
        base.OnDisconnected(connection, info);
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
