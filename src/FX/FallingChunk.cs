using Godot;
using System.Collections.Generic;
using VoxelSiege.Core;
using VoxelSiege.Utility;
using VoxelSiege.Voxel;
using VoxelValue = VoxelSiege.Voxel.Voxel;

namespace VoxelSiege.FX;

/// <summary>
/// Converts disconnected voxel groups into falling RigidBody3D physics chunks.
/// Generates a mesh from visible faces, applies collision, and handles impact behaviour.
/// During fall, sheds crumble particles for a visual collapse effect.
/// On shatter, spawns material-aware debris and a dust cloud.
/// </summary>
public partial class FallingChunk : RigidBody3D
{
    private const int MaxActiveFallingChunks = 80;
    private const float MaxLifetime = 15.0f;
    private const float SettleVelocityThreshold = 2.0f;
    private const float SettleTimeRequired = 0.5f;
    private const float ShatterImpactVelocity = 18.0f;
    private const int CompoundCollisionThreshold = 8;
    private const int SimpleBoxCollisionThreshold = 32;

    // Crumble particle shedding during fall
    private const float CrumbleInterval = 0.15f;      // Seconds between crumble particle spawns
    private const int MaxCrumbleParticles = 8;         // Max crumble spawns per chunk per fall
    private const float CrumbleSpeedThreshold = 1.5f;  // Minimum speed to shed crumble particles

    private static readonly List<FallingChunk> ActiveChunks = new();

    private float _age;
    private float _settleTimer;
    private bool _isFrozen;
    private List<Vector3I> _voxelPositions = new();
    private List<Color> _voxelColors = new();
    private List<VoxelMaterialType> _voxelMaterials = new();
    private float _impactVelocity;
    private float _crumbleTimer;
    private int _crumbleCount;
    private bool _hasHitGround;

    // 6 cardinal directions for face culling
    private static readonly Vector3I[] FaceDirections =
    {
        Vector3I.Right,
        Vector3I.Left,
        Vector3I.Up,
        Vector3I.Down,
        new Vector3I(0, 0, -1),
        new Vector3I(0, 0, 1),
    };

    private static readonly Vector3[] FaceNormals =
    {
        Vector3.Right,
        Vector3.Left,
        Vector3.Up,
        Vector3.Down,
        Vector3.Forward,  // -Z in Godot
        Vector3.Back,     // +Z in Godot
    };

    // ── Cleanup ─────────────────────────────────────────────────────────

    /// <summary>
    /// Immediately frees all active falling chunks.
    /// Call when transitioning between game phases.
    /// </summary>
    public static void ClearAll()
    {
        // Snapshot the list since Free() triggers _ExitTree which modifies ActiveChunks
        List<FallingChunk> snapshot = new List<FallingChunk>(ActiveChunks);
        ActiveChunks.Clear();
        foreach (FallingChunk chunk in snapshot)
        {
            if (IsInstanceValid(chunk))
            {
                chunk.Free();
            }
        }
    }

