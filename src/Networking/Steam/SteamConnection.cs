using Godot;
using Steamworks;
using Steamworks.Data;

namespace VoxelSiege.Networking.Steam;

public partial class SteamConnection : RefCounted
{
    public SteamId SteamIdRaw { get; set; }
    public ulong SteamId => SteamIdRaw.Value;
    public Connection Connection { get; set; }
    public int PeerId { get; set; } = -1;

    public struct SetupPeerPayload
    {
        public int PeerId = -1;

        public SetupPeerPayload()
        {
        }
    }

    public Error Send(SteamPacketPeer packet)
    {
        return RawSend(packet);
    }

    private Error RawSend(SteamPacketPeer packet)
    {
        return GetErrorFromResult(Connection.SendMessage(packet.Data, SendType.Reliable));
    }

    private static Error GetErrorFromResult(Result result) => result switch
    {
        Result.OK => Error.Ok,
        Result.Fail => Error.Failed,
        Result.NoConnection => Error.ConnectionError,
        Result.InvalidParam => Error.InvalidParameter,
        _ => Error.Bug
    };

    public Error SendPeer(int uniqueId)
    {
        SetupPeerPayload payload = new SetupPeerPayload();
        payload.PeerId = uniqueId;
        return Send(new SteamPacketPeer(payload.ToBytes(), MultiplayerPeer.TransferModeEnum.Reliable));
    }
}
