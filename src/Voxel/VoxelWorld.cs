using Godot;
using System.Collections.Concurrent;
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

    // Snapshot pool: reuse VoxelChunkSnapshot objects to reduce GC pressure
    // Fix #2: Use ConcurrentStack for thread-safe access from deferred callbacks
    private readonly ConcurrentStack<VoxelChunkSnapshot> _snapshotPool = new ConcurrentStack<VoxelChunkSnapshot>();

    // Pooled lists/sets for explosion/destruction operations
    private readonly Stack<List<Vector3I>> _listPool = new Stack<List<Vector3I>>();
    private readonly Stack<HashSet<Vector3I>> _hashSetPool = new Stack<HashSet<Vector3I>>();

    // Fix: Deferred edge chunk remesh queue to prevent floating texture faces.
    // When explosions destroy voxels, edge chunks must remesh AFTER affected chunks
    // to avoid snapshotting stale boundary data. Edge chunks are queued here and
    // drained into the main dirty queue on the next frame.
    private readonly Queue<Vector3I> _deferredEdgeRemesh = new Queue<Vector3I>();

    // Fix #1: Guard against re-entrant _Process dirty chunk processing
    private bool _processingDirtyChunks;

    // Fix #9: Throttle chunk distance culling
    private float _cullTimer;
    private Vector3 _lastCullCameraPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    private const float CullInterval = 0.5f;
    private const float CullCameraMoveSqThreshold = 4f; // 2m squared

    // Fix #10: Static neighbor direction array instead of yield-return IEnumerable
    private static readonly Vector3I[] NeighborDirections =
    {
        Vector3I.Right,
        Vector3I.Left,
        Vector3I.Up,
        Vector3I.Down,
        new Vector3I(0, 0, -1), // Forward (Godot: -Z)
        new Vector3I(0, 0, 1),  // Back (Godot: +Z)
    };

    [Export]
    public bool AutoGeneratePrototypeArena { get; set; } = true;

    public VoxelTextureAtlas TextureAtlas { get; } = new VoxelTextureAtlas();
    public int ChunkCount => _chunks.Count;

    /// <summary>
    /// Returns the chunk at the given chunk coordinates, or null if not loaded.
    /// </summary>
    public VoxelChunk? GetChunkAt(Vector3I chunkCoords)
    {
        return _chunks.TryGetValue(chunkCoords, out VoxelChunk? chunk) ? chunk : null;
    }

    public override void _Ready()
    {
        WireAtlasToShader();
        if (AutoGeneratePrototypeArena)
        {
            GeneratePrototypeArena();
        }
    }

    /// <summary>
    /// Sets the material_atlas, use_material_atlas, and atlas_tile_size uniforms
    /// on the shared opaque shader material when generated textures are available.
    /// </summary>
    private void WireAtlasToShader()
    {
        if (!TextureAtlas.HasGeneratedTextures || TextureAtlas.AtlasTexture == null)
        {
            return;
        }

        ShaderMaterial mat = VoxelChunk.GetSharedOpaqueShaderMaterial();
        mat.SetShaderParameter("material_atlas", TextureAtlas.AtlasTexture);
        mat.SetShaderParameter("use_material_atlas", true);
        mat.SetShaderParameter("atlas_tile_size", TextureAtlas.NormalizedTileSize);
        GD.Print($"[VoxelWorld] Atlas wired to shader: use_material_atlas=true, tile_size={TextureAtlas.NormalizedTileSize}");
    }

    // Fix #1: Remove async/await from _Process to prevent re-entrancy.
    // QueueRemeshAsync already handles re-entrancy via _meshingInProgress/_meshQueued.
    public override void _Process(double delta)
    {
        if (!_processingDirtyChunks)
        {
            _processingDirtyChunks = true;

            // Drain deferred edge chunks into the main dirty queue BEFORE processing.
            // These were queued last frame by ApplyBulkChanges/FillBoxBulk so that
            // affected chunks get a full frame head-start on remeshing, preventing
            // edge chunks from snapshotting stale boundary voxel data.
            while (_deferredEdgeRemesh.Count > 0)
            {
                QueueRemesh(_deferredEdgeRemesh.Dequeue());
            }

            int remeshBudget = GameConfig.MaxChunkMeshesPerFrame;
            while (remeshBudget-- > 0 && _dirtyChunkQueue.Count > 0)
            {
                Vector3I chunkCoords = _dirtyChunkQueue.Dequeue();
                _dirtyChunkSet.Remove(chunkCoords);
                // Don't check chunk.IsDirty — neighbor chunks queued for boundary
                // remesh have no direct voxel writes, so IsDirty is false, but they
                // still need remeshing because their padded snapshot reads changed
                // boundary data from adjacent chunks. QueueRemeshAsync's own
                // _meshingInProgress/_meshQueued guard prevents redundant work.
                if (_chunks.TryGetValue(chunkCoords, out VoxelChunk? chunk))
                {
                    _ = chunk.QueueRemeshAsync(TextureAtlas);
                }
            }
            _processingDirtyChunks = false;
        }

        // Fix #9: Chunk distance culling — only recompute every CullInterval seconds
        // or when camera has moved more than 2m.
        _cullTimer += (float)delta;
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera != null)
        {
            Vector3 cameraPos = camera.GlobalPosition;
            float cameraDeltaSq = _lastCullCameraPos.DistanceSquaredTo(cameraPos);
            if (_cullTimer >= CullInterval || cameraDeltaSq >= CullCameraMoveSqThreshold)
            {
                _cullTimer = 0f;
                _lastCullCameraPos = cameraPos;
                float cullDistSq = GameConfig.ChunkLODDistance * GameConfig.ChunkLODDistance;
                foreach ((Vector3I _, VoxelChunk chunk) in _chunks)
                {
                    float distSq = chunk.GlobalPosition.DistanceSquaredTo(cameraPos);
                    chunk.Visible = distSq <= cullDistSq;
                }
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

        // When a solid voxel is destroyed (replaced with air), remove grass in the
        // surrounding area. Grass blades have random offsets within 2x2 cells so we
        // need to check a 3x3 neighborhood, not just the exact position.
        if (before.IsSolid && voxel.IsAir)
        {
            Vector3 worldPos = MathHelpers.MicrovoxelToWorld(worldPosition);
            TerrainDecorator.RemoveGrassInRadius(worldPos, GameConfig.MicrovoxelMeters * 2f);
        }
    }

    public void FillBox(Vector3I minInclusive, Vector3I maxInclusive, Voxel voxel, PlayerSlot? instigator = null)
    {
        FillBoxBulk(minInclusive, maxInclusive, voxel, instigator);
    }

    /// <summary>
    /// Fix #4: Bulk fill that groups voxel writes by chunk, avoiding per-voxel dictionary lookups,
    /// remesh queues, and event emissions. Queues exactly one remesh per affected chunk.
    /// </summary>
    public void FillBoxBulk(Vector3I minInclusive, Vector3I maxInclusive, Voxel voxel, PlayerSlot? instigator = null)
    {
        // Gather all affected chunk coords and the voxels to write within each
        var affectedChunks = new Dictionary<Vector3I, VoxelChunk>();
        var edgeChunks = new HashSet<Vector3I>();

        for (int z = minInclusive.Z; z <= maxInclusive.Z; z++)
        {
            for (int y = minInclusive.Y; y <= maxInclusive.Y; y++)
            {
                for (int x = minInclusive.X; x <= maxInclusive.X; x++)
                {
                    Vector3I worldPos = new Vector3I(x, y, z);
                    Vector3I chunkCoords = MathHelpers.WorldToChunk(worldPos);

                    if (!affectedChunks.TryGetValue(chunkCoords, out VoxelChunk? chunk))
                    {
                        chunk = GetOrCreateChunk(chunkCoords);
                        affectedChunks[chunkCoords] = chunk;
                    }

                    Vector3I local = MathHelpers.WorldToLocal(worldPos);
                    Voxel before = chunk.GetVoxel(local);
                    chunk.SetVoxel(local, voxel);

                    // Track edge neighbors for remeshing
                    if (local.X == 0) edgeChunks.Add(chunkCoords + Vector3I.Left);
                    if (local.X == GameConfig.ChunkSize - 1) edgeChunks.Add(chunkCoords + Vector3I.Right);
                    if (local.Y == 0) edgeChunks.Add(chunkCoords + Vector3I.Down);
                    if (local.Y == GameConfig.ChunkSize - 1) edgeChunks.Add(chunkCoords + Vector3I.Up);
                    if (local.Z == 0) edgeChunks.Add(chunkCoords + new Vector3I(0, 0, -1));
                    if (local.Z == GameConfig.ChunkSize - 1) edgeChunks.Add(chunkCoords + new Vector3I(0, 0, 1));

                    if (instigator != null)
                    {
                        EventBus.Instance?.EmitVoxelChanged(new VoxelChangeEvent(worldPos, before.Data, voxel.Data, instigator));
                    }
                }
            }
        }

        // Queue exactly one remesh per affected chunk
        foreach (Vector3I chunkCoords in affectedChunks.Keys)
        {
            QueueRemesh(chunkCoords);
        }

        // Defer edge-neighbor remeshes by one frame so affected chunks finish
        // updating first, preventing floating texture faces at chunk boundaries.
        foreach (Vector3I neighborCoords in edgeChunks)
        {
            if (!affectedChunks.ContainsKey(neighborCoords))
            {
                _deferredEdgeRemesh.Enqueue(neighborCoords);
            }
        }
    }

    /// <summary>
    /// Applies a batch of voxel changes (from explosion processing) in one pass.
    /// Groups writes by chunk to avoid redundant dictionary lookups, and queues
    /// exactly one remesh per affected chunk. Much faster than calling SetVoxel
    /// per voxel for large explosions.
    /// </summary>
    public void ApplyBulkChanges(List<(Vector3I Position, Voxel NewVoxel)> changes, PlayerSlot? instigator)
    {
        if (changes.Count == 0)
            return;

        var affectedChunks = new Dictionary<Vector3I, VoxelChunk>();
        var edgeChunks = new HashSet<Vector3I>();

        for (int i = 0; i < changes.Count; i++)
        {
            Vector3I worldPos = changes[i].Position;
            Voxel newVoxel = changes[i].NewVoxel;
            Vector3I chunkCoords = MathHelpers.WorldToChunk(worldPos);

            if (!affectedChunks.TryGetValue(chunkCoords, out VoxelChunk? chunk))
            {
                chunk = GetOrCreateChunk(chunkCoords);
                affectedChunks[chunkCoords] = chunk;
            }

            Vector3I local = MathHelpers.WorldToLocal(worldPos);
            Voxel before = chunk.GetVoxel(local);
            chunk.SetVoxel(local, newVoxel);

            // Track edge/corner neighbors for remeshing (all chunks this voxel touches)
            int cs = GameConfig.ChunkSize - 1;
            int dx0 = local.X == 0 ? -1 : 0;
            int dx1 = local.X == cs ? 1 : 0;
            int dy0 = local.Y == 0 ? -1 : 0;
            int dy1 = local.Y == cs ? 1 : 0;
            int dz0 = local.Z == 0 ? -1 : 0;
            int dz1 = local.Z == cs ? 1 : 0;
            for (int dx = dx0; dx <= dx1; dx++)
            for (int dy = dy0; dy <= dy1; dy++)
            for (int dz = dz0; dz <= dz1; dz++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                edgeChunks.Add(chunkCoords + new Vector3I(dx, dy, dz));
            }

            EventBus.Instance?.EmitVoxelChanged(new VoxelChangeEvent(worldPos, before.Data, newVoxel.Data, instigator));
        }

        // Queue exactly one remesh per affected chunk
        foreach (Vector3I chunkCoords in affectedChunks.Keys)
        {
            QueueRemesh(chunkCoords);
        }

        // Defer edge-neighbor remeshes by one frame so affected chunks finish
        // updating first, preventing floating texture faces at chunk boundaries.
        foreach (Vector3I neighborCoords in edgeChunks)
        {
            if (!affectedChunks.ContainsKey(neighborCoords))
            {
                _deferredEdgeRemesh.Enqueue(neighborCoords);
            }
        }

        // Remove grass for any solid→air transitions so blades don't float
        for (int i = 0; i < changes.Count; i++)
        {
            if (changes[i].NewVoxel.IsAir)
            {
                Vector3 grassPos = MathHelpers.MicrovoxelToWorld(changes[i].Position);
                TerrainDecorator.RemoveGrassInRadius(grassPos, GameConfig.MicrovoxelMeters * 2f);
            }
        }
    }

    public List<Vector3I> DestroyVoxelsInRadius(Vector3 centerWorld, float radiusMicrovoxels, int damage, PlayerSlot? instigator = null)
    {
        Vector3I center = MathHelpers.WorldToMicrovoxel(centerWorld);
        int radius = Mathf.CeilToInt(radiusMicrovoxels);
        List<Vector3I> destroyed = AcquireList();
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

    /// <summary>
    /// Acquires a snapshot from the pool (or creates a new one) and populates it.
    /// Call ReturnSnapshot when meshing is complete.
    /// </summary>
    public VoxelChunkSnapshot AcquireSnapshot(Vector3I chunkCoords)
    {
        // Fix #2: Thread-safe pool via ConcurrentStack
        if (!_snapshotPool.TryPop(out VoxelChunkSnapshot? snapshot))
        {
            snapshot = new VoxelChunkSnapshot(GameConfig.ChunkSize);
        }

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

    /// <summary>
    /// Returns a snapshot to the pool for reuse.
    /// </summary>
    public void ReturnSnapshot(VoxelChunkSnapshot snapshot)
    {
        _snapshotPool.Push(snapshot);
    }

    /// <summary>
    /// Legacy CreateSnapshot kept for backward compatibility. Prefer AcquireSnapshot/ReturnSnapshot.
    /// </summary>
    public VoxelChunkSnapshot CreateSnapshot(Vector3I chunkCoords)
    {
        return AcquireSnapshot(chunkCoords);
    }

    public List<Vector3I> FindDisconnectedVoxels(Aabb searchBounds)
    {
        Vector3I scanMin = MathHelpers.WorldToMicrovoxel(searchBounds.Position);
        Vector3I scanMax = MathHelpers.WorldToMicrovoxel(searchBounds.End);

        // Extend scan down to ground so BFS finds terrain-connected voxels
        int groundLevel = GameConfig.PrototypeGroundThickness;
        scanMin.Y = System.Math.Min(scanMin.Y, groundLevel);

        // BFS bounds: let the flood fill walk further than the scan area so that
        // connection paths running outside the scan area are still discovered.
        // This prevents false positives (flagging voxels that are connected via
        // voxels just outside the search bounds).
        // Use a generous padding: at least the full scan width or 64 microvoxels
        // (32m), whichever is larger. Structures can connect to ground via long
        // paths (bridges, arches, L-shapes) that route well outside the blast zone.
        int scanWidth = System.Math.Max(scanMax.X - scanMin.X, System.Math.Max(scanMax.Y - scanMin.Y, scanMax.Z - scanMin.Z));
        int bfsPadding = System.Math.Max(64, scanWidth); // At least 64 microvoxels, or the full scan width
        Vector3I bfsMin = new Vector3I(scanMin.X - bfsPadding, 0, scanMin.Z - bfsPadding);
        Vector3I bfsMax = new Vector3I(scanMax.X + bfsPadding, scanMax.Y + bfsPadding, scanMax.Z + bfsPadding);

        Queue<Vector3I> frontier = new Queue<Vector3I>();
        HashSet<Vector3I> connected = AcquireHashSet();

        // Seed BFS from ground/foundation rows within the BFS XZ bounds
        for (int z = bfsMin.Z; z <= bfsMax.Z; z++)
        {
            for (int x = bfsMin.X; x <= bfsMax.X; x++)
            {
                for (int y = 0; y <= groundLevel; y++)
                {
                    Vector3I ground = new Vector3I(x, y, z);
                    if (GetVoxel(ground).IsSolid && connected.Add(ground))
                    {
                        frontier.Enqueue(ground);
                    }
                }
            }
        }

        while (frontier.Count > 0)
        {
            Vector3I current = frontier.Dequeue();
            // Fix #10: Use static direction array instead of yield-return iterator
            for (int d = 0; d < NeighborDirections.Length; d++)
            {
                Vector3I neighbor = current + NeighborDirections[d];
                // BFS walks within the expanded bfs bounds to find connection paths
                if (neighbor.X < bfsMin.X || neighbor.Y < bfsMin.Y || neighbor.Z < bfsMin.Z ||
                    neighbor.X > bfsMax.X || neighbor.Y > bfsMax.Y || neighbor.Z > bfsMax.Z)
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

        // Only report disconnected voxels within the original scan area (not the padded BFS area)
        List<Vector3I> disconnected = AcquireList();
        for (int z = scanMin.Z; z <= scanMax.Z; z++)
        {
            for (int y = scanMin.Y; y <= scanMax.Y; y++)
            {
                for (int x = scanMin.X; x <= scanMax.X; x++)
                {
                    Vector3I position = new Vector3I(x, y, z);
                    if (GetVoxel(position).IsSolid && !connected.Contains(position))
                    {
                        disconnected.Add(position);
                    }
                }
            }
        }

        ReturnHashSet(connected);
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
        _deferredEdgeRemesh.Clear();

        // Remove decorator nodes (grass, flags) from previous generation.
        // These are NOT VoxelChunks so the loop above doesn't catch them.
        var decoratorNodes = new List<Node>();
        foreach (Node child in GetChildren())
        {
            if (child is not VoxelChunk)
                decoratorNodes.Add(child);
        }
        foreach (Node node in decoratorNodes)
        {
            RemoveChild(node);
            node.QueueFree();
        }

        if (regeneratePrototypeArena)
        {
            GeneratePrototypeArena();
        }
    }

    public void GeneratePrototypeArena()
    {
        int halfWidth = GameConfig.PrototypeArenaWidth / 2;

        // Extend the flat ground out to cover the gap between arena edge and mountain start.
        // This ensures there is a wide flat area before mountains begin rising.
        int groundExtent = halfWidth + TerrainDecorator.MountainStartOffset;

        // Bottom layer: Foundation (indestructible bedrock)
        FillBoxBulk(
            new Vector3I(-groundExtent, 0, -groundExtent),
            new Vector3I(groundExtent, 0, groundExtent),
            Voxel.Create(VoxelMaterialType.Foundation));

        // Y=1 to Y=2: Stone (destructible, creates cool layered craters)
        FillBoxBulk(
            new Vector3I(-groundExtent, 1, -groundExtent),
            new Vector3I(groundExtent, 2, groundExtent),
            Voxel.Create(VoxelMaterialType.Stone));

        // Y=3 to Y=5: Dirt (destructible top layers, looks like grass)
        FillBoxBulk(
            new Vector3I(-groundExtent, 3, -groundExtent),
            new Vector3I(groundExtent, GameConfig.PrototypeGroundThickness - 1, groundExtent),
            Voxel.Create(VoxelMaterialType.Dirt));
    }

    // NOTE: Sun, WorldEnvironment, and sky are now created exclusively by
    // VoxelGiSetup (via GameManager.SetupVoxelGiAndLighting) to avoid duplicate
    // lighting/environment nodes that caused visual artifacts at the arena center.

    /// <summary>
    /// Steps through the voxel grid along a ray and returns the first solid voxel hit
    /// plus the face normal indicating which face was entered.
    /// </summary>
    public bool RaycastVoxel(Vector3 worldOrigin, Vector3 worldDirection, float maxDistance, out Vector3I hitPos, out Vector3I hitNormal)
    {
        return MathHelpers.RaycastVoxel(
            worldOrigin,
            worldDirection,
            maxDistance,
            pos => GetVoxel(pos).IsSolid,
            out hitPos,
            out hitNormal);
    }

    // --- Pooled collection helpers ---

    /// <summary>
    /// Acquires a List from the pool or creates a new one.
    /// </summary>
    public List<Vector3I> AcquireList()
    {
        if (_listPool.Count > 0)
        {
            List<Vector3I> list = _listPool.Pop();
            list.Clear();
            return list;
        }
        return new List<Vector3I>();
    }

    /// <summary>
    /// Returns a List to the pool for reuse.
    /// </summary>
    public void ReturnList(List<Vector3I> list)
    {
        list.Clear();
        _listPool.Push(list);
    }

    /// <summary>
    /// Acquires a HashSet from the pool or creates a new one.
    /// </summary>
    public HashSet<Vector3I> AcquireHashSet()
    {
        if (_hashSetPool.Count > 0)
        {
            HashSet<Vector3I> set = _hashSetPool.Pop();
            set.Clear();
            return set;
        }
        return new HashSet<Vector3I>();
    }

    /// <summary>
    /// Returns a HashSet to the pool for reuse.
    /// </summary>
    public void ReturnHashSet(HashSet<Vector3I> set)
    {
        set.Clear();
        _hashSetPool.Push(set);
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
        // Determine which chunk boundaries this voxel touches
        int cs = GameConfig.ChunkSize - 1;
        int dx0 = localPosition.X == 0 ? -1 : 0;
        int dx1 = localPosition.X == cs ? 1 : 0;
        int dy0 = localPosition.Y == 0 ? -1 : 0;
        int dy1 = localPosition.Y == cs ? 1 : 0;
        int dz0 = localPosition.Z == 0 ? -1 : 0;
        int dz1 = localPosition.Z == cs ? 1 : 0;

        // Defer neighbor chunk remeshes by one frame so the owning chunk finishes
        // updating first. This prevents floating texture faces at chunk boundaries
        // when SetVoxel is called in a loop (e.g. BuildSystem.ApplyBuildAction or
        // DestroyVoxelsInRadius). Without deferral, the neighbor chunk can snapshot
        // stale boundary data before all voxel writes in the batch complete.
        for (int dx = dx0; dx <= dx1; dx++)
        for (int dy = dy0; dy <= dy1; dy++)
        for (int dz = dz0; dz <= dz1; dz++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue; // skip self
            _deferredEdgeRemesh.Enqueue(chunkCoords + new Vector3I(dx, dy, dz));
        }
    }
}
