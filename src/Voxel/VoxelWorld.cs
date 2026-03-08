using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Utility;

namespace VoxelSiege.Voxel;

public partial class VoxelWorld : Node3D
{
    private readonly Dictionary<Vector3I, VoxelChunk> _chunks = new Dictionary<Vector3I, VoxelChunk>();
    private readonly ChunkPool _chunkPool = new ChunkPool();
    private readonly Queue<Vector3I> _dirtyChunkQueue = new Queue<Vector3I>();
    private readonly HashSet<Vector3I> _dirtyChunkSet = new HashSet<Vector3I>();

    [Export]
    public bool AutoGeneratePrototypeArena { get; set; } = true;

    public VoxelTextureAtlas TextureAtlas { get; } = new VoxelTextureAtlas();
    public int ChunkCount => _chunks.Count;

    public override void _Ready()
    {
        EnsurePrototypeScenery();
        if (AutoGeneratePrototypeArena)
        {
            GeneratePrototypeArena();
        }
    }

    public override async void _Process(double delta)
    {
        int remeshBudget = GameConfig.MaxChunkMeshesPerFrame;
        while (remeshBudget-- > 0 && _dirtyChunkQueue.Count > 0)
        {
            Vector3I chunkCoords = _dirtyChunkQueue.Dequeue();
            _dirtyChunkSet.Remove(chunkCoords);
            if (_chunks.TryGetValue(chunkCoords, out VoxelChunk? chunk) && chunk.IsDirty)
            {
                await chunk.QueueRemeshAsync(TextureAtlas);
            }
        }
    }

    public Voxel GetVoxel(Vector3I worldPosition)
    {
        Vector3I chunkCoords = MathHelpers.WorldToChunk(worldPosition);
        if (!_chunks.TryGetValue(chunkCoords, out VoxelChunk? chunk))
        {
            return Voxel.Air;
        }

        Vector3I local = MathHelpers.WorldToLocal(worldPosition);
        return chunk.GetVoxel(local);
    }

    public void SetVoxel(Vector3I worldPosition, Voxel voxel, PlayerSlot? instigator = null)
    {
        Vector3I chunkCoords = MathHelpers.WorldToChunk(worldPosition);
        VoxelChunk chunk = GetOrCreateChunk(chunkCoords);
        Vector3I local = MathHelpers.WorldToLocal(worldPosition);
        Voxel before = chunk.GetVoxel(local);
        chunk.SetVoxel(local, voxel);
        QueueRemesh(chunkCoords);
        QueueNeighborRemeshesOnEdge(local, chunkCoords);
        EventBus.Instance?.EmitVoxelChanged(new VoxelChangeEvent(worldPosition, before.Data, voxel.Data, instigator));
    }

    public void FillBox(Vector3I minInclusive, Vector3I maxInclusive, Voxel voxel, PlayerSlot? instigator = null)
    {
        for (int z = minInclusive.Z; z <= maxInclusive.Z; z++)
        {
            for (int y = minInclusive.Y; y <= maxInclusive.Y; y++)
            {
                for (int x = minInclusive.X; x <= maxInclusive.X; x++)
                {
                    SetVoxel(new Vector3I(x, y, z), voxel, instigator);
                }
            }
        }
    }

    public List<Vector3I> DestroyVoxelsInRadius(Vector3 centerWorld, float radiusMicrovoxels, int damage, PlayerSlot? instigator = null)
    {
        Vector3I center = MathHelpers.WorldToMicrovoxel(centerWorld);
        int radius = Mathf.CeilToInt(radiusMicrovoxels);
        List<Vector3I> destroyed = new List<Vector3I>();
        foreach (Vector3I position in MathHelpers.EnumerateSphere(center, radius))
        {
            Voxel voxel = GetVoxel(position);
            if (voxel.IsAir || voxel.Material == VoxelMaterialType.Foundation)
            {
                continue;
            }

            int nextHitPoints = voxel.HitPoints - damage;
            if (nextHitPoints <= 0)
            {
                SetVoxel(position, Voxel.Air, instigator);
                destroyed.Add(position);
            }
            else
            {
                SetVoxel(position, voxel.WithHitPoints(nextHitPoints).WithDamaged(true), instigator);
            }
        }

        return destroyed;
    }

    public VoxelChunkSnapshot CreateSnapshot(Vector3I chunkCoords)
    {
        VoxelChunkSnapshot snapshot = new VoxelChunkSnapshot(GameConfig.ChunkSize);
        Vector3I worldBase = chunkCoords * GameConfig.ChunkSize;
        for (int z = -1; z <= GameConfig.ChunkSize; z++)
        {
            for (int y = -1; y <= GameConfig.ChunkSize; y++)
            {
                for (int x = -1; x <= GameConfig.ChunkSize; x++)
                {
                    Vector3I local = new Vector3I(x, y, z);
                    snapshot.Set(local, GetVoxel(worldBase + local));
                }
            }
        }

        return snapshot;
    }

