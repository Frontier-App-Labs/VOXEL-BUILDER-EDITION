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
    /// With relay connections, OnConnected/OnConnecting/OnDisconnected may never
    /// be called directly (Facepunch issue #568). Only OnConnectionChanged fires.
    /// We call the base virtual methods explicitly to ensure Facepunch's internal
    /// Connected list is maintained (required for Receive() to process messages).
    /// </summary>
    public override void OnConnectionChanged(Connection connection, ConnectionInfo info)
    {
        GD.Print($"[SteamSocketMgr] ConnectionChanged: state={info.State}, steamId={info.Identity.SteamId}, endReason={info.EndReason}");

        switch (info.State)
        {
            case ConnectionState.Connecting:
                GD.Print($"[SteamSocketMgr] INCOMING connection from {info.Identity.SteamId}, accepting...");
                // base.OnConnecting calls connection.Accept() and tracks internally
                base.OnConnecting(connection, info);
                break;

            case ConnectionState.FindingRoute:
                // Transitional state for relay — just wait
                GD.Print($"[SteamSocketMgr] FindingRoute for {info.Identity.SteamId}...");
                break;

            case ConnectionState.Connected:
                GD.Print($"[SteamSocketMgr] CONNECTED: {info.Identity.SteamId}");
                // base.OnConnected adds to internal Connected list (needed by Receive)
                base.OnConnected(connection, info);
                if (!_connectionMessages.ContainsKey(connection))
                {
                    _connectionMessages.Add(connection, new Queue<SteamNetworkingMessage>());
                }
                OnConnectionEstablished?.Invoke((connection, info));
                break;

            case ConnectionState.ClosedByPeer:
            case ConnectionState.ProblemDetectedLocally:
                GD.Print($"[SteamSocketMgr] DISCONNECTED: {info.Identity.SteamId}, state={info.State}, endReason={info.EndReason}");
                base.OnDisconnected(connection, info);
                _connectionMessages.Remove(connection);
                OnConnectionLost?.Invoke((connection, info));
                break;

            default:
                GD.Print($"[SteamSocketMgr] Unhandled state: {info.State} for {info.Identity.SteamId}");
                break;
        }
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
