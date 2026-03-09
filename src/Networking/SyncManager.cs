using Godot;
using System.Collections.Generic;
using System.Text.Json;
using VoxelSiege.Core;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.Networking;

public sealed class BuildPhaseSnapshot
{
    public PlayerSlot Player { get; set; }
    public List<Vector3I> Positions { get; set; } = new List<Vector3I>();
    public List<ushort> Data { get; set; } = new List<ushort>();
}

public partial class SyncManager : Node
{
    public byte[] SerializeBuildSnapshot(PlayerSlot player, IEnumerable<(Vector3I Position, VoxelValue Voxel)> voxels)
    {
        BuildPhaseSnapshot snapshot = new BuildPhaseSnapshot { Player = player };
        foreach ((Vector3I position, VoxelValue voxel) in voxels)
        {
            snapshot.Positions.Add(position);
            snapshot.Data.Add(voxel.Data);
        }

        return JsonSerializer.SerializeToUtf8Bytes(snapshot);
    }

    public BuildPhaseSnapshot? DeserializeBuildSnapshot(byte[] data)
    {
        return JsonSerializer.Deserialize<BuildPhaseSnapshot>(data);
    }

    public byte[] SerializeVoxelDelta(IEnumerable<(Vector3I Position, VoxelValue Voxel)> deltas)
    {
        VoxelDeltaPayload payload = new VoxelDeltaPayload();
        foreach ((Vector3I position, VoxelValue voxel) in deltas)
        {
            payload.Positions.Add(position);
            payload.Data.Add(voxel.Data);
        }

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    public void ApplyVoxelDelta(VoxelWorld world, byte[] data, PlayerSlot? instigator = null)
    {
        VoxelDeltaPayload? payload = JsonSerializer.Deserialize<VoxelDeltaPayload>(data);
        if (payload == null)
        {
            return;
        }

        for (int index = 0; index < payload.Positions.Count && index < payload.Data.Count; index++)
        {
            world.SetVoxel(payload.Positions[index], new VoxelValue(payload.Data[index]), instigator);
        }
    }
}