    public List<Vector3I> FindDisconnectedVoxels(Aabb searchBounds)
    {
        Vector3I min = MathHelpers.WorldToMicrovoxel(searchBounds.Position);
        Vector3I max = MathHelpers.WorldToMicrovoxel(searchBounds.End);
        Queue<Vector3I> frontier = new Queue<Vector3I>();
        HashSet<Vector3I> connected = new HashSet<Vector3I>();

        for (int z = min.Z; z <= max.Z; z++)
        {
            for (int x = min.X; x <= max.X; x++)
            {
                Vector3I ground = new Vector3I(x, min.Y, z);
                if (GetVoxel(ground).IsSolid)
                {
                    frontier.Enqueue(ground);
                    connected.Add(ground);
                }
            }
        }

        while (frontier.Count > 0)
        {
            Vector3I current = frontier.Dequeue();
            foreach (Vector3I neighbor in EnumerateNeighbors(current))
            {
                if (neighbor.X < min.X || neighbor.Y < min.Y || neighbor.Z < min.Z || neighbor.X > max.X || neighbor.Y > max.Y || neighbor.Z > max.Z)
                {
                    continue;
                }

                if (connected.Contains(neighbor) || GetVoxel(neighbor).IsAir)
                {
                    continue;
                }

                connected.Add(neighbor);
                frontier.Enqueue(neighbor);
            }
        }

        List<Vector3I> disconnected = new List<Vector3I>();
        for (int z = min.Z; z <= max.Z; z++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    Vector3I position = new Vector3I(x, y, z);
                    if (GetVoxel(position).IsSolid && !connected.Contains(position))
                    {
                        disconnected.Add(position);
                    }
                }
            }
        }

        return disconnected;
    }

    public void ClearWorld(bool regeneratePrototypeArena = true)
    {
        foreach ((Vector3I chunkCoords, VoxelChunk chunk) in _chunks)
        {
            RemoveChild(chunk);
            _chunkPool.Return(chunk);
        }

        _chunks.Clear();
        _dirtyChunkQueue.Clear();
        _dirtyChunkSet.Clear();

        if (regeneratePrototypeArena)
        {
            GeneratePrototypeArena();
        }
    }

    public void GeneratePrototypeArena()
    {
        int halfWidth = GameConfig.PrototypeArenaWidth / 2;
        int halfDepth = GameConfig.PrototypeArenaDepth / 2;
        FillBox(
            new Vector3I(-halfWidth, 0, -halfDepth),
            new Vector3I(halfWidth, GameConfig.PrototypeGroundThickness - 1, halfDepth),
            Voxel.Create(VoxelMaterialType.Foundation));
    }

    private void EnsurePrototypeScenery()
    {
        if (GetNodeOrNull<DirectionalLight3D>("Sun") == null)
        {
            DirectionalLight3D sun = new DirectionalLight3D();
            sun.Name = "Sun";
            sun.LightEnergy = 1.8f;
            sun.ShadowEnabled = true;
            sun.RotationDegrees = new Vector3(-45f, -30f, 0f);
            AddChild(sun);
        }

        if (GetNodeOrNull<WorldEnvironment>("WorldEnvironment") == null)
        {
            WorldEnvironment worldEnvironment = new WorldEnvironment();
            worldEnvironment.Name = "WorldEnvironment";

            Environment environment = new Environment();
            environment.BackgroundMode = Environment.BGMode.Sky;
            environment.AmbientLightSource = Environment.AmbientSource.Sky;
            environment.AmbientLightEnergy = 0.8f;
            environment.TonemapMode = Environment.ToneMapper.Aces;

            ProceduralSkyMaterial sky = new ProceduralSkyMaterial();
            sky.SkyTopColor = new Color("6aa4ff");
            sky.SkyHorizonColor = new Color("b5d5ff");
            sky.GroundBottomColor = new Color("4c5a32");
            sky.GroundHorizonColor = new Color("768b4a");

            Sky skyResource = new Sky();
            skyResource.SkyMaterial = sky;
            environment.Sky = skyResource;
            worldEnvironment.Environment = environment;
            AddChild(worldEnvironment);
        }
    }

    private static IEnumerable<Vector3I> EnumerateNeighbors(Vector3I origin)
    {
        yield return origin + Vector3I.Right;
        yield return origin + Vector3I.Left;
        yield return origin + Vector3I.Up;
        yield return origin + Vector3I.Down;
        yield return origin + Vector3I.Forward;
        yield return origin + Vector3I.Back;
    }

    private VoxelChunk GetOrCreateChunk(Vector3I chunkCoords)
    {
        if (_chunks.TryGetValue(chunkCoords, out VoxelChunk? existingChunk))
        {
            return existingChunk;
        }

        VoxelChunk chunk = _chunkPool.GetOrCreate();
        AddChild(chunk);
        chunk.Initialize(this, chunkCoords);
        _chunks[chunkCoords] = chunk;
        return chunk;
    }

    private void QueueRemesh(Vector3I chunkCoords)
    {
        if (!_dirtyChunkSet.Add(chunkCoords))
        {
            return;
        }

        _dirtyChunkQueue.Enqueue(chunkCoords);
    }

    private void QueueNeighborRemeshesOnEdge(Vector3I localPosition, Vector3I chunkCoords)
    {
        if (localPosition.X == 0)
        {
            QueueRemesh(chunkCoords + Vector3I.Left);
        }

        if (localPosition.X == GameConfig.ChunkSize - 1)
        {
            QueueRemesh(chunkCoords + Vector3I.Right);
        }

        if (localPosition.Y == 0)
        {
            QueueRemesh(chunkCoords + Vector3I.Down);
        }

        if (localPosition.Y == GameConfig.ChunkSize - 1)
        {
            QueueRemesh(chunkCoords + Vector3I.Up);
        }

        if (localPosition.Z == 0)
        {
            QueueRemesh(chunkCoords + Vector3I.Back);
        }

        if (localPosition.Z == GameConfig.ChunkSize - 1)
        {
            QueueRemesh(chunkCoords + Vector3I.Forward);
        }
    }
}