    // ── Creation ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a FallingChunk from a list of connected, disconnected voxel positions.
    /// Reads material colors, removes voxels from the world, builds mesh and collision.
    /// </summary>
    public static FallingChunk? Create(List<Vector3I> voxelPositions, VoxelWorld world, Vector3 explosionCenter)
    {
        if (voxelPositions.Count == 0)
        {
            return null;
        }

        // Enforce active chunk cap
        CleanupExcess();
        if (ActiveChunks.Count >= MaxActiveFallingChunks)
        {
            // Force-remove oldest chunk to make room
            FallingChunk oldest = ActiveChunks[0];
            oldest.ForceCleanup();
        }

        FallingChunk chunk = new FallingChunk();
        chunk._voxelPositions = new List<Vector3I>(voxelPositions);

        // Compute center of mass (in world-space meters)
        Vector3 centerOfMass = Vector3.Zero;
        HashSet<Vector3I> positionSet = new HashSet<Vector3I>(voxelPositions);

        chunk._voxelColors = new List<Color>(voxelPositions.Count);
        chunk._voxelMaterials = new List<VoxelMaterialType>(voxelPositions.Count);

        for (int i = 0; i < voxelPositions.Count; i++)
        {
            Vector3I pos = voxelPositions[i];
            VoxelValue voxel = world.GetVoxel(pos);
            Color color = VoxelMaterials.GetPreviewColor(voxel.Material);
            chunk._voxelColors.Add(color);
            chunk._voxelMaterials.Add(voxel.Material);
            centerOfMass += MathHelpers.MicrovoxelToWorld(pos);

            // Remove voxel from world
            world.SetVoxel(pos, VoxelValue.Air);
        }

        centerOfMass /= voxelPositions.Count;

        // Build the visible-face mesh
        ArrayMesh? mesh = BuildChunkMesh(voxelPositions, positionSet, chunk._voxelColors, centerOfMass);
        if (mesh != null)
        {
            MeshInstance3D meshInstance = new MeshInstance3D();
            meshInstance.Mesh = mesh;

            StandardMaterial3D mat = new StandardMaterial3D();
            mat.VertexColorUseAsAlbedo = true;
            mat.Roughness = 0.8f;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
            meshInstance.SetSurfaceOverrideMaterial(0, mat);

            chunk.AddChild(meshInstance);
        }

        // Build collision shape
        BuildCollisionShape(chunk, voxelPositions, positionSet, centerOfMass, mesh);

        // Physics properties
        chunk.Mass = Mathf.Max(0.5f, voxelPositions.Count * 0.1f);
        chunk.GravityScale = 1.2f;
        chunk.ContactMonitor = true;
        chunk.MaxContactsReported = 4;
        chunk.CollisionLayer = 4;  // Debris layer
        chunk.CollisionMask = 1;   // Collide with world

        // Add to scene tree FIRST (required for physics operations and GlobalPosition)
        Node root = world.GetTree().Root;
        root.AddChild(chunk);
        chunk.GlobalPosition = centerOfMass;

        // Apply initial impulse away from explosion center (must be in tree)
        Vector3 impulseDir = (centerOfMass - explosionCenter).Normalized();
        if (impulseDir.LengthSquared() < 0.01f)
        {
            impulseDir = Vector3.Up;
        }
        float impulseMag = Mathf.Clamp(voxelPositions.Count * 0.5f, 2.0f, 15.0f);
        chunk.ApplyImpulse(impulseDir * impulseMag + Vector3.Up * impulseMag * 0.5f);

        // Add angular velocity for visual tumbling
        chunk.AngularVelocity = new Vector3(
            (float)GD.RandRange(-3.0, 3.0),
            (float)GD.RandRange(-3.0, 3.0),
            (float)GD.RandRange(-3.0, 3.0));
        ActiveChunks.Add(chunk);

        return chunk;
    }

    // ── Mesh generation ─────────────────────────────────────────────────

    /// <summary>
    /// Generates a mesh with only the exposed faces (faces whose neighbor is not in the group).
    /// Uses vertex colors from material preview colors.
    /// </summary>
    private static ArrayMesh? BuildChunkMesh(
        List<Vector3I> positions,
        HashSet<Vector3I> positionSet,
        List<Color> colors,
        Vector3 centerOfMass)
    {
        SurfaceTool st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float scale = GameConfig.MicrovoxelMeters;
        bool hasAnyFaces = false;

        for (int i = 0; i < positions.Count; i++)
        {
            Vector3I voxelPos = positions[i];
            Color color = colors[i];
            Vector3 localOrigin = MathHelpers.MicrovoxelToWorld(voxelPos) - centerOfMass;

            for (int face = 0; face < 6; face++)
            {
                Vector3I neighborPos = voxelPos + FaceDirections[face];
                if (positionSet.Contains(neighborPos))
                {
                    continue; // Neighbor in group, face is hidden
                }

                // Add this face as a quad (two triangles)
                AddFaceQuad(st, localOrigin, face, scale, FaceNormals[face], color);
                hasAnyFaces = true;
            }
        }

        if (!hasAnyFaces)
        {
            return null;
        }

        // NOTE: Do NOT call st.GenerateNormals() here — normals are already set
        // manually per-vertex above. GenerateNormals() recalculates from winding
        // and can strip vertex color data in Godot 4's SurfaceTool, causing the
        // mesh to render as transparent/white artifacts instead of colored voxels.
        return st.Commit();
    }

