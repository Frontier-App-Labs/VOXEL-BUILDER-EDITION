using Godot;
using System.Threading.Tasks;
using VoxelSiege.Core;
using VoxelSiege.Utility;

namespace VoxelSiege.Voxel;

public partial class VoxelChunk : Node3D
{
    private static ShaderMaterial? _cachedOpaqueShaderMaterial;
    private static StandardMaterial3D? _cachedTransparentMaterial;

    private static ShaderMaterial OpaqueShaderMaterial
    {
        get
        {
            if (_cachedOpaqueShaderMaterial == null)
            {
                _cachedOpaqueShaderMaterial = CreateOpaqueShaderMaterial();
            }

            return _cachedOpaqueShaderMaterial;
        }
    }

    /// <summary>
    /// Returns the shared opaque shader material so that VoxelWorld can set
    /// atlas-related uniforms (material_atlas, use_material_atlas, atlas_tile_size)
    /// after the texture atlas has been built.
    /// </summary>
    public static ShaderMaterial GetSharedOpaqueShaderMaterial() => OpaqueShaderMaterial;

    private static StandardMaterial3D TransparentMaterial
    {
        get
        {
            if (_cachedTransparentMaterial == null)
            {
                _cachedTransparentMaterial = CreateChunkMaterial(true);
            }

            return _cachedTransparentMaterial;
        }
    }

    private readonly Voxel[] _voxels = new Voxel[GameConfig.ChunkSize * GameConfig.ChunkSize * GameConfig.ChunkSize];
    private MeshInstance3D? _opaqueMeshInstance;
    private MeshInstance3D? _transparentMeshInstance;
    private StaticBody3D? _collisionBody;
    private CollisionShape3D? _collisionShape;
    // Fix #5: Second collision shape for transparent mesh (glass/ice)
    private CollisionShape3D? _transparentCollisionShape;
    private bool _meshingInProgress;
    private bool _meshQueued;
    private MeshBuildResult? _pendingResult;
    private VoxelWorld? _world;
    private bool _collisionPending;
    private ArrayMesh? _collisionSourceMesh;
    // Fix #5: Separate pending transparent collision mesh
    private ArrayMesh? _transparentCollisionSourceMesh;

    public Vector3I ChunkCoords { get; private set; }
    public bool IsDirty { get; private set; } = true;

    public override void _Ready()
    {
        EnsureNodes();
    }

    public void Initialize(VoxelWorld world, Vector3I chunkCoords)
    {
        _world = world;
        ChunkCoords = chunkCoords;
        Position = MathHelpers.MicrovoxelToWorld(chunkCoords * GameConfig.ChunkSize);
        Name = $"Chunk_{chunkCoords.X}_{chunkCoords.Y}_{chunkCoords.Z}";
        EnsureNodes();
        Visible = true;
        IsDirty = true;
        // Fix #8: Disable _Process on idle chunks
        SetProcess(false);
    }

    public void ResetChunk()
    {
        for (int index = 0; index < _voxels.Length; index++)
        {
            _voxels[index] = Voxel.Air;
        }

        ChunkCoords = default;
        IsDirty = true;
        _meshQueued = false;
        _meshingInProgress = false;
        _pendingResult = null;
        _collisionPending = false;
        _collisionSourceMesh = null;
        _transparentCollisionSourceMesh = null;
        if (_opaqueMeshInstance != null)
        {
            _opaqueMeshInstance.Mesh = null;
        }

        if (_transparentMeshInstance != null)
        {
            _transparentMeshInstance.Mesh = null;
        }

        if (_collisionShape != null)
        {
            _collisionShape.Shape = null;
        }

        if (_transparentCollisionShape != null)
        {
            _transparentCollisionShape.Shape = null;
        }

        // Fix #8: Disable _Process on reset chunks
        SetProcess(false);
    }

    public Voxel GetVoxel(Vector3I localPosition)
    {
        return _voxels[MathHelpers.LocalToFlatIndex(localPosition)];
    }

