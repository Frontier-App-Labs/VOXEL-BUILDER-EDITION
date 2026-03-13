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
    /// With relay connections, OnConnected/OnConnecting/OnDisconnected are never
    /// called (Facepunch.Steamworks issue #568). Only OnConnectionChanged fires.
    /// We handle all state transitions here.
    /// </summary>
    public override void OnConnectionChanged(ConnectionInfo info)
    {
        GD.Print($"[SteamConnMgr] ConnectionChanged: state={info.State}, endReason={info.EndReason}, identity={info.Identity.SteamId}");

        switch (info.State)
        {
            case ConnectionState.Connected:
                GD.Print($"[SteamConnMgr] CONNECTED to {info.Identity.SteamId}");
                // Set the base class Connected property so _Poll can call Receive()
                Connected = true;
                Connecting = false;
                OnConnectionEstablished?.Invoke(info);
                break;

            case ConnectionState.Connecting:
            case ConnectionState.FindingRoute:
                GD.Print($"[SteamConnMgr] Connecting/FindingRoute to {info.Identity.SteamId}...");
                Connecting = true;
                break;

            case ConnectionState.ClosedByPeer:
            case ConnectionState.ProblemDetectedLocally:
                GD.Print($"[SteamConnMgr] DISCONNECTED from {info.Identity.SteamId}, state={info.State}, endReason={info.EndReason}");
                Connected = false;
                Connecting = false;
                OnConnectionLost?.Invoke(info);
                break;

            default:
                GD.Print($"[SteamConnMgr] Unhandled state: {info.State}");
                break;
        }
    }

    // Keep these as no-ops — they won't fire with relay, but just in case:
    public override void OnConnected(ConnectionInfo info)
    {
        GD.Print($"[SteamConnMgr] OnConnected callback (unexpected with relay): {info.Identity.SteamId}");
        base.OnConnected(info);
    }

    public override void OnConnecting(ConnectionInfo info)
    {
        GD.Print($"[SteamConnMgr] OnConnecting callback (unexpected with relay): {info.Identity.SteamId}");
        base.OnConnecting(info);
    }

    public override void OnDisconnected(ConnectionInfo info)
    {
        GD.Print($"[SteamConnMgr] OnDisconnected callback (unexpected with relay): {info.Identity.SteamId}");
        base.OnDisconnected(info);
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