    /// <summary>
    /// Adds a single voxel face (quad) to the SurfaceTool.
    /// </summary>
    private static void AddFaceQuad(SurfaceTool st, Vector3 origin, int face, float size, Vector3 normal, Color color)
    {
        // Define the 4 corners of each face relative to voxel origin
        Vector3 v0, v1, v2, v3;

        switch (face)
        {
            case 0: // +X (Right)
                v0 = origin + new Vector3(size, 0, 0);
                v1 = origin + new Vector3(size, size, 0);
                v2 = origin + new Vector3(size, size, size);
                v3 = origin + new Vector3(size, 0, size);
                break;
            case 1: // -X (Left)
                v0 = origin + new Vector3(0, 0, size);
                v1 = origin + new Vector3(0, size, size);
                v2 = origin + new Vector3(0, size, 0);
                v3 = origin + new Vector3(0, 0, 0);
                break;
            case 2: // +Y (Up)
                v0 = origin + new Vector3(0, size, 0);
                v1 = origin + new Vector3(0, size, size);
                v2 = origin + new Vector3(size, size, size);
                v3 = origin + new Vector3(size, size, 0);
                break;
            case 3: // -Y (Down)
                v0 = origin + new Vector3(0, 0, size);
                v1 = origin + new Vector3(0, 0, 0);
                v2 = origin + new Vector3(size, 0, 0);
                v3 = origin + new Vector3(size, 0, size);
                break;
            case 4: // -Z (Forward)
                v0 = origin + new Vector3(size, 0, 0);
                v1 = origin + new Vector3(size, size, 0);
                v2 = origin + new Vector3(0, size, 0);
                v3 = origin + new Vector3(0, 0, 0);
                break;
            default: // +Z (Back)
                v0 = origin + new Vector3(0, 0, size);
                v1 = origin + new Vector3(0, size, size);
                v2 = origin + new Vector3(size, size, size);
                v3 = origin + new Vector3(size, 0, size);
                break;
        }

        // Triangle 1: v0, v2, v1 (clockwise winding for Godot 4 front faces)
        st.SetColor(color);
        st.SetNormal(normal);
        st.AddVertex(v0);
        st.SetColor(color);
        st.SetNormal(normal);
        st.AddVertex(v2);
        st.SetColor(color);
        st.SetNormal(normal);
        st.AddVertex(v1);

        // Triangle 2: v0, v3, v2 (clockwise winding for Godot 4 front faces)
        st.SetColor(color);
        st.SetNormal(normal);
        st.AddVertex(v0);
        st.SetColor(color);
        st.SetNormal(normal);
        st.AddVertex(v3);
        st.SetColor(color);
        st.SetNormal(normal);
        st.AddVertex(v2);
    }

    // ── Collision shape ─────────────────────────────────────────────────