    public void SetVoxel(Vector3I localPosition, Voxel voxel)
    {
        _voxels[MathHelpers.LocalToFlatIndex(localPosition)] = voxel;
        IsDirty = true;
    }

    public bool IsInside(Vector3I localPosition)
    {
        return localPosition.X >= 0 && localPosition.Y >= 0 && localPosition.Z >= 0
            && localPosition.X < GameConfig.ChunkSize
            && localPosition.Y < GameConfig.ChunkSize
            && localPosition.Z < GameConfig.ChunkSize;
    }

    public async Task QueueRemeshAsync(VoxelTextureAtlas atlas)
    {
        if (_world == null)
        {
            return;
        }

        if (_meshingInProgress)
        {
            _meshQueued = true;
            return;
        }

        _meshingInProgress = true;
        _meshQueued = false;
        IsDirty = false;

        VoxelChunkSnapshot snapshot = _world.AcquireSnapshot(ChunkCoords);
        MeshBuildResult result = await Task.Run(() => ChunkMesher.Build(snapshot, atlas));
        _world.ReturnSnapshot(snapshot);
        _pendingResult = result;
        CallDeferred(nameof(ApplyPendingMesh));
    }

    public override void _Process(double delta)
    {
        // Deferred collision shape creation: spread cost over frames
        if (_collisionPending)
        {
            EnsureNodes();
            if (_collisionSourceMesh != null)
            {
                _collisionShape!.Shape = _collisionSourceMesh.CreateTrimeshShape();
            }
            else
            {
                _collisionShape!.Shape = null;
            }

            // Fix #5: Create collision shape for transparent mesh too (glass/ice)
            if (_transparentCollisionSourceMesh != null)
            {
                _transparentCollisionShape!.Shape = _transparentCollisionSourceMesh.CreateTrimeshShape();
            }
            else
            {
                _transparentCollisionShape!.Shape = null;
            }

            _collisionPending = false;
            _collisionSourceMesh = null;
            _transparentCollisionSourceMesh = null;
            // Fix #8: Disable _Process now that collision is done
            SetProcess(false);
        }
    }

    private void EnsureNodes()
    {
        _opaqueMeshInstance ??= GetNodeOrNull<MeshInstance3D>("OpaqueMesh");
        if (_opaqueMeshInstance == null)
        {
            _opaqueMeshInstance = new MeshInstance3D();
            _opaqueMeshInstance.Name = "OpaqueMesh";
            _opaqueMeshInstance.MaterialOverride = OpaqueShaderMaterial;
            AddChild(_opaqueMeshInstance);
        }

        _transparentMeshInstance ??= GetNodeOrNull<MeshInstance3D>("TransparentMesh");
        if (_transparentMeshInstance == null)
        {
            _transparentMeshInstance = new MeshInstance3D();
            _transparentMeshInstance.Name = "TransparentMesh";
            _transparentMeshInstance.MaterialOverride = TransparentMaterial;
            AddChild(_transparentMeshInstance);
        }

        _collisionBody ??= GetNodeOrNull<StaticBody3D>("CollisionBody");
        if (_collisionBody == null)
        {
            _collisionBody = new StaticBody3D();
            _collisionBody.Name = "CollisionBody";
            AddChild(_collisionBody);
        }

        _collisionShape ??= _collisionBody.GetNodeOrNull<CollisionShape3D>("CollisionShape");
        if (_collisionShape == null)
        {
            _collisionShape = new CollisionShape3D();
            _collisionShape.Name = "CollisionShape";
            _collisionBody.AddChild(_collisionShape);
        }

        // Fix #5: Ensure transparent collision shape node exists
        _transparentCollisionShape ??= _collisionBody.GetNodeOrNull<CollisionShape3D>("TransparentCollisionShape");
        if (_transparentCollisionShape == null)
        {
            _transparentCollisionShape = new CollisionShape3D();
            _transparentCollisionShape.Name = "TransparentCollisionShape";
            _collisionBody.AddChild(_transparentCollisionShape);
        }
    }

