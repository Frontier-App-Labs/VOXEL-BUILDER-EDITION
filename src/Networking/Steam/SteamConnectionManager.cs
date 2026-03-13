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

    /// <summary>
    /// With relay connections, OnConnected/OnConnecting/OnDisconnected may never
    /// be called directly (Facepunch issue #568). Only OnConnectionChanged fires.
    /// We MUST call base.OnConnectionChanged first — that's where Facepunch sets
    /// the ConnectionInfo property (needed for message sender identity) and the
    /// Connected/Connecting flags. The base also dispatches to the empty virtuals
    /// OnConnected/OnConnecting/OnDisconnected, which is harmless.
    /// </summary>
    public override void OnConnectionChanged(ConnectionInfo info)
    {
        GD.Print($"[SteamConnMgr] ConnectionChanged: state={info.State}, endReason={info.EndReason}, identity={info.Identity.SteamId}");

        // Let base class set ConnectionInfo, Connected, Connecting properties.
        base.OnConnectionChanged(info);

        switch (info.State)
        {
            case ConnectionState.Connected:
                GD.Print($"[SteamConnMgr] CONNECTED to {info.Identity.SteamId} (Connected={Connected})");
                OnConnectionEstablished?.Invoke(info);
                break;

            case ConnectionState.Connecting:
            case ConnectionState.FindingRoute:
                GD.Print($"[SteamConnMgr] Connecting/FindingRoute to {info.Identity.SteamId}...");
                break;

            case ConnectionState.ClosedByPeer:
            case ConnectionState.ProblemDetectedLocally:
                GD.Print($"[SteamConnMgr] DISCONNECTED from {info.Identity.SteamId}, state={info.State}, endReason={info.EndReason}");
                OnConnectionLost?.Invoke(info);
                break;

            default:
                GD.Print($"[SteamConnMgr] Unhandled state: {info.State}");
                break;
        }
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
