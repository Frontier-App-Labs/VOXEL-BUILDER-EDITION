using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Core;

namespace VoxelSiege.Voxel;

public sealed class VoxelChunkSnapshot
{
    private readonly int _paddedSize;
    private readonly Voxel[] _voxels;

    public VoxelChunkSnapshot(int chunkSize)
    {
        ChunkSize = chunkSize;
        _paddedSize = chunkSize + 2;
        _voxels = new Voxel[_paddedSize * _paddedSize * _paddedSize];
    }

    public int ChunkSize { get; }

    public void Set(Vector3I paddedPosition, Voxel voxel)
    {
        int x = paddedPosition.X + 1;
        int y = paddedPosition.Y + 1;
        int z = paddedPosition.Z + 1;
        int index = x + (_paddedSize * (y + (_paddedSize * z)));
        _voxels[index] = voxel;
    }

    public Voxel Get(Vector3I paddedPosition)
    {
        int x = paddedPosition.X + 1;
        int y = paddedPosition.Y + 1;
        int z = paddedPosition.Z + 1;
        int index = x + (_paddedSize * (y + (_paddedSize * z)));
        return _voxels[index];
    }
}

public sealed class MeshBuildResult
{
    public MeshBuildResult(ArrayMesh? opaqueMesh, ArrayMesh? transparentMesh)
    {
        OpaqueMesh = opaqueMesh;
        TransparentMesh = transparentMesh;
    }

    public ArrayMesh? OpaqueMesh { get; }
    public ArrayMesh? TransparentMesh { get; }
}

internal readonly record struct GreedyMaskCell(Voxel Voxel, bool FrontFace, VoxelFaceDirection FaceDirection, bool Transparent);

internal sealed class ChunkGeometryBuffer
{
    private readonly List<Vector3> _vertices = new List<Vector3>();
    private readonly List<Vector3> _normals = new List<Vector3>();
    private readonly List<Vector2> _uvs = new List<Vector2>();
    private readonly List<Color> _colors = new List<Color>();
    private readonly List<int> _indices = new List<int>();

    public bool IsEmpty => _vertices.Count == 0;

    public void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal, Rect2 uvRect, Color color)
    {
        int start = _vertices.Count;
        _vertices.Add(v0);
        _vertices.Add(v1);
        _vertices.Add(v2);
        _vertices.Add(v3);

        _normals.Add(normal);
        _normals.Add(normal);
        _normals.Add(normal);
        _normals.Add(normal);

        _colors.Add(color);
        _colors.Add(color);
        _colors.Add(color);
        _colors.Add(color);

        _uvs.Add(uvRect.Position);
        _uvs.Add(new Vector2(uvRect.Position.X, uvRect.End.Y));
        _uvs.Add(uvRect.End);
        _uvs.Add(new Vector2(uvRect.End.X, uvRect.Position.Y));

        _indices.Add(start);
        _indices.Add(start + 1);
        _indices.Add(start + 2);
        _indices.Add(start);
        _indices.Add(start + 2);
        _indices.Add(start + 3);
    }

    public ArrayMesh? ToArrayMesh()
    {
        if (IsEmpty)
        {
            return null;
        }

        Godot.Collections.Array arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = _uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();

        ArrayMesh mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}

