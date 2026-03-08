using Godot;
using System.Threading.Tasks;
using VoxelSiege.Core;
using VoxelSiege.Utility;

namespace VoxelSiege.Voxel;

public partial class VoxelChunk : Node3D
{
    private static readonly StandardMaterial3D OpaqueMaterial = CreateChunkMaterial(false);
    private static readonly StandardMaterial3D TransparentMaterial = CreateChunkMaterial(true);
    private readonly Voxel[] _voxels = new Voxel[GameConfig.ChunkSize * GameConfig.ChunkSize * GameConfig.ChunkSize];
    private MeshInstance3D? _opaqueMeshInstance;
    private MeshInstance3D? _transparentMeshInstance;
    private StaticBody3D? _collisionBody;
    private CollisionShape3D? _collisionShape;
    private bool _meshingInProgress;
    private bool _meshQueued;
    private MeshBuildResult? _pendingResult;
    private VoxelWorld? _world;

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

        VoxelChunkSnapshot snapshot = _world.CreateSnapshot(ChunkCoords);
        MeshBuildResult result = await Task.Run(() => ChunkMesher.Build(snapshot, atlas));
        _pendingResult = result;
        CallDeferred(nameof(ApplyPendingMesh));
    }

    private void EnsureNodes()
    {
        _opaqueMeshInstance ??= GetNodeOrNull<MeshInstance3D>("OpaqueMesh");
        if (_opaqueMeshInstance == null)
        {
            _opaqueMeshInstance = new MeshInstance3D();
            _opaqueMeshInstance.Name = "OpaqueMesh";
            _opaqueMeshInstance.MaterialOverride = OpaqueMaterial;
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
    }

    private async void ApplyPendingMesh()
    {
        if (_pendingResult == null)
        {
            _meshingInProgress = false;
            return;
        }

        EnsureNodes();
        _opaqueMeshInstance!.Mesh = _pendingResult.OpaqueMesh;
        _transparentMeshInstance!.Mesh = _pendingResult.TransparentMesh;
        _collisionShape!.Shape = _pendingResult.OpaqueMesh?.CreateTrimeshShape();
        _pendingResult = null;
        _meshingInProgress = false;

        if (_meshQueued && _world != null)
        {
            _meshQueued = false;
            await QueueRemeshAsync(_world.TextureAtlas);
        }
    }

    private static StandardMaterial3D CreateChunkMaterial(bool transparent)
    {
        StandardMaterial3D material = new StandardMaterial3D();
        material.VertexColorUseAsAlbedo = true;
        material.Roughness = transparent ? 0.05f : 0.9f;
        material.Metallic = transparent ? 0f : 0.05f;
        material.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
        if (transparent)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        }

        return material;
    }
}
