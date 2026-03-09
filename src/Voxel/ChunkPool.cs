using System.Collections.Generic;

namespace VoxelSiege.Voxel;

public sealed class ChunkPool
{
    private readonly Stack<VoxelChunk> _available = new Stack<VoxelChunk>();

    public VoxelChunk GetOrCreate()
    {
        return _available.Count > 0 ? _available.Pop() : new VoxelChunk();
    }

    public void Return(VoxelChunk chunk)
    {
        chunk.ResetChunk();
        _available.Push(chunk);
    }
}