public static class ChunkMesher
{
    public static MeshBuildResult Build(VoxelChunkSnapshot snapshot, VoxelTextureAtlas atlas)
    {
        ChunkGeometryBuffer opaqueBuffer = new ChunkGeometryBuffer();
        ChunkGeometryBuffer transparentBuffer = new ChunkGeometryBuffer();
        int size = snapshot.ChunkSize;
        GreedyMaskCell?[] mask = new GreedyMaskCell?[size * size];
        int[] x = new int[3];
        int[] q = new int[3];

        for (int axis = 0; axis < 3; axis++)
        {
            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;
            Array.Clear(q, 0, q.Length);
            q[axis] = 1;

            for (x[axis] = -1; x[axis] < size;)
            {
                int maskIndex = 0;
                for (x[v] = 0; x[v] < size; x[v]++)
                {
                    for (x[u] = 0; x[u] < size; x[u]++)
                    {
                        Vector3I aPos = new Vector3I(x[0], x[1], x[2]);
                        Vector3I bPos = new Vector3I(x[0] + q[0], x[1] + q[1], x[2] + q[2]);
                        Voxel a = snapshot.Get(aPos);
                        Voxel b = snapshot.Get(bPos);
                        mask[maskIndex++] = CreateMaskCell(a, b, axis);
                    }
                }

                x[axis]++;
                maskIndex = 0;
                for (int j = 0; j < size; j++)
                {
                    for (int i = 0; i < size;)
                    {
                        GreedyMaskCell? cell = mask[maskIndex];
                        if (cell is null)
                        {
                            i++;
                            maskIndex++;
                            continue;
                        }

                        int width;
                        for (width = 1; i + width < size && mask[maskIndex + width] == cell; width++)
                        {
                        }

                        int height;
                        bool done = false;
                        for (height = 1; j + height < size; height++)
                        {
                            for (int offset = 0; offset < width; offset++)
                            {
                                if (mask[maskIndex + offset + (height * size)] != cell)
                                {
                                    done = true;
                                    break;
                                }
                            }

                            if (done)
                            {
                                break;
                            }
                        }

                        x[u] = i;
                        x[v] = j;
                        int[] du = new int[3];
                        int[] dv = new int[3];
                        du[u] = width;
                        dv[v] = height;
                        AppendQuad(cell.Value, x, du, dv, axis, atlas, opaqueBuffer, transparentBuffer);

                        for (int l = 0; l < height; l++)
                        {
                            for (int k = 0; k < width; k++)
                            {
                                mask[maskIndex + k + (l * size)] = null;
                            }
                        }

                        i += width;
                        maskIndex += width;
                    }
                }
            }
        }

        return new MeshBuildResult(opaqueBuffer.ToArrayMesh(), transparentBuffer.ToArrayMesh());
    }

    private static GreedyMaskCell? CreateMaskCell(Voxel a, Voxel b, int axis)
    {
        bool aTransparent = VoxelMaterials.IsTransparent(a.Material);
        bool bTransparent = VoxelMaterials.IsTransparent(b.Material);
        bool aVisible = a.IsSolid && (!b.IsSolid || aTransparent != bTransparent);
        bool bVisible = b.IsSolid && (!a.IsSolid || aTransparent != bTransparent);

        if (aVisible == bVisible)
        {
            return null;
        }

        if (aVisible)
        {
            return new GreedyMaskCell(a, true, ResolveFaceDirection(axis, true), aTransparent);
        }

        return new GreedyMaskCell(b, false, ResolveFaceDirection(axis, false), bTransparent);
    }

    private static VoxelFaceDirection ResolveFaceDirection(int axis, bool frontFace)
    {
        return axis switch
        {
            0 => frontFace ? VoxelFaceDirection.Right : VoxelFaceDirection.Left,
            1 => frontFace ? VoxelFaceDirection.Top : VoxelFaceDirection.Bottom,
            _ => frontFace ? VoxelFaceDirection.Front : VoxelFaceDirection.Back,
        };
    }

    private static void AppendQuad(
        GreedyMaskCell cell,
        int[] x,
        int[] du,
        int[] dv,
        int axis,
        VoxelTextureAtlas atlas,
        ChunkGeometryBuffer opaqueBuffer,
        ChunkGeometryBuffer transparentBuffer)
    {
        Vector3 basePosition = new Vector3(x[0], x[1], x[2]) * GameConfig.MicrovoxelMeters;
        Vector3 duVector = new Vector3(du[0], du[1], du[2]) * GameConfig.MicrovoxelMeters;
        Vector3 dvVector = new Vector3(dv[0], dv[1], dv[2]) * GameConfig.MicrovoxelMeters;
        Vector3 normal = axis switch
        {
            0 => cell.FrontFace ? Vector3.Right : Vector3.Left,
            1 => cell.FrontFace ? Vector3.Up : Vector3.Down,
            _ => cell.FrontFace ? Vector3.Forward : Vector3.Back,
        };

        Vector3 v0;
        Vector3 v1;
        Vector3 v2;
        Vector3 v3;
        if (cell.FrontFace)
        {
            v0 = basePosition;
            v1 = basePosition + dvVector;
            v2 = basePosition + duVector + dvVector;
            v3 = basePosition + duVector;
        }
        else
        {
            v0 = basePosition;
            v1 = basePosition + duVector;
            v2 = basePosition + duVector + dvVector;
            v3 = basePosition + dvVector;
        }

        Rect2 uvRect = atlas.GetUvRect(cell.Voxel.Material, cell.FaceDirection);
        Color color = VoxelMaterials.GetPreviewColor(cell.Voxel.Material);
        if (cell.Transparent)
        {
            transparentBuffer.AddQuad(v0, v1, v2, v3, normal, uvRect, color);
        }
        else
        {
            opaqueBuffer.AddQuad(v0, v1, v2, v3, normal, uvRect, color);
        }
    }
}