    private void ApplyPendingMesh()
    {
        if (_pendingResult == null)
        {
            _meshingInProgress = false;
            return;
        }

        EnsureNodes();
        _opaqueMeshInstance!.Mesh = _pendingResult.OpaqueMesh;
        _transparentMeshInstance!.Mesh = _pendingResult.TransparentMesh;

        // Defer collision shape creation to next frame via _Process to avoid blocking
        _collisionSourceMesh = _pendingResult.OpaqueMesh;
        // Fix #5: Also use transparent mesh for collision
        _transparentCollisionSourceMesh = _pendingResult.TransparentMesh;
        _collisionPending = _collisionSourceMesh != null || _transparentCollisionSourceMesh != null;
        if (!_collisionPending)
        {
            _collisionShape!.Shape = null;
            _transparentCollisionShape!.Shape = null;
        }
        else
        {
            // Fix #8: Enable _Process only when collision work is pending
            SetProcess(true);
        }

        _pendingResult = null;

        // Fix #3: Check for queued remesh BEFORE clearing _meshingInProgress.
        // This prevents a window where a new remesh request could slip in
        // between clearing the flag and starting the queued remesh.
        if (_meshQueued && _world != null)
        {
            _meshQueued = false;
            // Set _meshingInProgress = false so QueueRemeshAsync can acquire it.
            // No race here because this runs on the main thread (CallDeferred),
            // and QueueRemeshAsync's guard check also runs on the main thread
            // before any await.
            _meshingInProgress = false;
            _ = QueueRemeshAsync(_world.TextureAtlas);
        }
        else
        {
            _meshingInProgress = false;
        }
    }

    // Fix #7: Compute atlas tile size from actual atlas dimensions
    private static ShaderMaterial CreateOpaqueShaderMaterial()
    {
        Shader? shader = GD.Load<Shader>("res://assets/shaders/voxel_triplanar.gdshader");
        if (shader != null)
        {
            ShaderMaterial mat = new ShaderMaterial();
            mat.Shader = shader;
            mat.SetShaderParameter("triplanar_scale", 1.0f);
            mat.SetShaderParameter("ao_strength", 0.65f);
            mat.SetShaderParameter("edge_highlight_strength", 0.3f);
            mat.SetShaderParameter("noise_strength", 0.04f);
            mat.SetShaderParameter("damage_blend", 0.0f);
            mat.SetShaderParameter("player_tint", new Color(1f, 1f, 1f, 1f));
            // Fix #7: Compute tile size from VoxelTextureAtlas defaults instead of hardcoding.
            // Default atlas is 8 tiles per row; total material count determines rows.
            // Use 1/TilesPerRow for both axes as the default (will be overridden by
            // WireAtlasToShader when generated textures are available).
            const int defaultTilesPerRow = 8;
            float tileSize = 1f / defaultTilesPerRow;
            mat.SetShaderParameter("atlas_tile_size", new Vector2(tileSize, tileSize));
            return mat;
        }

        // Fallback: if shader cannot be loaded, use StandardMaterial3D with vertex colors
        GD.PushWarning("VoxelChunk: voxel_triplanar.gdshader not found, falling back to StandardMaterial3D");
        ShaderMaterial fallback = new ShaderMaterial();
        // Return a StandardMaterial3D wrapped in the same type is not possible,
        // so create a minimal inline shader that reads vertex color.
        Shader inlineShader = new Shader();
        inlineShader.Code = "shader_type spatial;\nvoid vertex() {}\nvoid fragment() { ALBEDO = COLOR.rgb; }";
        fallback.Shader = inlineShader;
        return fallback;
    }

    private static StandardMaterial3D CreateChunkMaterial(bool transparent)
    {
        StandardMaterial3D material = new StandardMaterial3D();
        material.VertexColorUseAsAlbedo = true;
        material.Roughness = transparent ? 0.05f : 0.9f;
        material.Metallic = transparent ? 0f : 0.05f;
        material.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
        material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
        if (transparent)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        }

        return material;
    }
}