    private static void BuildCollisionShape(
        FallingChunk chunk,
        List<Vector3I> positions,
        HashSet<Vector3I> positionSet,
        Vector3 centerOfMass,
        ArrayMesh? mesh)
    {
        float scale = GameConfig.MicrovoxelMeters;

        if (positions.Count < CompoundCollisionThreshold)
        {
            // Use individual BoxShape3D per voxel (compound collision)
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 localCenter = MathHelpers.MicrovoxelToWorld(positions[i]) - centerOfMass
                    + Vector3.One * scale * 0.5f;

                CollisionShape3D collider = new CollisionShape3D();
                BoxShape3D box = new BoxShape3D();
                box.Size = Vector3.One * scale;
                collider.Shape = box;
                collider.Position = localCenter;
                chunk.AddChild(collider);
            }
        }
        else if (positions.Count >= SimpleBoxCollisionThreshold)
        {
            // Large chunks: use a single bounding box to avoid expensive per-vertex convex shapes
            CreateBoundingBoxCollider(chunk, positions, centerOfMass, scale);
        }
        else
        {
            // Medium chunks: use ConvexPolygonShape3D from the mesh vertices
            if (mesh != null && mesh.GetSurfaceCount() > 0)
            {
                Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
                if (arrays.Count > 0 && arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array() is Vector3[] vertices && vertices.Length >= 4)
                {
                    ConvexPolygonShape3D convexShape = new ConvexPolygonShape3D();
                    convexShape.Points = vertices;

                    CollisionShape3D collider = new CollisionShape3D();
                    collider.Shape = convexShape;
                    chunk.AddChild(collider);
                }
                else
                {
                    // Fallback: bounding box
                    CreateBoundingBoxCollider(chunk, positions, centerOfMass, scale);
                }
            }
            else
            {
                CreateBoundingBoxCollider(chunk, positions, centerOfMass, scale);
            }
        }
    }

    private static void CreateBoundingBoxCollider(FallingChunk chunk, List<Vector3I> positions, Vector3 centerOfMass, float scale)
    {
        Vector3I min = positions[0];
        Vector3I max = positions[0];
        for (int i = 1; i < positions.Count; i++)
        {
            Vector3I p = positions[i];
            if (p.X < min.X) min = new Vector3I(p.X, min.Y, min.Z);
            if (p.Y < min.Y) min = new Vector3I(min.X, p.Y, min.Z);
            if (p.Z < min.Z) min = new Vector3I(min.X, min.Y, p.Z);
            if (p.X > max.X) max = new Vector3I(p.X, max.Y, max.Z);
            if (p.Y > max.Y) max = new Vector3I(max.X, p.Y, max.Z);
            if (p.Z > max.Z) max = new Vector3I(max.X, max.Y, p.Z);
        }

        Vector3 minWorld = MathHelpers.MicrovoxelToWorld(min);
        Vector3 maxWorld = MathHelpers.MicrovoxelToWorld(max) + Vector3.One * scale;
        Vector3 size = maxWorld - minWorld;
        Vector3 localCenter = ((minWorld + maxWorld) * 0.5f) - centerOfMass;

        CollisionShape3D collider = new CollisionShape3D();
        BoxShape3D box = new BoxShape3D();
        box.Size = size;
        collider.Shape = box;
        collider.Position = localCenter;
        chunk.AddChild(collider);
    }

    // ── Physics callbacks ───────────────────────────────────────────────

    public override void _PhysicsProcess(double delta)
    {
        if (_isFrozen)
        {
            return;
        }

        float dt = (float)delta;
        _age += dt;

        // Force cleanup after max lifetime
        if (_age >= MaxLifetime)
        {
            ForceCleanup();
            return;
        }

        // Track settling
        float speed = LinearVelocity.Length();
        if (speed < SettleVelocityThreshold)
        {
            _settleTimer += dt;
        }
        else
        {
            _settleTimer = 0f;
            _impactVelocity = Mathf.Max(_impactVelocity, speed);
        }

        // Shed crumble particles while airborne and moving fast enough
        if (!_hasHitGround && speed > CrumbleSpeedThreshold && _crumbleCount < MaxCrumbleParticles)
        {
            _crumbleTimer += dt;
            if (_crumbleTimer >= CrumbleInterval)
            {
                _crumbleTimer -= CrumbleInterval;
                SpawnCrumbleParticle();
                _crumbleCount++;
            }
        }

        // Check for collision with ground
        if (GetContactCount() > 0)
        {
            if (!_hasHitGround)
            {
                _hasHitGround = true;

                // Spawn a dust cloud on first ground impact
                SpawnImpactDust();
            }

            float currentSpeed = LinearVelocity.Length();

            // High-speed impact: shatter into material-aware debris
            if (_impactVelocity > ShatterImpactVelocity && currentSpeed < ShatterImpactVelocity * 0.5f)
            {
                Shatter();
                return;
            }

            // Low velocity for long enough: freeze as static ruin
            if (_settleTimer >= SettleTimeRequired)
            {
                FreezeAsRuin();
                return;
            }
        }
    }

    private void FreezeAsRuin()
    {
        _isFrozen = true;
        Freeze = true;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        CollisionLayer = 0;
        CollisionMask = 0;
    }

    private void Shatter()
    {
        Node fxParent = GetTree().Root;
        Vector3 center = GlobalPosition;

        // Spawn material-aware debris for each voxel position
        int maxDebris = Mathf.Min(_voxelPositions.Count, 20);
        for (int i = 0; i < maxDebris; i++)
        {
            Vector3 voxelWorld = MathHelpers.MicrovoxelToWorld(_voxelPositions[i]);
            Color color = i < _voxelColors.Count ? _voxelColors[i] : new Color(0.5f, 0.4f, 0.3f);
            VoxelMaterialType matType = i < _voxelMaterials.Count ? _voxelMaterials[i] : VoxelMaterialType.Stone;
            DebrisFX.SpawnDebris(fxParent, voxelWorld, color, center, 1, matType);
        }

        // Spawn a dust cloud at the shatter point, tinted to dominant material
        VoxelMaterialType dominantMat = GetDominantMaterial();
        DustFX.Spawn(fxParent, center, Mathf.Clamp(_voxelPositions.Count * 0.15f, 0.5f, 3f), dominantMat);

        ForceCleanup();
    }

    /// <summary>
    /// Spawns a small crumble particle shed from the chunk while falling.
    /// Picks a random voxel from the chunk and spawns a tiny debris piece at its
    /// world-space position (translated by the chunk's current transform).
    /// </summary>
    private void SpawnCrumbleParticle()
    {
        if (_voxelPositions.Count == 0)
        {
            return;
        }

        Node? fxParent = GetTree()?.Root;
        if (fxParent == null)
        {
            return;
        }

        // Pick a random surface voxel from the chunk
        int idx = (int)GD.Randi() % _voxelPositions.Count;
        Color color = idx < _voxelColors.Count ? _voxelColors[idx] : new Color(0.5f, 0.4f, 0.3f);
        VoxelMaterialType matType = idx < _voxelMaterials.Count ? _voxelMaterials[idx] : VoxelMaterialType.Stone;

        // Compute the world position of this voxel within the chunk's current transform
        Vector3 localPos = MathHelpers.MicrovoxelToWorld(_voxelPositions[idx]) - GlobalPosition;
        // The chunk has moved, so approximate the world position from the transform
        // Use a slight random offset for natural scatter
        Vector3 worldPos = GlobalPosition + localPos.Rotated(Vector3.Up, Rotation.Y) + new Vector3(
            (float)GD.RandRange(-0.1, 0.1),
            (float)GD.RandRange(-0.1, 0.1),
            (float)GD.RandRange(-0.1, 0.1));

        DebrisFX.SpawnDebris(fxParent, worldPos, color, GlobalPosition, 1, matType);
    }

    /// <summary>
    /// Spawns a dust cloud at the chunk's position when it first hits the ground.
    /// </summary>
    private void SpawnImpactDust()
    {
        Node? fxParent = GetTree()?.Root;
        if (fxParent == null)
        {
            return;
        }

        VoxelMaterialType dominantMat = GetDominantMaterial();
        float dustRadius = Mathf.Clamp(_voxelPositions.Count * 0.1f, 0.3f, 2f);
        DustFX.Spawn(fxParent, GlobalPosition, dustRadius, dominantMat);
    }

    /// <summary>
    /// Finds the most common material type in this chunk for tinting dust.
    /// </summary>
    private VoxelMaterialType GetDominantMaterial()
    {
        if (_voxelMaterials.Count == 0)
        {
            return VoxelMaterialType.Stone;
        }

        // Simple: just return the first material for small chunks,
        // scan for most common in larger ones
        if (_voxelMaterials.Count <= 3)
        {
            return _voxelMaterials[0];
        }

        VoxelMaterialType best = _voxelMaterials[0];
        int bestCount = 0;
        Dictionary<VoxelMaterialType, int> counts = new();
        for (int i = 0; i < _voxelMaterials.Count; i++)
        {
            VoxelMaterialType m = _voxelMaterials[i];
            counts.TryGetValue(m, out int c);
            counts[m] = c + 1;
            if (c + 1 > bestCount)
            {
                bestCount = c + 1;
                best = m;
            }
        }
        return best;
    }

    private void ForceCleanup()
    {
        ActiveChunks.Remove(this);
        QueueFree();
    }

    public override void _ExitTree()
    {
        ActiveChunks.Remove(this);
    }

    private static void CleanupExcess()
    {
        // Remove any chunks that have been freed
        for (int i = ActiveChunks.Count - 1; i >= 0; i--)
        {
            if (!IsInstanceValid(ActiveChunks[i]))
            {
                ActiveChunks.RemoveAt(i);
            }
        }
    }

    // ── Connected component grouping ────────────────────────────────────

    /// <summary>
    /// Groups a set of disconnected voxel positions into connected components using BFS.
    /// Each resulting list is a separate island of connected voxels.
    /// </summary>
    public static List<List<Vector3I>> GroupConnectedComponents(HashSet<Vector3I> disconnected)
    {
        List<List<Vector3I>> components = new();
        HashSet<Vector3I> visited = new();

        foreach (Vector3I start in disconnected)
        {
            if (visited.Contains(start))
            {
                continue;
            }

            // BFS from this voxel
            List<Vector3I> component = new();
            Queue<Vector3I> frontier = new();
            frontier.Enqueue(start);
            visited.Add(start);

            while (frontier.Count > 0)
            {
                Vector3I current = frontier.Dequeue();
                component.Add(current);

                for (int d = 0; d < FaceDirections.Length; d++)
                {
                    Vector3I neighbor = current + FaceDirections[d];
                    if (disconnected.Contains(neighbor) && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        frontier.Enqueue(neighbor);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }
}
